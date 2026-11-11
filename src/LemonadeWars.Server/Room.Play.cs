using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LemonadeWars.Server;

public sealed partial class Room
{
    private const int MaxBotSteps = 20000;
    /// <summary>
    /// How long clients spend showing a die (tumble, settle, hold). The server cannot see
    /// the animation, so bots hold this long after any roll before the next turn starts.
    /// </summary>
    private const int RollTheatreMs = 2800;
    /// <summary>Per earner: the client's bill pile (0.25s in, 0.6s hold, 0.5s to the bar, 0.5s breath).</summary>
    private const int EarningsTheatreMs = 1850;

    /// <summary>Seats already alerted for their current wait: one nudge per turn edge.</summary>
    private readonly HashSet<int> _alertedSeats = new();
    private bool _botPumpActive;
    private DateTime _rollTheatreUntil = DateTime.MinValue;

    /// <summary>0 means instant bots (tests), which also skips the roll theatre hold.</summary>
    private int BotDelayMs => _botDelayOverrideMs >= 0 ? _botDelayOverrideMs : _botDelayMs;

    /// <summary>For rehydrated rooms, where a bot may have been mid-turn when the server died.</summary>
    public void PumpBots() => _ = EnsureBotPumpAsync();

    public async Task HandleActionAsync(int seatIndex, JObject actionJson)
    {
        GameAction action;
        try
        {
            action = WireCodec.DecodeAction(actionJson);
        }
        catch (JsonSerializationException e)
        {
            await SendErrorAsync(seatIndex, $"Bad action: {e.Message}");
            return;
        }
        // Whatever the client claimed, a seat can only act as itself.
        action.PlayerId = seatIndex;

        var events = new List<GameEvent>();
        lock (_sync)
        {
            if (_game == null)
            {
                _ = SendErrorAsync(seatIndex, "The game has not started.");
                return;
            }
            try
            {
                events.AddRange(_game.Apply(action));
                PersistAction(action);
            }
            catch (InvalidActionException e)
            {
                _ = SendErrorAsync(seatIndex, e.Message);
                return;
            }
        }

        await BroadcastThenPumpAsync(events);
    }

    private async Task BroadcastThenPumpAsync(List<GameEvent> events)
    {
        await BroadcastGameAsync(events);
        await EnsureBotPumpAsync();
    }

