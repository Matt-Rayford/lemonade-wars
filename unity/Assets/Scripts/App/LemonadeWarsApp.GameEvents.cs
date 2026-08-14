using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Unity
{
    /// <summary>Typed engine events: presentation moments (dice, effects, purchase
    /// theatre) and the running action log.</summary>
    public sealed partial class LemonadeWarsApp
    {
        // ----- purchase theatre bookkeeping (see HandleMarketRefilled) -----
        // The Black Market row as the viewer last SAW it (seeded per render, advanced
        // by MarketRefilled events mid-batch). MarketRefilled carries only the new id
        // list, so this is how a refill diff recovers which card left which slot.
        private List<PlayerView.CardInfo> _marketSnap;
        // Stand-supply top shapes as last rendered: the copy a StandPurchased took.
        private Dictionary<string, Shape> _supplyTopSnap;
        // Refilled market cards the viewer has NOT been shown yet: each renders as an
        // empty slot until the purchase drift that exposed it delivers. Every drift
        // delivery removes its id exactly once (including skip/Clear — the
        // EffectsPlayer guarantees delivery on every exit path).
        private readonly HashSet<int> _hiddenMarketIds = new HashSet<int>();
        // Who last took something for money (MoneyChanged < 0) or off the market
        // (BlackMarketPurchased): MarketRefilled has no buyer field, but one of those
        // always precedes it in the same event burst — Pay() for purchases, TryEquip's
        // event for free takes like Connections. Caption-only; never load-bearing.
        private int _lastSpenderId = -1;

        // The viewer's earnings collected across one roll, for the "you earned" recap.
        private List<string> _saleEarnings;
        private int _saleTotal;

        private readonly List<string> _actionLog = new List<string>();

        /// <summary>Typed engine events drive presentation moments (dice, later: effects).</summary>
        private void OnGameEvent(GameEvent gameEvent)
        {
            if (_session == null || _session.HumanAutoplay)
            {
                return; // autopilot is for fast testing — no theatre
            }
            string logLine = LogLine(gameEvent);
            if (logLine != null)
            {
                _actionLog.Add(logLine);
                if (_actionLog.Count > 40)
                {
                    _actionLog.RemoveAt(0);
                }
            }
            if (gameEvent is SaleRolled roll)
            {
                // Played, not queued: it belongs to the tumble, which passive effects
                // deliberately wait behind.
                Sfx.Play(Sfx.SaleRoll);
                _dice.Enqueue(NameOf(roll.PlayerId), roll.Value, roll.PlayerId == _session.Seat,
                    roll.Purpose,
                    roll.StandInstanceId is int standId ? StandName(standId) : null);
                // Start collecting this roll's earnings for the "you earned" recap.
                _saleEarnings = new List<string>();
                _saleTotal = 0;
            }
            else if (gameEvent is StandSold sold)
            {
                _fx.QueueMoney(sold.PlayerId, sold.Earnings);
                if (sold.PlayerId == _session.Seat && _saleEarnings != null)
                {
                    _saleEarnings.Add($"{StandName(sold.StandInstanceId)} +${sold.Earnings}");
                    _saleTotal += sold.Earnings;
                }
            }
            else if (gameEvent is DieRerolled reroll)
            {
                Sfx.Play(Sfx.SaleRoll); // a second real tumble deserves the same sound
                _dice.EnqueueReroll(NameOf(reroll.ByPlayerId), reroll.NewValue,
                    reroll.ByPlayerId == _session.Seat);
            }
            else if (gameEvent is RollModified modified)
            {
                _dice.EnqueueModifier(NameOf(modified.PlayerId), BlackMarketName(modified.SourceDefId),
                    modified.NewValue, modified.PlayerId == _session.Seat);
            }
            else if (gameEvent is MoneyChanged money)
            {
                if (money.Amount < 0)
                {
                    // Remember who paid: the MarketRefilled that follows a purchase
                    // carries no buyer, but Pay() always precedes it in the burst.
                    _lastSpenderId = money.PlayerId;
                }
                _fx.QueueMoney(money.PlayerId, money.Amount);
                if (money.PlayerId == _session.Seat && money.Amount > 0 && _saleEarnings != null)
                {
                    _saleEarnings.Add($"{money.Reason} +${money.Amount}");
                    _saleTotal += money.Amount;
                }
            }
            else if (gameEvent is CardDrawn drawn && drawn.PlayerId == _session.Seat)
            {
                // Queued, not played: the turn-start draw waits behind ONWARD!.
                _fx.QueueSound(Sfx.CardDraw);
            }
            else if (gameEvent is TimeoutDrawn timeout && timeout.PlayerId == _session.Seat)
            {
                _fx.QueueSound(Sfx.CardDraw);
            }
            else if (gameEvent is TitleClaimed title && title.PlayerId == _session.Seat)
            {
                Sfx.Play(Sfx.TitleClaim);
            }
            else if (gameEvent is BraggingRightsPurchased brag)
            {
                if (brag.PlayerId == _session.Seat)
                {
                    Sfx.Play(Sfx.TitleClaim); // a title claim first, a purchase second
                }
                else
                {
                    _fx.QueueSound(Sfx.CashRegister);
                }
                // Which shelf card was bought: $16 base, +$2 per sale (mirrors the
                // shelf's own index-from-price math in TableView.RenderSupply).
                var bragArt = _art.BraggingRights((brag.Price - 16) / 2)
                    ?? _art.Back("braggingRights");
                _fx.QueuePurchase(bragArt, PurchaseCaption(brag.PlayerId),
                    () => _table.BraggingShelfWorld());
            }
            else if (gameEvent is WhiniestBabyMoved baby && baby.ToPlayerId == _session.Seat)
            {
                // Queued: the card often lands at turn start, so the wail waits for
                // ONWARD! and plays when you can actually see why. The claim reveal
                // holds the card centered (readable), then flies it into the lord fan
                // — the fan keeps the card HIDDEN until the flight delivers it (the
                // table renders the new state immediately, well before the theatre).
                _table.SuppressWhiniestBaby = true;
                _fx.QueueSound(Sfx.WhinyBaby);
                _fx.QueueClaim(_art.WhiniestBaby(), "You are the Whiniest Baby!",
                    () => _table.WhiniestBabyLandingWorld(),
                    () =>
                    {
                        _table.SuppressWhiniestBaby = false;
                        _renderedRevision = -1; // surface the landed card now
                    });
            }
            else if (gameEvent is SpoiledRottenMoved spoiled && spoiled.ToPlayerId == _session.Seat)
            {
                // Same claim theatre as the baby — reveal, then fly into the lord fan.
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
            else if (gameEvent is BlackMarketPurchased bought)
            {
                // No theatre here: this fires when the EQUIP completes — post-tantrum-
                // window for in-game buys, never if cancelled. The card visibly leaves
                // the shelf at MarketRefilled below, so the register and the drift are
                // keyed there. But for free takes (Connections) this precedes the
                // refill and is the only event naming the taker — remember them.
                _lastSpenderId = bought.PlayerId;
            }
            else if (gameEvent is MarketRefilled refilled)
            {
                HandleMarketRefilled(refilled);
            }
            else if (gameEvent is StandPurchased standBought)
            {
                // Queued, so the register rings alongside the "-$X" floater. The pile's
                // next card may surface instantly (that's the pile revealing itself);
                // the drift shows WHOSE stand left the shelf.
                _fx.QueueSound(Sfx.CashRegister);
                var standArt = _supplyTopSnap != null &&
                    _supplyTopSnap.TryGetValue(standBought.StandTypeId, out var topShape)
                        ? _art.Stand(standBought.StandTypeId, topShape)
                        : _art.Stand(standBought.StandTypeId);
                string typeId = standBought.StandTypeId;
                _fx.QueuePurchase(standArt, PurchaseCaption(standBought.PlayerId),
                    () => _table.StandSupplyWorld(typeId));
            }
            else if (gameEvent is MoneyStolen theft)
            {
                _fx.QueueMoneySteal(theft.FromPlayerId, theft.ToPlayerId, theft.Amount);
            }
            else if (gameEvent is CardsStolen cardTheft)
            {
                _fx.QueueCardFly(_art.Back("lemon"),
                    cardTheft.FromPlayerId, cardTheft.ToPlayerId, cardTheft.Count);
            }
            else if (gameEvent is HandsTraded trade)
            {
                _fx.QueueCardFly(_art.Back("lemon"), trade.PlayerA, trade.PlayerB, 1);
                _fx.QueueCardFly(_art.Back("lemon"), trade.PlayerB, trade.PlayerA, 1);
            }
            else if (gameEvent is LemonCardPlayed played &&
                     played.TargetPlayerId is int victim)
            {
                // The clash is audible for both duelists, silent for bystanders —
                // queued, so it waits out dice theatre instead of stinging mid-roll.
                if (played.PlayerId == _session.Seat || victim == _session.Seat)
                {
                    _fx.QueueSound(Sfx.AttackCard);
                }
                if (played.PlayerId != _session.Seat)
                {
                    // Someone ELSE aimed an attack: one card-reveal beat naming both
                    // sides. If a response modal is about to open for us, the window
                    // event below cancels this — the modal tells the same story.
                    string victimName = victim == _session.Seat
                        ? "YOU"
                        : NameOf(victim).ToUpperInvariant();
                    _fx.QueueReveal(_art.Lemon(played.DefId),
                        $"{NameOf(played.PlayerId).ToUpperInvariant()} ATTACKS {victimName} WITH " +
                        $"{LemonName(played.DefId).ToUpperInvariant()}!");
                }
            }
            else if (gameEvent is ResponseWindowOpened window &&
                     window.AwaitingPlayers.Contains(_session.Seat))
            {
                _fx.CancelPendingReveals();
            }
            else if (gameEvent is AttackRedirected redirect)
            {
                _fx.QueueToast($"{NameOf(redirect.ByPlayerId).ToUpperInvariant()} TAGS IT TO " +
                    $"{NameOf(redirect.NewTargetId).ToUpperInvariant()}!");
            }
            else if (gameEvent is AttackReflected reflect)
            {
                _fx.QueueToast($"{NameOf(reflect.ByPlayerId).ToUpperInvariant()} " +
                    "REFLECTS IT BACK!");
            }
            else if (gameEvent is AttackFizzled fizzle)
            {
                _fx.QueueToast($"{LemonName(fizzle.DefId).ToUpperInvariant()} FIZZLES!");
            }
            else if (gameEvent is PlayCancelled cancelled)
            {
                // The Inflatable Decoy is an equipped reaction with no play event of
                // its own — without a reveal it cancels invisibly and reads as a bug.
                if (cancelled.WithDefId == "inflatable-decoy" && cancelled.ByPlayerId is int blocker)
                {
                    _fx.QueueReveal(_art.BlackMarket("inflatable-decoy", Shape.Square),
                        $"{NameOf(blocker)} blocks with Inflatable Decoy!");
                }
                string byWhat = cancelled.ByPlayerId is int by && cancelled.WithDefId != null
                    ? $" BY {NameOf(by).ToUpperInvariant()}'S {CardName(cancelled.WithDefId).ToUpperInvariant()}"
                    : "";
                _fx.QueueToast($"{NameOf(cancelled.OwnerId).ToUpperInvariant()}'S " +
                    $"{CardName(cancelled.DefId).ToUpperInvariant()} IS CANCELLED{byWhat}!");
            }
        }

        // ------------------------------------------------- purchase theatre

        /// <summary>
        /// Purchase-theatre baseline: the shelf as the viewer now sees it. Later
        /// MarketRefilled/StandPurchased events diff against these to know which
        /// card left which slot (the events alone don't say). Engine truth, NOT
        /// presentation state — a slot held visually empty is still snapshotted.
        /// </summary>
        private void SnapshotMarketBaseline()
        {
            _marketSnap = View.Market
                .Select(c => new PlayerView.CardInfo
                {
                    InstanceId = c.InstanceId,
                    DefId = c.DefId,
                    Shape = c.Shape,
                })
                .ToList();
            _supplyTopSnap = new Dictionary<string, Shape>(View.SupplyTopShapes);
        }

        /// <summary>
        /// The moment a card visibly leaves the Black Market shelf. Diffing the refill's
        /// id list against the last-seen row recovers which card left which slot (the
        /// engine removes at the slot and APPENDS the refill, so the row shifts left and
        /// the new card is the last slot). For each departed card: drift its art out of
        /// its old slot, and hold the newly-appeared slot empty until the drift lands.
        /// A paid full-row refresh (RefreshedByPlayerId set) announces itself already.
        /// </summary>
        private void HandleMarketRefilled(MarketRefilled refilled)
        {
            var oldSnap = _marketSnap;
            var newIds = refilled.Market;
            // Advance the snapshot first (carrying known art forward), so a second
            // purchase in the same batched burst diffs against THIS refill's row.
            _marketSnap = newIds
                .Select(id => oldSnap?.FirstOrDefault(c => c.InstanceId == id)
                    ?? new PlayerView.CardInfo { InstanceId = id, DefId = null })
                .ToList();
            if (refilled.RefreshedByPlayerId != null || oldSnap == null)
            {
                return; // paid sweep, or no baseline yet (fresh join): no per-card fx
            }

            // Cards now on the shelf that the viewer has not been shown yet.
            var freshIds = newIds.Where(id => !oldSnap.Any(c => c.InstanceId == id)).ToList();

            int departedSoFar = 0;
            for (int slot = 0; slot < oldSnap.Count; slot++)
            {
                var card = oldSnap[slot];
                if (newIds.Contains(card.InstanceId))
                {
                    continue; // still on the shelf
                }
                // Pair the k-th departed card with the k-th fresh card (1:1 in practice).
                int freshId = departedSoFar < freshIds.Count ? freshIds[departedSoFar] : -1;
                departedSoFar++;
                var art = card.DefId == null
                    ? null // refilled and re-bought inside one burst: art never seen
                    : _art.BlackMarket(card.DefId, card.Shape ?? Shape.Square);
                if (art == null)
                {
                    continue; // nothing to fly — leave the refill instant (status quo)
                }
                if (freshId >= 0)
                {
                    _hiddenMarketIds.Add(freshId);
                }
                int originSlot = slot;   // geometric spot the departed card occupied
                int revealId = freshId;  // capture per-flight, not per-loop
                _fx.QueueSound(Sfx.CashRegister);
                _fx.QueuePurchase(art, PurchaseCaption(_lastSpenderId),
                    () => _table.MarketSlotWorld(originSlot),
                    () => ReleaseMarketCard(revealId));
            }
        }

        /// <summary>Drift delivery: let the hidden refill card show itself. Fires
        /// exactly once per flight on every path (landing, skip, effects Clear).</summary>
        private void ReleaseMarketCard(int instanceId)
        {
            _hiddenMarketIds.Remove(instanceId); // -1 (no paired refill) is a no-op
            _renderedRevision = -1;              // surface the refilled card now
        }

        /// <summary>Buyer's name under the drifting card; null for your own buys (you
        /// just dragged it — the name would be noise).</summary>
        private string PurchaseCaption(int playerId) =>
            playerId >= 0 && playerId != _session.Seat
                ? NameOf(playerId).ToUpperInvariant()
                : null;

        /// <summary>
        /// Roll fully resolved: recap what the viewer earned. The entries collect
        /// across the Applies of a roll (windows can hold it open several frames),
        /// so flush only once the pending roll is gone.
        /// </summary>
        private void FlushSaleRecap()
        {
            if (_saleEarnings == null || View == null || View.PendingRollValue != null)
            {
                return;
            }
            if (_saleTotal > 0)
            {
                // Coins first so they ring under the recap, not after it.
                _fx.QueueSound(Sfx.Coins);
                _fx.QueueToast($"YOU EARN ${_saleTotal}:  " +
                    string.Join("  ·  ", _saleEarnings).ToUpperInvariant());
            }
            _saleEarnings = null;
        }

        // --------------------------------------------------------- action log

        /// <summary>
        /// Friendly one-liner for the action log; null for events too noisy to log
        /// (individual money ticks, window bookkeeping) or already folded into the
        /// line above (a run of draws grows its own line in place).
        /// </summary>
        private string LogLine(GameEvent e)
        {
            switch (e)
            {
                case TurnStarted turn: return $"— {NameOf(turn.PlayerId)}'s turn —";
                case CardDrawn drawn: return DrawLine(drawn);
                case LemonCardPlayed played:
                    return $"{NameOf(played.PlayerId)} plays {LemonName(played.DefId)}" +
                           (played.TargetPlayerId is int t ? $" on {NameOf(t)}" : "");
                case PlayCancelled cancelled:
                    return $"{NameOf(cancelled.OwnerId)}'s {CardName(cancelled.DefId)} is cancelled" +
                        (cancelled.ByPlayerId is int by && cancelled.WithDefId != null
                            ? $" by {NameOf(by)}'s {CardName(cancelled.WithDefId)}"
                            : "");
                case AttackRedirected redirect:
                    return $"{NameOf(redirect.ByPlayerId)} tags the attack to {NameOf(redirect.NewTargetId)}";
                case AttackReflected reflect:
                    return $"{NameOf(reflect.ByPlayerId)} reflects the attack onto {NameOf(reflect.NewTargetId)}";
                case AttackFizzled fizzle:
                    return $"{NameOf(fizzle.OwnerId)}'s {LemonName(fizzle.DefId)} fizzles";
                case StandPurchased standBuy:
                    return $"{NameOf(standBuy.PlayerId)} buys a {_db.StandType(standBuy.StandTypeId).Name}";
                case BlackMarketPurchased bmBuy:
                    return $"{NameOf(bmBuy.PlayerId)} buys {BlackMarketName(bmBuy.DefId)}";
                case BraggingRightsPurchased brag:
                    return $"{NameOf(brag.PlayerId)} buys Bragging Rights (${brag.Price})";
                // Only the paid sweep is worth a line — the refill after a purchase is not.
                case MarketRefilled refill when refill.RefreshedByPlayerId is int refresher:
                    return $"{NameOf(refresher)} refreshes the Black Market";
                case PowerPourTriggered pour:
                    return $"{NameOf(pour.PlayerId)} hits a Power Pour!";
                case AbilityTriggered ability:
                    return $"{NameOf(ability.PlayerId)}'s {BlackMarketName(ability.DefId)} kicks in";
                case TitleClaimed title:
                    return $"{NameOf(title.PlayerId)} claims {_db.Title(title.TitleId).Name}!";
                case SaleRolled rolled:
                    return $"{NameOf(rolled.PlayerId)} rolls a {rolled.Value}" +
                           (RollScopeName(rolled.Purpose, rolled.StandInstanceId) is string why
                               ? $" ({why})" : "");
                case DieRerolled reroll:
                    return $"{NameOf(reroll.ByPlayerId)} plays Out of Stock — reroll: {reroll.NewValue}";
                case RollModified modified:
                    return $"{NameOf(modified.PlayerId)}'s {BlackMarketName(modified.SourceDefId)} " +
                           $"changes the roll to {modified.NewValue}";
                case MoneyStolen theft:
                    return $"{NameOf(theft.ToPlayerId)} steals ${theft.Amount} from {NameOf(theft.FromPlayerId)}";
                case CardsStolen cards:
                    return $"{NameOf(cards.ToPlayerId)} steals {cards.Count} card(s) from {NameOf(cards.FromPlayerId)}";
                case HandsTraded traded:
                    return $"{NameOf(traded.PlayerA)} and {NameOf(traded.PlayerB)} trade hands";
                case TimeoutDrawn timeout:
                    return $"{NameOf(timeout.PlayerId)} draws a Timeout!";
                case WhiniestBabyMoved baby when baby.ToPlayerId is int b:
                    return $"{NameOf(b)} is now the Whiniest Baby";
                case SpoiledRottenMoved rotten when rotten.ToPlayerId is int r:
                    return $"{NameOf(r)} is now Spoiled Rotten";
                case TrapPlaced trap:
                    return $"{NameOf(trap.OwnerId)} sets a trap on {NameOf(trap.OnPlayerId)}'s turf";
                case StageChanged stage when stage.Stage == GameStage.FinalRound:
                    return "— Final round! —";
                case GameEnded ended:
                    return $"Game over — {string.Join(", ", ended.Winners.Select(NameOf))} wins!";
                default:
                    return null;
            }
        }

        // A run of draws — turn-start, a Draw action, or a card handing over 3 at once —
        // grows ONE line instead of spamming one per card. The run belongs to the last
        // logged line: anything else logged in between starts a fresh one.
        private int _drawRunPlayer = -1;
        private DrawReason _drawRunReason;
        private int _drawRunCount;
        private string _drawRunText;

        /// <summary>
        /// Log a draw without leaking what was drawn. Returns the new line, or null when
        /// the draw was folded into the line already on the end of the log.
        /// </summary>
        private string DrawLine(CardDrawn drawn)
        {
            bool continues = _drawRunText != null &&
                             _drawRunPlayer == drawn.PlayerId &&
                             _drawRunReason == drawn.Reason &&
                             _actionLog.Count > 0 &&
                             _actionLog[_actionLog.Count - 1] == _drawRunText;

            _drawRunPlayer = drawn.PlayerId;
            _drawRunReason = drawn.Reason;
            _drawRunCount = continues ? _drawRunCount + 1 : 1;
            _drawRunText = DrawRunText(drawn.PlayerId, drawn.Reason, _drawRunCount);

            if (!continues)
            {
                return _drawRunText;
            }
            _actionLog[_actionLog.Count - 1] = _drawRunText;
            return null;
        }

        private string DrawRunText(int playerId, DrawReason reason, int count)
        {
            string cards = count == 1 ? "a card" : $"{count} cards";
            switch (reason)
            {
                case DrawReason.Action:
                    // The one draw kind that costs an action — say so in full.
                    return count == 1
                        ? $"{NameOf(playerId)} draws a Lemon card"
                        : $"{NameOf(playerId)} draws {count} Lemon cards";
                case DrawReason.TurnStart:
                    return $"{NameOf(playerId)} draws {cards} to start the turn";
                default:
                    return $"{NameOf(playerId)} draws {cards}";
            }
        }
    }
}
