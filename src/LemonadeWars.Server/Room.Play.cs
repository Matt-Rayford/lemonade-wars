using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LemonadeWars.Server;

// In-game action handling, bot-turn pacing, and per-seat broadcasts.
public sealed partial class Room
{
    private const int MaxBotSteps = 20000;

    /// <summary>Seats already alerted for their current wait — one nudge per turn edge.</summary>
    private readonly HashSet<int> _alertedSeats = new();
    private bool _botPumpActive;

    /// <summary>Kick the bot pump (used after rehydration when a bot is up).</summary>
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
        // Seats can only ever act as themselves.
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
    /// Paced bot turns: one action per BOT_DELAY_MS with a broadcast after each, so humans
    /// watch bot turns unfold instead of the whole thing flashing past. At most one pump
    /// runs per room; human actions interleave safely through the room lock.
    /// </summary>
    private async Task EnsureBotPumpAsync()
    {
        lock (_sync)
        {
            if (_botPumpActive || _game == null)
            {
                return;
            }
            _botPumpActive = true;
        }

        try
        {
            for (int steps = 0; steps < MaxBotSteps; steps++)
            {
                var events = new List<GameEvent>();
                lock (_sync)
                {
                    if (_game == null || _game.State.Stage == GameStage.Finished)
                    {
                        return;
                    }
                    var acting = _game.ActingPlayers();
                    // Humans answer first: while any human seat is being awaited
                    // (response window, discard, ...), bots hold their responses so
                    // the pause is unmistakable at the table.
                    if (acting.Any(a => !_bots.ContainsKey(a)))
                    {
                        return;
                    }
                    int actor = acting.FirstOrDefault(a => _bots.ContainsKey(a), -1);
                    if (actor < 0)
                    {
                        return;
                    }
                    var action = _bots[actor].Choose(_game, actor);
                    events.AddRange(_game.Apply(action));
                    PersistAction(action);
                }
                await BroadcastGameAsync(events);
                int delayMs = _botDelayOverrideMs >= 0 ? _botDelayOverrideMs : _botDelayMs;
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

    private async Task BroadcastGameAsync(IReadOnlyList<GameEvent> events)
    {
        List<(Seat Seat, UpdateMessage Message)> outbox = new();
        List<(string PlayerId, string Code)>? alerts = null;
        lock (_sync)
        {
            if (_game == null)
            {
                return;
            }
            var acting = _game.ActingPlayers();

            // Turn alerts, rising-edge: a seat that is newly awaited AND away from
            // the table gets one nudge on its player's other connections. Sitting at
            // the table (or ceasing to be awaited) re-arms the alert for next time.
            foreach (var seat in _seats.Where(s => !s.IsBot))
            {
                if (!acting.Contains(seat.Index) || seat.Connected)
                {
                    _alertedSeats.Remove(seat.Index);
                }
                else if (seat.PlayerId != null && _alertedSeats.Add(seat.Index))
                {
                    (alerts ??= new List<(string, string)>()).Add((seat.PlayerId, Code));
                }
            }

            foreach (var seat in _seats.Where(s => !s.IsBot))
            {
                var update = new UpdateMessage
                {
                    Events = events.Select(e => WireCodec.EncodeEventFor(e, seat.Index)).ToList(),
                    View = JObject.FromObject(_game.ViewFor(seat.Index),
                        JsonSerializer.Create(WireCodec.Settings)),
                };
                if (acting.Contains(seat.Index))
                {
                    update.Moves = _game.LegalMovesFor(seat.Index)
                        .Select(m => new MoveOption
                        {
                            Label = MoveDescriber.Describe(_game, m),
                            Action = WireCodec.EncodeAction(m),
                        })
                        .ToList();
                }
                outbox.Add((seat, update));
            }
        }
        if (alerts != null && _turnNotifier != null)
        {
            foreach (var (playerId, code) in alerts)
            {
                _turnNotifier(playerId, code);
            }
        }
        await Task.WhenAll(outbox.Select(o => o.Seat.SendAsync(o.Message)));
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
