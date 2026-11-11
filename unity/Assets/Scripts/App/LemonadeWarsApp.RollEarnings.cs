using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Unity
{
    /// <summary>Dice throws and the money that follows them: a roll's earnings are held
    /// back from the bars and paid out as bill piles once the roll has settled.</summary>
    public sealed partial class LemonadeWarsApp
    {
        /// <summary>The engine's MoneyChanged reason for handing back a cancelled purchase's price.</summary>
        private const string PurchaseRefundReason = "purchase refunded";

        /// <summary>Every player's earnings from the roll in progress; null between rolls.</summary>
        private Dictionary<int, int> _rollEarnings;
        /// <summary>Cards the roll in progress dealt (Meditation, Revelation), by player: shown with that player's payout.</summary>
        private readonly Dictionary<int, List<int>> _rollDraws = new Dictionary<int, List<int>>();
        /// <summary>A Spoiled Rotten roll has happened in the engine but its die has not been thrown on screen
        /// yet: its number, its payout and its response window all stay out of sight until it is.</summary>
        private bool _turnStartRollWaiting;
        private int _rollerId;
        /// <summary>Earned cash whose pile has not reached the bar yet; the bars show the old total meanwhile.</summary>
        private readonly Dictionary<int, int> _earningsInFlight = new Dictionary<int, int>();

        private void HandleSaleRolled(SaleRolled roll, System.Action onLanded)
        {
            // Two rolls can share one burst (a sale roll, then the next player's Spoiled Rotten
            // roll): the first one's earnings are staged now, or their held cash is never released.
            StageRollEarnings();
            _rollEarnings = new Dictionary<int, int>();
            _rollerId = roll.PlayerId;

            void ThrowDie()
            {
                _turnStartRollWaiting = false;
                // Played, not queued: it belongs to the throw, which passive effects wait behind.
                Sfx.Play(Sfx.SaleRoll);
                _dice.Enqueue(NameOf(roll.PlayerId), roll.Value, roll.PlayerId == _session.Seat,
                    roll.Purpose,
                    roll.StandInstanceId is int standId ? StandName(standId) : null,
                    onLanded);
            }

            if (roll.Purpose != RollPurpose.SpoiledRotten)
            {
                ThrowDie();
                return;
            }
            // Spoiled Rotten rolls at turn start, in the same burst as the sale roll before it. It is
            // thrown only once that roll has paid out, and on the viewer's own turn only after ONWARD!.
            _turnStartRollWaiting = true;
            if (AwaitingTurnBanner())
            {
                _afterTurnBanner.Add(ThrowDie);
            }
            else
            {
                _fx.QueueCallback(ThrowDie); // behind the payouts just staged, ahead of whatever follows
            }
        }

        private void HandleDieRerolled(DieRerolled reroll, System.Action onLanded)
        {
            Sfx.Play(Sfx.SaleRoll);
            _dice.EnqueueReroll(NameOf(reroll.ByPlayerId), reroll.NewValue,
                reroll.ByPlayerId == _session.Seat, onLanded);
        }

        private void HandleRollModified(RollModified modified, System.Action onLanded)
        {
            _dice.EnqueueModifier(NameOf(modified.PlayerId), BlackMarketName(modified.SourceDefId),
                modified.NewValue, modified.PlayerId == _session.Seat, onLanded);
        }

        private void HandleMoneyChanged(MoneyChanged money)
        {
            if (money.Amount < 0)
            {
                // The MarketRefilled that follows a purchase names neither buyer nor
                // price, but the payment always precedes it in the burst.
                _lastSpenderId = money.PlayerId;
                _lastMarketPrice = -money.Amount;
            }
            // A cancelled purchase was never shown being paid for, so its refund is not shown either.
            if (money.Reason == PurchaseRefundReason && ReleaseHeldSpending(money.PlayerId, money.Amount))
            {
                return;
            }
            if (money.Amount > 0)
            {
                CollectEarnings(money.PlayerId, money.Amount);
            }
            else if (!PaysWithBills(money.Reason))
            {
                _fx.QueueMoney(money.PlayerId, money.Amount);
            }
        }

        /// <summary>
        /// A purchase or a market refresh slaps its money on the table as a bill pile,
        /// so a "-$X" floater would only say it again. Fines and the Swear Jar have no pile.
        /// </summary>
        private static bool PaysWithBills(string reason) =>
            reason != "timeout fine" && reason != "swear jar";

        /// <summary>A roll's money is still being collected, or still sliding into bars.</summary>
        private bool EarningsStillShowing() =>
            _rollEarnings != null || _earningsInFlight.Values.Any(held => held > 0);

        /// <summary>
        /// Response windows can hold a roll open for several frames, so its earnings are
        /// staged only once the pending roll is gone.
        /// </summary>
        private void FlushRollEarnings()
        {
            if (_rollEarnings == null || View == null || View.PendingRollValue != null || _turnStartRollWaiting)
            {
                return;
            }
            if (_rollEarnings.TryGetValue(_session.Seat, out int mine) && mine > 0)
            {
                _fx.QueueSound(Sfx.Coins);
            }
            StageRollEarnings();
        }

        /// <summary>
        /// A gain during a roll joins that roll's pile; any other gain (a Doorbuster Sale, a sale,
        /// a refund) gets its own pile now. Either way the bar shows it only once the pile lands.
        /// </summary>
        private void CollectEarnings(int playerId, int amount)
        {
            if (_rollEarnings == null)
            {
                _earningsInFlight[playerId] = _earningsInFlight.TryGetValue(playerId, out int pending) ? pending + amount : amount;
                _renderedRevision = -1;
                _fx.QueueEarnings(playerId, amount, _art.Bill, () =>
                {
                    _earningsInFlight[playerId] = System.Math.Max(0,
                        (_earningsInFlight.TryGetValue(playerId, out int held) ? held : 0) - amount);
                    _renderedRevision = -1;
                });
                return;
            }
            _rollEarnings[playerId] = _rollEarnings.TryGetValue(playerId, out int sum) ? sum + amount : amount;
            _earningsInFlight[playerId] = _earningsInFlight.TryGetValue(playerId, out int held) ? held + amount : amount;
            _renderedRevision = -1; // the bar must show the old total from the first frame
        }

        /// <summary>
        /// The roll has settled: every earner gets their pile in turn order from the
        /// roller, one after another; each landing releases that player's held cash.
        /// </summary>
        private void StageRollEarnings()
        {
            var earnings = _rollEarnings;
            var draws = new Dictionary<int, List<int>>(_rollDraws);
            _rollEarnings = null;
            _rollDraws.Clear();
            if (earnings == null || View == null)
            {
                ShowHeldRollDraws(draws.Values.SelectMany(ids => ids));
                return;
            }
            int playerCount = View.Players.Count;
            UnityEngine.Debug.Log("[LW] roll earnings staged: " + (earnings.Count == 0 ? "nobody earned" :
                string.Join(", ", earnings.Select(kv => $"{NameOf(kv.Key)} +${kv.Value}"))));
            for (int offset = 0; offset < playerCount; offset++)
            {
                int playerId = (_rollerId + offset) % playerCount;
                earnings.TryGetValue(playerId, out int amount);
                draws.TryGetValue(playerId, out var drawnIds);
                // Of the viewer's own draws only those still held ride in: Whispers of Fate has
                // already made them discard one by now. A rival's count is all the table can know.
                int drawnCount = drawnIds == null ? 0
                    : playerId == _session.Seat ? drawnIds.Count(id => View.Hand.Any(card => card.InstanceId == id))
                    : drawnIds.Count;
                if (amount <= 0 && drawnCount == 0)
                {
                    continue;
                }
                if (drawnCount > 0 && playerId == _session.Seat)
                {
                    _fx.QueueSound(Sfx.CardDraw);
                }
                _fx.QueueEarnings(playerId, System.Math.Max(0, amount), _art.Bill, () =>
                {
                    _earningsInFlight[playerId] = System.Math.Max(0,
                        (_earningsInFlight.TryGetValue(playerId, out int held) ? held : 0) - amount);
                    ShowHeldRollDraws(drawnIds);
                    _renderedRevision = -1;
                }, _art.Back("lemon"), drawnCount);
            }
        }

        /// <summary>The viewer's cards a roll dealt were kept out of the fan until their payout landed.</summary>
        private void ShowHeldRollDraws(IEnumerable<int> drawnIds)
        {
            if (drawnIds == null)
            {
                return;
            }
            foreach (int drawnId in drawnIds)
            {
                _handCardsInFlight.Remove(drawnId);
            }
            _renderedRevision = -1;
        }
    }
}
