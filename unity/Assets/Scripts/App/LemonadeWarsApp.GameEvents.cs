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
        // For a hidden refill id: the art of the card its slot used to hold, drawn
        // inert there until the effect that sweeps or lifts it is on screen.
        private readonly Dictionary<int, UnityEngine.Texture2D> _marketStandIns =
            new Dictionary<int, UnityEngine.Texture2D>();
        // Who last took something for money (MoneyChanged < 0) or off the market
        // (BlackMarketPurchased): MarketRefilled has no buyer field, but one of those
        // always precedes it in the same event burst — Pay() for purchases, TryEquip's
        // event for free takes like Connections. Caption-only; never load-bearing.
        private int _lastSpenderId = -1;
        /// <summary>
        /// What the Black Market take in progress cost — the last payment, or 0 once a
        /// free take's equip event says so — for the bills its drift slaps down.
        /// </summary>
        private int _lastMarketPrice;

        // Every player's earnings from the roll in progress, staged as bill piles once
        // the roll settles; null between rolls.
        private Dictionary<int, int> _rollEarnings;
        private int _rollerId;
        /// <summary>Earned cash whose pile has not reached the bar yet; the bars show the old total meanwhile.</summary>
        private readonly Dictionary<int, int> _earningsInFlight = new Dictionary<int, int>();

        private readonly List<string> _actionLog = new List<string>();

        /// <summary>Typed engine events drive presentation moments (dice, later: effects).</summary>
        private void OnGameEvent(GameEvent gameEvent)
        {
            if (_session == null || _session.HumanAutoplay)
            {
                return; // autopilot is for fast testing — no theatre
            }
            string logLine = LogLine(gameEvent);
            // A roll's number is a secret until the die settles: its line waits.
            bool logWaitsForDie = gameEvent is SaleRolled || gameEvent is DieRerolled || gameEvent is RollModified;
            if (logLine != null && !logWaitsForDie)
            {
                AddLogLine(logLine);
            }
            System.Action onLanded = logLine == null ? null : () => AddLogLine(logLine);
            if (gameEvent is SaleRolled roll)
            {
                // Played, not queued: it belongs to the throw, which passive effects
                // deliberately wait behind.
                Sfx.Play(Sfx.SaleRoll);
                _dice.Enqueue(NameOf(roll.PlayerId), roll.Value, roll.PlayerId == _session.Seat,
                    roll.Purpose,
                    roll.StandInstanceId is int standId ? StandName(standId) : null,
                    onLanded);
                // Start collecting this roll's earnings; they are staged as bill piles
                // once the roll settles.
                _rollEarnings = new Dictionary<int, int>();
                _rollerId = roll.PlayerId;
            }
            else if (gameEvent is StandSold sold)
            {
                CollectEarnings(sold.PlayerId, sold.Earnings);
            }
            else if (gameEvent is DieRerolled reroll)
            {
                Sfx.Play(Sfx.SaleRoll); // a second real throw deserves the same sound
                _dice.EnqueueReroll(NameOf(reroll.ByPlayerId), reroll.NewValue,
                    reroll.ByPlayerId == _session.Seat, onLanded);
            }
            else if (gameEvent is RollModified modified)
            {
                _dice.EnqueueModifier(NameOf(modified.PlayerId), BlackMarketName(modified.SourceDefId),
                    modified.NewValue, modified.PlayerId == _session.Seat, onLanded);
            }
            else if (gameEvent is MoneyChanged money)
            {
                if (money.Amount < 0)
                {
                    // Remember who paid, and what: the MarketRefilled that follows a
                    // purchase carries neither, but Pay() always precedes it in the
                    // burst (the equip event, which also knows the price, comes after
                    // the tantrum window — too late for the drift).
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
                _fx.QueuePurchase(bragArt, brag.PlayerId, brag.Price, _art.Bill,
                    () => _table.BraggingShelfWorld(),
                    showCard: ShowsPurchaseTheatre(brag.PlayerId));
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
                _lastMarketPrice = bought.Price;
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
                _fx.QueuePurchase(standArt, standBought.PlayerId, standBought.Price, _art.Bill,
                    () => _table.StandSupplyWorld(typeId),
                    showCard: ShowsPurchaseTheatre(standBought.PlayerId));
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
            else if (gameEvent is LemonCardPlayed played)
            {
                // The clash is audible for both duelists, silent for bystanders —
                // queued, so it waits out dice theatre instead of stinging mid-roll.
                if (played.TargetPlayerId is int struck &&
                    (played.PlayerId == _session.Seat || struck == _session.Seat))
                {
                    _fx.QueueSound(Sfx.AttackCard);
                }
                // Every Lemon play lands on the table. A reaction thrown at a staged
                // play slaps down on top of it; anything else — a plan, an attack, or a
                // reaction with nothing under it (Out of Stock on a roll, Profit Share
                // after a theft) — is staged beside its player's bar, badged with who
                // played it, and held there through any reactions, so a Tantrum is
                // always thrown at something everyone can see. Attacks get the arrow
                // to their victim; steals and discard takes show their loot.
                bool onThePile = !IsStagedPlay(played.DefId) &&
                    _playTheatre.AddResponse(_art.Lemon(played.DefId), played.DefId, played.PlayerId);
                if (!onThePile)
                {
                    _stagedAttacker = played.PlayerId;
                    var loot = played.DefId == "finders-keepers" && played.StolenDefId != null
                        ? _art.BlackMarket(played.StolenDefId, played.StolenShape ?? Shape.Square)
                        : TakenArt(played);
                    _playTheatre.Begin(_art.Lemon(played.DefId), played.DefId, played.PlayerId,
                        played.TargetPlayerId ?? -1, loot);
                }
            }
            else if (gameEvent is ResponseWindowOpened window &&
                     window.AwaitingPlayers.Contains(_session.Seat))
            {
                _fx.CancelPendingReveals();
            }
            else if (gameEvent is AttackRedirected redirect)
            {
                _playTheatre.Retarget(_stagedAttacker, redirect.NewTargetId);
                _fx.QueueToast($"{NameOf(redirect.ByPlayerId).ToUpperInvariant()} TAGS IT TO " +
                    $"{NameOf(redirect.NewTargetId).ToUpperInvariant()}!");
            }
            else if (gameEvent is AttackReflected reflect)
            {
                // "As if you played it": the reflector owns the attack from here on.
                _stagedAttacker = reflect.ByPlayerId;
                _playTheatre.Retarget(reflect.ByPlayerId, reflect.NewTargetId);
                _fx.QueueToast($"{NameOf(reflect.ByPlayerId).ToUpperInvariant()} " +
                    "REFLECTS IT BACK!");
            }
            else if (gameEvent is AttackFizzled fizzle)
            {
                if (_playTheatre.Matches(fizzle.OwnerId, fizzle.DefId))
                {
                    _playTheatre.Cancel();
                }
                _fx.QueueToast($"{LemonName(fizzle.DefId).ToUpperInvariant()} FIZZLES!");
            }
            else if (gameEvent is PlayResolved resolved && _playTheatre.Matches(resolved.OwnerId, resolved.DefId))
            {
                _playTheatre.Resolve();
            }
            else if (gameEvent is PlayCancelled cancelled)
            {
                if (_playTheatre.Matches(cancelled.OwnerId, cancelled.DefId))
                {
                    _playTheatre.Cancel();
                }
                else
                {
                    _playTheatre.CancelResponse(cancelled.OwnerId, cancelled.DefId); // a countered reaction, if it is one on the pile
                }
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

        /// <summary>Who currently owns the staged play (a reflection hands an attack over).</summary>
        private int _stagedAttacker = -1;

        /// <summary>
        /// Plans and attacks are staged on the table; instants (responses) slap onto
        /// them or get the reveal. False for a Black Market id (a cancelled purchase).
        /// </summary>
        private bool IsStagedPlay(string defId)
        {
            try
            {
                var type = _db.Lemon(defId).Type;
                return type == LemonCardType.Plan || type == LemonCardType.Attack;
            }
            catch
            {
                return false; // not a Lemon card
            }
        }

        /// <summary>
        /// Art of the discard card a take is lifting, for the plays that take one:
        /// Reduce and Reuse (a Black Market card) and Reverse Engineer (a Lemon card,
        /// unless it drew instead). Null for everything else.
        /// </summary>
        private UnityEngine.Texture2D TakenArt(LemonCardPlayed played)
        {
            if (played.TakenDefId == null ||
                (played.DefId != "reduce-and-reuse" && played.DefId != "reverse-engineer"))
            {
                return null;
            }
            return played.TakenIsLemon
                ? _art.Lemon(played.TakenDefId)
                : _art.BlackMarket(played.TakenDefId, played.TakenShape ?? Shape.Square);
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
            if (oldSnap == null)
            {
                return; // no baseline yet (fresh join): nothing to animate from
            }
            var freshSlots = newIds
                .Select((id, slot) => (Slot: slot, InstanceId: id))
                .Where(entry => !oldSnap.Any(c => c.InstanceId == entry.InstanceId))
                .ToList();

            if (refilled.RefreshedByPlayerId is int refresher)
            {
                // The paid sweep: the dollar comes out, the whole row slides under the
                // deck, and a fresh row is dealt out of it.
                var swept = oldSnap
                    .Select((card, slot) => (Slot: slot, Art: ShelfArt(card)))
                    .Where(entry => entry.Art != null)
                    .ToList();
                foreach (var (slot, art) in swept)
                {
                    HoldStandIn(freshSlots, slot, art);
                }
                QueueMarketDeal(freshSlots, swept, refresher, _db.Config.BlackMarketRefreshCost);
                return;
            }

            for (int slot = 0; slot < oldSnap.Count; slot++)
            {
                var card = oldSnap[slot];
                if (newIds.Contains(card.InstanceId))
                {
                    continue; // still on the shelf
                }
                var art = ShelfArt(card); // null: refilled and re-bought inside one burst, art never seen
                if (art == null)
                {
                    continue; // nothing to fly — leave the refill instant (status quo)
                }
                int originSlot = slot;   // geometric spot the departed card occupied
                HoldStandIn(freshSlots, originSlot, art);
                _fx.QueueSound(Sfx.CashRegister);
                _fx.QueuePurchase(art, _lastSpenderId, _lastMarketPrice, _art.Bill,
                    () => _table.MarketSlotWorld(originSlot),
                    null, ShowsPurchaseTheatre(_lastSpenderId),
                    onStarted: () => DropStandIns(freshSlots));
            }
            // The replacement(s) are dealt from the deck once the purchase has played.
            QueueMarketDeal(freshSlots, new List<(int Slot, UnityEngine.Texture2D Art)>(), payerId: -1, price: 0);
        }

        /// <summary>
        /// Hide the dealt cards' slots until each flight lands, then queue the deal.
        /// Art resolves when the effect starts, when the view has certainly caught up.
        /// </summary>
        private void QueueMarketDeal(List<(int Slot, int InstanceId)> dealt,
            List<(int Slot, UnityEngine.Texture2D Art)> swept, int payerId, int price)
        {
            if (dealt.Count == 0 && swept.Count == 0)
            {
                return;
            }
            var deals = new List<ShelfCardFlight>();
            foreach (var (slot, instanceId) in dealt)
            {
                _hiddenMarketIds.Add(instanceId);
                deals.Add(new ShelfCardFlight
                {
                    Art = () => MarketArt(instanceId),
                    SlotWorld = () => _table.MarketSlotWorld(slot),
                    OnLanded = () => ReleaseMarketCard(instanceId),
                });
            }
            var sweep = new List<ShelfCardFlight>();
            foreach (var (slot, art) in swept)
            {
                sweep.Add(new ShelfCardFlight
                {
                    Art = () => art,
                    SlotWorld = () => _table.MarketSlotWorld(slot),
                });
            }
            _fx.QueueMarketDeal(() => _table.BlackMarketDeckWorld(), sweep, deals, payerId, price, _art.Bill,
                onStarted: () => DropStandIns(dealt));
        }

        /// <summary>The card that left <paramref name="slot"/> keeps its place on the shelf, inert, until its effect plays.</summary>
        private void HoldStandIn(List<(int Slot, int InstanceId)> fresh, int slot, UnityEngine.Texture2D art)
        {
            foreach (var (freshSlot, instanceId) in fresh)
            {
                if (freshSlot == slot)
                {
                    _marketStandIns[instanceId] = art;
                }
            }
        }

        private void DropStandIns(List<(int Slot, int InstanceId)> fresh)
        {
            bool dropped = false;
            foreach (var (_, instanceId) in fresh)
            {
                dropped |= _marketStandIns.Remove(instanceId);
            }
            if (dropped)
            {
                _renderedRevision = -1; // the slots show their empty sockets under the flights
            }
        }

        private UnityEngine.Texture2D ShelfArt(PlayerView.CardInfo card) =>
            card.DefId == null ? null : _art.BlackMarket(card.DefId, card.Shape ?? Shape.Square);

        /// <summary>Art of a card on the shelf right now, by instance; null once it is gone.</summary>
        private UnityEngine.Texture2D MarketArt(int instanceId)
        {
            var card = View?.Market.FirstOrDefault(c => c.InstanceId == instanceId);
            return card == null ? null : ShelfArt(card);
        }

        /// <summary>
        /// A loss the bill pile already shows: a purchase (stand, Black Market card,
        /// Bragging Rights) or a market refresh slaps its money on the table, so the
        /// "-$X" floater would only say it again. Fines and the Swear Jar have no pile.
        /// </summary>
        private static bool PaysWithBills(string reason) =>
            reason != "timeout fine" && reason != "swear jar";

        /// <summary>
        /// Whose purchases get the showcase-and-fly treatment: everyone but the viewer,
        /// who just clicked the card and already knows what they bought (their money
        /// still slaps down).
        /// </summary>
        private bool ShowsPurchaseTheatre(int buyerId) => buyerId != _session.Seat;

        /// <summary>Drift delivery: let the hidden refill card show itself. Fires
        /// exactly once per flight on every path (landing, skip, effects Clear).</summary>
        private void ReleaseMarketCard(int instanceId)
        {
            _hiddenMarketIds.Remove(instanceId); // -1 (no paired refill) is a no-op
            _marketStandIns.Remove(instanceId);
            _renderedRevision = -1;              // surface the refilled card now
        }


        /// <summary>
        /// A roll's earnings stay collected across the Applies of the roll (windows can
        /// hold it open several frames), so they are staged only once the pending roll
        /// is gone. The viewer's own earnings ring the coins first.
        /// </summary>
        /// <summary>A roll's money is still being collected, or still sliding into bars.</summary>
        private bool EarningsStillShowing() =>
            _rollEarnings != null || _earningsInFlight.Values.Any(held => held > 0);

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
        /// A gain during a roll joins that roll's pile instead of floating as "+$X";
        /// its cash is held back from the bar until the pile lands there. Gains
        /// outside a roll (Profit Share, refunds) still float.
        /// </summary>
        private void CollectEarnings(int playerId, int amount)
        {
            if (_rollEarnings == null)
            {
                _fx.QueueMoney(playerId, amount);
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
            int n = View.Players.Count;
            UnityEngine.Debug.Log("[LW] roll earnings staged: " + (earnings.Count == 0 ? "nobody earned" :
                string.Join(", ", earnings.Select(kv => $"{NameOf(kv.Key)} +${kv.Value}"))));
            for (int offset = 0; offset < n; offset++)
            {
                int playerId = (_rollerId + offset) % n;
                if (!earnings.TryGetValue(playerId, out int amount) || amount <= 0)
                {
                    continue;
                }
                _fx.QueueEarnings(playerId, amount, _art.Bill, () =>
                {
                    _earningsInFlight[playerId] = System.Math.Max(0,
                        (_earningsInFlight.TryGetValue(playerId, out int held) ? held : 0) - amount);
                    _renderedRevision = -1; // the bar may show the new total now
                });
            }
        }

        // --------------------------------------------------------- action log

        /// <summary>
        /// Friendly one-liner for the action log; null for events too noisy to log
        /// (individual money ticks, window bookkeeping) or already folded into the
        /// line above (a run of draws grows its own line in place).
        /// </summary>
        private void AddLogLine(string line)
        {
            _actionLog.Add(line);
            if (_actionLog.Count > 40)
            {
                _actionLog.RemoveAt(0);
            }
            _renderedRevision = -1; // the log panel only redraws with the table
        }

        private string LogLine(GameEvent e)
        {
            switch (e)
            {
                case TurnStarted turn: return $"— {NameOf(turn.PlayerId)}'s turn —";
                case CardDrawn drawn: return DrawLine(drawn);
                case LemonCardPlayed played:
                    return $"{NameOf(played.PlayerId)} plays {LemonName(played.DefId)}" +
                           (played.TargetPlayerId is int t ? $" on {NameOf(t)}"
                            : played.RespondingToDefId != null && played.RespondingToOwnerId is int vs
                                ? $" on {NameOf(vs)}'s {CardName(played.RespondingToDefId)}"
                                : "");
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
                case AbilityDeclined declined:
                    return $"{NameOf(declined.PlayerId)} passes on {BlackMarketName(declined.DefId)}";
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
