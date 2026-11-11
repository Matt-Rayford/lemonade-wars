using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Unity
{
    /// <summary>Dice throws and the money that follows them: a roll's earnings are held
    /// back from the bars and paid out as bill piles once the roll has settled.</summary>
    public sealed partial class LemonadeWarsApp
    {
        /// <summary>Every player's earnings from the roll in progress; null between rolls.</summary>
        private Dictionary<int, int> _rollEarnings;
        private int _rollerId;
        /// <summary>Earned cash whose pile has not reached the bar yet; the bars show the old total meanwhile.</summary>
        private readonly Dictionary<int, int> _earningsInFlight = new Dictionary<int, int>();

        private void HandleSaleRolled(SaleRolled roll, System.Action onLanded)
        {
            // Played, not queued: it belongs to the throw, which passive effects wait behind.
            Sfx.Play(Sfx.SaleRoll);
            _dice.Enqueue(NameOf(roll.PlayerId), roll.Value, roll.PlayerId == _session.Seat,
                roll.Purpose,
                roll.StandInstanceId is int standId ? StandName(standId) : null,
                onLanded);
            // Two rolls can share one burst (a sale roll, then the next player's Spoiled Rotten
            // roll): the first one's earnings are staged now, or their held cash is never released.
            StageRollEarnings();
            _rollEarnings = new Dictionary<int, int>();
            _rollerId = roll.PlayerId;
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
            if (_rollEarnings == null || View == null || View.PendingRollValue != null)
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
            _rollEarnings = null;
            if (earnings == null || View == null)
            {
                return;
            }
            int playerCount = View.Players.Count;
            UnityEngine.Debug.Log("[LW] roll earnings staged: " + (earnings.Count == 0 ? "nobody earned" :
                string.Join(", ", earnings.Select(kv => $"{NameOf(kv.Key)} +${kv.Value}"))));
            for (int offset = 0; offset < playerCount; offset++)
            {
                int playerId = (_rollerId + offset) % playerCount;
                if (!earnings.TryGetValue(playerId, out int amount) || amount <= 0)
                {
                    continue;
                }
                _fx.QueueEarnings(playerId, amount, _art.Bill, () =>
                {
                    _earningsInFlight[playerId] = System.Math.Max(0,
                        (_earningsInFlight.TryGetValue(playerId, out int held) ? held : 0) - amount);
                    _renderedRevision = -1;
                });
            }
        }
    }
}
