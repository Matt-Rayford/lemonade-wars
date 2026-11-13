using System.Diagnostics;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Protocol;
using Newtonsoft.Json;

namespace LemonadeWars.Server;

// The action timer: every awaited human gets a countdown, and the table acts for whoever lets it run out.
public sealed partial class Room
{
    public const int NoActionTimer = 0;
    public const int DefaultActionTimerSeconds = 60;
    private static readonly int[] ActionTimerChoices = { NoActionTimer, 15, 30, 60 };

    private const int NoActor = -1;
    private const int SettleGraceSeconds = 15;
    private const int ClockTickMs = 100;
    /// <summary>One expiry settles everything the seat owes in a row: a discard, then the turn, then its roll window.</summary>
    private const int MaxActionsPerExpiry = 40;

    private readonly Dictionary<int, ActionClock> _clocks = new();
    private readonly IBot _absentStandIn = new GreedyBot();
    /// <summary>1000 outside tests, which shorten the timer's second to keep themselves fast.</summary>
    private readonly int _msPerTimerSecond;
    private bool _clockLoopActive;
    private int _stateVersion;

    /// <summary>Seconds an awaited player has per action; <see cref="NoActionTimer"/> = no limit.</summary>
    public int ActionTimerSeconds { get; private set; } = DefaultActionTimerSeconds;

    /// <summary>A lone human among bots keeps nobody waiting, so they play untimed whatever the setting.</summary>
    private bool IsOnActionTimer =>
        ActionTimerSeconds != NoActionTimer && _seats.Count(seat => !seat.IsBot) > 1;

    public string SetActionTimer(int requestingSeat, int seconds)
    {
        List<Seat> toNotify;
        lock (_sync)
        {
            if (requestingSeat != 0)
            {
                return "Only the host can change the action timer.";
            }
            if (_game != null)
            {
                return "The game already started.";
            }
            if (!ActionTimerChoices.Contains(seconds))
            {
                return "The action timer must be off, 15, 30, or 60 seconds.";
            }
            ActionTimerSeconds = seconds;
            toNotify = _seats.ToList();
        }
        BroadcastRoom(toNotify);
        return "";
    }

    /// <summary>The seat's client has finished showing what led to its wait: its countdown starts now.</summary>
    public void ReportTableSettled(int seatIndex, int stateVersion)
    {
        lock (_sync)
        {
            // A report about an older state is void: the update that replaced it is still playing out over there.
            if (stateVersion != _stateVersion ||
                !_clocks.TryGetValue(seatIndex, out var clock) || clock.Running)
            {
                return;
            }
            clock.Running = true;
        }
        BroadcastClocks();
    }

    // ---- BroadcastGameAsync step, run with _sync held

    /// <summary>
    /// Arms a clock for each newly awaited human, keeps the clock of one still facing the same
    /// wait, and stamps every outgoing update with the clocks as they now stand.
    /// </summary>
    private void SyncActionClocks(IReadOnlyList<int> acting, int actorSeat,
        IReadOnlyList<GameEvent> events, List<(Seat Seat, UpdateMessage Message)> outbox)
    {
        _stateVersion++;
        var awaited = IsOnActionTimer
            ? outbox.Where(entry => acting.Contains(entry.Seat.Index)).ToList()
            : new List<(Seat Seat, UpdateMessage Message)>();

        foreach (int seatIndex in _clocks.Keys.ToList())
        {
            if (awaited.All(entry => entry.Seat.Index != seatIndex))
            {
                _clocks.Remove(seatIndex);
            }
        }
        foreach (var (seat, update) in awaited)
        {
            string signature = string.Join("|",
                update.Moves.Select(move => move.Action.ToString(Formatting.None)));
            bool isSameWait = _clocks.TryGetValue(seat.Index, out var clock) &&
                seat.Index != actorSeat && clock.WaitSignature == signature &&
                !AsksAnew(events, seat.Index);
            if (!isSameWait)
            {
                _clocks[seat.Index] = new ActionClock
                {
                    Remaining = TimerSeconds(ActionTimerSeconds),
                    SettleGraceLeft = TimerSeconds(SettleGraceSeconds),
                    WaitSignature = signature,
                };
            }
        }

        var clocks = ClockInfos();
        foreach (var (_, update) in outbox)
        {
            update.StateVersion = _stateVersion;
            update.Clocks = clocks;
        }
    }

    /// <summary>A new window or decision can offer the very moves the last one did, so the moves alone cannot tell them apart.</summary>
    private static bool AsksAnew(IReadOnlyList<GameEvent> events, int seatIndex) =>
        events.Any(gameEvent =>
            (gameEvent is ResponseWindowOpened window && window.AwaitingPlayers.Contains(seatIndex)) ||
            (gameEvent is DecisionRequired decision && decision.PlayerId == seatIndex) ||
            (gameEvent is TurnStarted turn && turn.PlayerId == seatIndex));

