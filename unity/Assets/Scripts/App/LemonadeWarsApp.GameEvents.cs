using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Unity
{
    /// <summary>Routes each typed engine event to its presentation moment.</summary>
    public sealed partial class LemonadeWarsApp
    {
        /// <summary>Presentation that belongs to the viewer's turn start and so waits
        /// until the Your Turn banner has been shown and dismissed.</summary>
        private readonly List<System.Action> _afterTurnBanner = new List<System.Action>();

        private void OnGameEvent(GameEvent gameEvent)
        {
            if (_session == null || _session.HumanAutoplay)
            {
                return; // autopilot is for fast testing: no theatre
            }
            string logLine = LogLine(gameEvent);
            // A roll's number is a secret until the die settles: its line waits.
            bool logWaitsForDie = gameEvent is SaleRolled || gameEvent is DieRerolled || gameEvent is RollModified;
            if (logLine != null && !logWaitsForDie)
            {
                AddLogLine(logLine);
            }
            System.Action onLanded = logLine == null ? null : () => AddLogLine(logLine);

            switch (gameEvent)
            {
                case SaleRolled roll: HandleSaleRolled(roll, onLanded); break;
                case StandSold sold: CollectEarnings(sold.PlayerId, sold.Earnings); break;
                case DieRerolled reroll: HandleDieRerolled(reroll, onLanded); break;
                case RollModified modified: HandleRollModified(modified, onLanded); break;
                case MoneyChanged money: HandleMoneyChanged(money); break;
                case CardDrawn drawn when drawn.PlayerId == _session.Seat: HandleOwnCardDrawn(drawn); break;
                case CardDrawn rivalDraw: HandleRivalCardDrawn(rivalDraw); break;
                case TimeoutDrawn _: HandleTimeoutDrawn(); break;
                case TitleClaimed title when title.PlayerId == _session.Seat: Sfx.Play(Sfx.TitleClaim); break;
                case BraggingRightsPurchased brag: HandleBraggingRightsPurchased(brag); break;
                case WhiniestBabyMoved baby when baby.ToPlayerId == _session.Seat: HandleWhiniestBabyClaimed(); break;
                case WhiniestBabyMoved baby when baby.ToPlayerId is int newBabyId:
                    HandleWhiniestBabyPassedToRival(baby.FromPlayerId, newBabyId);
                    break;
                case SpoiledRottenMoved spoiled when spoiled.ToPlayerId == _session.Seat: HandleSpoiledRottenClaimed(); break;
                case BlackMarketPurchased bought: HandleBlackMarketPurchased(bought); break;
                case BlackMarketPurchaseStarted purchaseStarted: HandlePurchaseStarted(purchaseStarted); break;
                case MarketRefilled refilled: HandleMarketRefilled(refilled); break;
                // The previous turn's sale roll has settled: its payout is staged ahead of
                // anything this new turn queues (a Timeout's flight, a draw).
                case TurnStarted _: StageRollEarnings(); break;
                case StandPurchased standBought: HandleStandPurchased(standBought); break;
                case MoneyStolen theft: _fx.QueueMoneySteal(theft.FromPlayerId, theft.ToPlayerId, theft.Amount); break;
                case CardsStolen cardTheft: HandleCardsStolen(cardTheft); break;
                case HandsTraded trade: HandleHandsTraded(trade); break;
                case LemonCardPlayed played: HandleLemonCardPlayed(played); break;
                case ResponseWindowOpened window when window.AwaitingPlayers.Contains(_session.Seat):
                    _fx.CancelPendingReveals();
                    break;
                case AttackRedirected redirect: HandleAttackRedirected(redirect); break;
                case AttackReflected reflect: HandleAttackReflected(reflect); break;
                case AttackFizzled fizzle: HandleAttackFizzled(fizzle); break;
                case PlayResolved resolved: HandlePlayResolved(resolved); break;
                case PlayCancelled cancelled: HandlePlayCancelled(cancelled); break;
            }
        }

        // ---------------------------------------------- waiting on the turn banner

        /// <summary>The viewer's turn has been dealt by the engine but not yet handed over on screen.</summary>
        private bool AwaitingTurnBanner() => IsMyTurn() && (!_wasMyTurn || _turnBanner.IsOpen);

        private void RunAfterTurnBanner(System.Action presentation)
        {
            if (AwaitingTurnBanner())
            {
                _afterTurnBanner.Add(presentation);
            }
            else
            {
                presentation();
            }
        }

        /// <summary>Called every frame, before the render: releases what waited on ONWARD!.</summary>
        private void ReleaseAfterTurnBanner()
        {
            if (_afterTurnBanner.Count == 0 || View == null || AwaitingTurnBanner())
            {
                return;
            }
            var ready = _afterTurnBanner.ToList();
            _afterTurnBanner.Clear();
            foreach (var presentation in ready)
            {
                presentation();
            }
        }

        // ------------------------------------------------------ claims and thefts

        private void HandleWhiniestBabyClaimed()
        {
            // The table renders the new state at once, so the lord fan keeps the card
            // hidden until the claim flight delivers it.
            _table.SuppressWhiniestBaby = true;
            // Queued: the card often lands at turn start, so the wail waits for ONWARD!.
            _fx.QueueSound(Sfx.WhinyBaby);
            _fx.QueueClaim(_art.WhiniestBaby(), "You are the Whiniest Baby!",
                () => _table.WhiniestBabyLandingWorld(),
                () =>
                {
                    _table.SuppressWhiniestBaby = false;
                    _renderedRevision = -1;
                });
        }

        /// <summary>
        /// The Baby goes to a rival: its card travels to their bar the way a purchase does (beside
        /// the bar, a beat, then into the chip), so it is seen changing hands, not just gone.
        /// </summary>
        private void HandleWhiniestBabyPassedToRival(int? previousHolderId, int newHolderId)
        {
            System.Func<UnityEngine.Vector3?> origin;
            if (previousHolderId == _session.Seat)
            {
                origin = () => _table.WhiniestBabyLandingWorld(); // where it sat in the viewer's own fan
            }
            else if (previousHolderId is int previousRival)
            {
                origin = () => _table.PlayerBarWorld(previousRival);
            }
            else
            {
                origin = () => _table.PurchaseShowcaseWorld(newHolderId); // fresh from the market: it appears beside the bar
            }
            _fx.QueueSound(Sfx.WhinyBaby);
            _fx.QueuePurchase(_art.WhiniestBaby(), newHolderId, price: 0, _art.Bill, origin);
        }

        private void HandleSpoiledRottenClaimed()
        {
            _table.SuppressSpoiledRotten = true;
            _fx.QueueSound(Sfx.TitleClaim);
            _fx.QueueClaim(_art.SpoiledRotten(), "You are Spoiled Rotten!",
                () => _table.SpoiledRottenLandingWorld(),
                () =>
                {
                    _table.SuppressSpoiledRotten = false;
                    _renderedRevision = -1;
                });
        }

        private void HandleCardsStolen(CardsStolen cardTheft)
        {
            _fx.QueueCardFly(_art.Back(cardTheft.IsBlackMarketCard ? "blackMarket" : "lemon"),
                cardTheft.FromPlayerId, cardTheft.ToPlayerId, cardTheft.Count);
        }

        private void HandleHandsTraded(HandsTraded trade)
        {
            _fx.QueueCardFly(_art.Back("lemon"), trade.PlayerA, trade.PlayerB, 1);
            _fx.QueueCardFly(_art.Back("lemon"), trade.PlayerB, trade.PlayerA, 1);
        }
    }
}