    /// <summary>
    /// Paced bot turns: one action per bot delay with a broadcast after each, so humans watch
    /// bot turns unfold. At most one pump runs per room; human actions interleave through the lock.
    /// </summary>
    private async Task EnsureBotPumpAsync()
    {
        if (!TryClaimBotPump())
        {
            return;
        }

        try
        {
            for (int steps = 0; steps < MaxBotSteps; steps++)
            {
                TimeSpan theatreLeft = RollTheatreRemaining();
                if (theatreLeft > TimeSpan.Zero)
                {
                    await Task.Delay(theatreLeft);
                }

                var events = TakeNextBotAction();
                if (events == null)
                {
                    return;
                }
                await BroadcastGameAsync(events);
                int delayMs = BotDelayMs;
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs);
                }
            }
        }
        finally
        {
            lock (_sync)
            {
                _botPumpActive = false;
            }
        }
    }

    private bool TryClaimBotPump()
    {
        lock (_sync)
        {
            if (_botPumpActive || _game == null)
            {
                return false;
            }
            _botPumpActive = true;
            return true;
        }
    }

    private TimeSpan RollTheatreRemaining()
    {
        lock (_sync)
        {
            return _rollTheatreUntil - DateTime.UtcNow;
        }
    }

    /// <summary>Null when no bot should act now: the game is over, or a human is being awaited.</summary>
    private List<GameEvent>? TakeNextBotAction()
    {
        lock (_sync)
        {
            if (_game == null || _game.State.Stage == GameStage.Finished)
            {
                return null;
            }
            var acting = _game.ActingPlayers();
            // Humans answer first: bots hold back while any human is awaited, so the pause is unmistakable.
            if (acting.Any(a => !_bots.ContainsKey(a)))
            {
                return null;
            }
            int actor = acting.FirstOrDefault(a => _bots.ContainsKey(a), -1);
            if (actor < 0)
            {
                return null;
            }
            var action = _bots[actor].Choose(_game, actor);
            var events = new List<GameEvent>(_game.Apply(action));
            PersistAction(action);
            return events;
        }
    }

    private async Task BroadcastGameAsync(IReadOnlyList<GameEvent> events)
    {
        List<(Seat Seat, UpdateMessage Message)> outbox;
        List<(string PlayerId, string Code)> alerts;
        lock (_sync)
        {
            if (_game == null)
            {
                return;
            }
            HoldBotsForRollTheatre(events);
            var acting = _game.ActingPlayers();
            alerts = CollectTurnAlerts(acting);
            outbox = BuildSeatUpdates(_game, events, acting);
        }
        if (_turnNotifier != null)
        {
            foreach (var (playerId, code) in alerts)
            {
                _turnNotifier(playerId, code);
            }
        }
        await Task.WhenAll(outbox.Select(o => o.Seat.SendAsync(o.Message)));
    }

    // ---- BroadcastGameAsync steps, all run with _sync held

    private void HoldBotsForRollTheatre(IReadOnlyList<GameEvent> events)
    {
        if (BotDelayMs <= 0 || !events.Any(e => e is SaleRolled || e is DieRerolled || e is RollModified))
        {
            return;
        }
        // The client plays the die, then one bill pile per earner, one at a time.
        int earners = events.Where(e => e is StandSold || (e is MoneyChanged m && m.Amount > 0))
            .Select(e => e is StandSold sold ? sold.PlayerId : ((MoneyChanged)e).PlayerId)
            .Distinct().Count();
        _rollTheatreUntil = DateTime.UtcNow.AddMilliseconds(RollTheatreMs + earners * EarningsTheatreMs);
    }

    /// <summary>
    /// Rising-edge: a human seat that is newly awaited AND away from the table gets one nudge.
    /// Sitting at the table, or ceasing to be awaited, re-arms the alert.
    /// </summary>
    private List<(string PlayerId, string Code)> CollectTurnAlerts(IReadOnlyList<int> acting)
    {
        var alerts = new List<(string, string)>();
        foreach (var seat in _seats.Where(s => !s.IsBot))
        {
            if (!acting.Contains(seat.Index) || seat.Connected)
            {
                _alertedSeats.Remove(seat.Index);
            }
            else if (seat.PlayerId != null && _alertedSeats.Add(seat.Index))
            {
                alerts.Add((seat.PlayerId, Code));
            }
        }
        return alerts;
    }

    private List<(Seat Seat, UpdateMessage Message)> BuildSeatUpdates(Game game,
        IReadOnlyList<GameEvent> events, IReadOnlyList<int> acting)
    {
        var outbox = new List<(Seat, UpdateMessage)>();
        foreach (var seat in _seats.Where(s => !s.IsBot))
        {
            var update = new UpdateMessage
            {
                Events = events.Select(e => WireCodec.EncodeEventFor(e, seat.Index)).ToList(),
                View = JObject.FromObject(game.ViewFor(seat.Index),
                    JsonSerializer.Create(WireCodec.Settings)),
            };
            if (acting.Contains(seat.Index))
            {
                update.Moves = game.LegalMovesFor(seat.Index)
                    .Select(m => new MoveOption
                    {
                        Label = MoveDescriber.Describe(game, m),
                        Action = WireCodec.EncodeAction(m),
                    })
                    .ToList();
            }
            outbox.Add((seat, update));
        }
        return outbox;
    }

    private void BroadcastRoom(List<Seat> seats)
    {
        bool started = Started;
        foreach (var seat in seats.Where(s => !s.IsBot))
        {
            var message = new RoomMessage
            {
                Code = Code,
                YourSeat = seat.Index,
                Token = seat.Token,
                Started = started,
                Speed = BotSpeed,
                Seats = seats.Select(s => new SeatInfo
                {
                    Seat = s.Index,
                    Name = s.Name,
                    IsBot = s.IsBot,
                    BotLevel = s.IsBot ? s.BotLevel : "",
                    Connected = s.IsBot || s.Connected,
                    Ready = s.Ready,
                }).ToList(),
            };
            _ = seat.SendAsync(message);
        }
    }

    private Task SendErrorAsync(int seatIndex, string message)
    {
        Seat? seat;
        lock (_sync)
        {
            seat = _seats.FirstOrDefault(s => s.Index == seatIndex);
        }
        return seat?.SendAsync(new ErrorMessage { Message = message }) ?? Task.CompletedTask;
    }
}