    private TimeSpan TimerSeconds(int seconds) =>
        TimeSpan.FromMilliseconds((double)seconds * _msPerTimerSecond);

    private List<ActionClockInfo> ClockInfos() =>
        _clocks.OrderBy(entry => entry.Key)
            .Select(entry => new ActionClockInfo
            {
                Seat = entry.Key,
                SecondsLeft = Math.Max(0, entry.Value.Remaining.TotalMilliseconds / _msPerTimerSecond),
                Running = entry.Value.Running,
            })
            .ToList();

    // ---- the clock loop

    /// <summary>At most one loop per room, and only while a clock has someone to keep waiting.</summary>
    private async Task EnsureClockLoopAsync()
    {
        lock (_sync)
        {
            if (_clockLoopActive || !CanClocksAdvance())
            {
                return;
            }
            _clockLoopActive = true;
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            TimeSpan lastTick = TimeSpan.Zero;
            while (true)
            {
                await Task.Delay(ClockTickMs);
                TimeSpan now = stopwatch.Elapsed;
                bool keepTicking = AdvanceClocks(now - lastTick, out var expiryEvents, out bool anyStarted);
                lastTick = now;
                if (!keepTicking)
                {
                    return;
                }
                if (expiryEvents != null)
                {
                    await BroadcastGameAsync(expiryEvents);
                    // Not awaited: bot turns run long, and the clocks must keep ticking under them.
                    _ = EnsureBotPumpAsync();
                }
                else if (anyStarted)
                {
                    BroadcastClocks();
                }
            }
        }
        catch
        {
            lock (_sync)
            {
                _clockLoopActive = false;
            }
            throw;
        }
    }

    /// <summary>
    /// With no human at the table nobody is being kept waiting, so the clocks stop where they
    /// are; otherwise an abandoned game would skip its own turns forever and never go stale.
    /// </summary>
    private bool CanClocksAdvance() =>
        _game != null && !_retired && _clocks.Count > 0 &&
        _seats.Any(seat => !seat.IsBot && seat.Connected);

    /// <summary>
    /// Returns false once the loop should end, releasing it under the same lock that decided so:
    /// a clock armed a moment later then finds no loop and starts one.
    /// </summary>
    private bool AdvanceClocks(TimeSpan elapsed, out List<GameEvent>? expiryEvents, out bool anyStarted)
    {
        expiryEvents = null;
        anyStarted = false;
        lock (_sync)
        {
            if (!CanClocksAdvance())
            {
                _clockLoopActive = false;
                return false;
            }
            var expiredSeats = new List<int>();
            foreach (var (seatIndex, clock) in _clocks)
            {
                if (clock.Running)
                {
                    clock.Remaining -= elapsed;
                    if (clock.Remaining <= TimeSpan.Zero)
                    {
                        expiredSeats.Add(seatIndex);
                    }
                    continue;
                }
                clock.SettleGraceLeft -= elapsed;
                // An absent player's table is showing nothing, so there is nothing to wait for.
                bool isAway = !_seats.First(seat => seat.Index == seatIndex).Connected;
                if (isAway || clock.SettleGraceLeft <= TimeSpan.Zero)
                {
                    clock.Running = true;
                    anyStarted = true;
                }
            }
            foreach (int seatIndex in expiredSeats)
            {
                expiryEvents ??= new List<GameEvent>();
                ActForExpiredSeat(seatIndex, expiryEvents);
            }
            return true;
        }
    }

    private void ActForExpiredSeat(int seatIndex, List<GameEvent> events)
    {
        _clocks.Remove(seatIndex);
        // An earlier expiry this tick may already have moved the game past this seat's wait.
        if (!_game!.ActingPlayers().Contains(seatIndex))
        {
            return;
        }
        events.Add(new ActionTimerExpired { PlayerId = seatIndex });
        try
        {
            for (int step = 0; step < MaxActionsPerExpiry && _game.ActingPlayers().Contains(seatIndex); step++)
            {
                var action = AbsentPlayerPolicy.Choose(_game, seatIndex, _absentStandIn);
                events.AddRange(_game.Apply(action));
                PersistAction(action);
            }
        }
        catch (Exception e) when (e is InvalidActionException || e is InvalidOperationException)
        {
            // The stand-in found nothing it could do: the seat stays awaited, and its next clock tries again.
        }
    }

    private void BroadcastClocks()
    {
        List<Seat> seats;
        ActionClocksMessage message;
        lock (_sync)
        {
            seats = _seats.Where(seat => !seat.IsBot).ToList();
            message = new ActionClocksMessage { StateVersion = _stateVersion, Clocks = ClockInfos() };
        }
        foreach (var seat in seats)
        {
            _ = seat.SendAsync(message);
        }
    }
}
