using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>Table gestures into moves: hand-card menus and guided flows, attack and
    /// equip aiming, discard-pile takes, and market/supply drag-drop purchases.</summary>
    public sealed partial class LemonadeWarsApp
    {
        private void OpenHandMenu(int cardInstanceId)
        {
            var groups = CurrentGroups();
            if (groups == null || !groups.HandMoves.TryGetValue(cardInstanceId, out var moves))
            {
                return;
            }
            // Reaction window: clicking the card in hand IS the response — the live
            // reaction panel already shows what it answers. (Tag routes through
            // bar-aiming via AttackTargets and never reaches this menu.)
            if (moves.Count == 1 && moves[0] is RespondToWindow)
            {
                Submit(moves[0]);
                return;
            }
            var card = View.Hand.FirstOrDefault(c => c.InstanceId == cardInstanceId);
            string cardName = _db.Lemon(card?.DefId ?? "").Name;
            var cardArt = _art.Lemon(card?.DefId ?? "");

            var guided = GuidedOptions(cardName, cardArt, moves, onBack: null, out var direct);
            var options = ToOptions(direct);
            options.AddRange(guided);

            if (options.Count == 1)
            {
                options[0].OnPick(); // one way to play it: clicking the card IS the play
                return;
            }
            _prompt.Show(cardName, new[] { cardArt }, options, showCancel: true);
        }

        /// <summary>
        /// Multi-variant plays collapse into one option that opens a guided flow (the
        /// visual discard picker, the victim-then-card steal), instead of flooding a
        /// menu with one text row per combination. Whatever is left over comes back in
        /// <paramref name="direct"/> for the caller to list normally. Shared by the
        /// hand menu and the blur-modal variant page, so a card plays the same way
        /// whether you clicked it in hand or answered a forced/free play with it.
        /// </summary>
        private List<Prompt.Option> GuidedOptions(string cardName, Texture2D cardArt,
            IEnumerable<GameAction> moves, System.Action onBack, out List<GameAction> direct)
        {
            var all = moves.ToList();
            var bmTakes = all.OfType<PlayLemonCard>()
                .Where(m => m.DiscardedBmInstanceId != null).Cast<GameAction>().ToList();
            var lemonTakes = all.OfType<PlayLemonCard>()
                .Where(m => m.DiscardedLemonInstanceId != null).Cast<GameAction>().ToList();
            var equipSteals = all.OfType<PlayLemonCard>()
                .Where(m => m.TargetEquippedInstanceId != null).Cast<GameAction>().ToList();
            var ownStandPlays = all.OfType<PlayLemonCard>()
                .Where(m => m.TargetStandInstanceId != null &&
                            m.TargetEquippedInstanceId == null).Cast<GameAction>().ToList();
            var rummageSales = all.OfType<PlayLemonCard>()
                .Where(m => m.SelectedInstanceIds.Count > 0 && IsRummageSale(m)).Cast<GameAction>().ToList();
            direct = all
                .Where(m => !bmTakes.Contains(m) && !lemonTakes.Contains(m) &&
                            !equipSteals.Contains(m) && !ownStandPlays.Contains(m) &&
                            !rummageSales.Contains(m)).ToList();

            var options = new List<Prompt.Option>();
            if (rummageSales.Count > 0)
            {
                // Rummage Sale: pick the cards to sell off your real board, with the
                // card staged and the running total in the strip (see RummageSale).
                int rummageCard = ((PlayLemonCard)rummageSales[0]).CardInstanceId;
                options.Add(new Prompt.Option("Choose cards to sell on your board...",
                    () => BeginRummageSale(rummageCard)));
            }
            if (ownStandPlays.Count > 0)
            {
                // Doorbuster Sale / Rebrand: the target is one of YOUR stands, so
                // pick it on the real board (Word of Mouth's mechanism) instead of
                // reading "stand #2" off a text row.
                var byStand = ownStandPlays.Cast<PlayLemonCard>()
                    .GroupBy(m => m.TargetStandInstanceId)
                    .ToDictionary(g => g.Key, g => g.Cast<GameAction>().ToList());
                options.Add(new Prompt.Option("Choose a stand on your board...",
                    () => BeginOwnStandPick(cardArt, $"{cardName} — choose a stand", byStand,
                        onCancel: onBack ?? (() => { }))));
            }
            if (bmTakes.Count > 0)
            {
                options.Add(new Prompt.Option("Take a discarded Black Market card...",
                    () => OpenTakeFromDiscard(bmTakes, blackMarket: true, onBack)));
            }
            if (lemonTakes.Count > 0)
            {
                options.Add(new Prompt.Option("Take a discarded Lemon card...",
                    () => OpenTakeFromDiscard(lemonTakes, blackMarket: false, onBack)));
            }
            if (equipSteals.Count > 0)
            {
                options.Add(new Prompt.Option("Choose a player to target...",
                    () => OpenEquippedSteal(cardName, cardArt, equipSteals)));
            }
            return options;
        }

        /// <summary>That's Not Fair! discards its target instead of stealing it.</summary>
        private bool TargetsForDiscardOnly(IEnumerable<GameAction> moves) =>
            moves.OfType<PlayLemonCard>().Any(m =>
                View.Hand.FirstOrDefault(c => c.InstanceId == m.CardInstanceId)?.DefId
                    == "thats-not-fair");

        /// <summary>
        /// Two-step flow for cards that target an opponent's equipped Black Market card
        /// (Finders Keepers, That's Not Fair!): pick the victim, then pick the card off
        /// their board in the big picker — with BACK to reconsider the victim.
        /// </summary>
        private void OpenEquippedSteal(string cardName, Texture2D cardArt, List<GameAction> steals)
        {
            var byVictim = steals.Cast<PlayLemonCard>()
                .GroupBy(m => m.TargetPlayerId ?? FindEquipOwner(m.TargetEquippedInstanceId.Value))
                .ToDictionary(g => g.Key, g => g.Cast<GameAction>().ToList());

            void ShowVictims()
            {
                _prompt.Show($"{cardName}: choose who to target", new[] { cardArt },
                    byVictim.Select(kv => new Prompt.Option(
                        $"{NameOf(kv.Key)} ({EquippedCardInfos(kv.Key, kv.Value).Count} card(s))",
                        () => ShowCards(kv.Key))).ToList(),
                    showCancel: true);
            }

            void ShowCards(int victim)
            {
                _prompt.Hide();
                var victimMoves = byVictim[victim];
                var byEquipped = victimMoves.Cast<PlayLemonCard>()
                    .GroupBy(m => m.TargetEquippedInstanceId.Value)
                    .ToDictionary(g => g.Key, g => g.Cast<GameAction>().ToList());
                var cards = EquippedCardInfos(victim, victimMoves);
                // That's Not Fair! trashes the card; Finders Keepers takes it.
                bool trashes = TargetsForDiscardOnly(victimMoves);
                // The pick happens on the victim's REAL board: which upgrade sits on
                // which stand often decides the take, so the tucked cards themselves
                // are the targets. Arm BEFORE the snap render — the retained aim
                // rides through it and keeps MaybeShowModal stood down.
                _table.BeginEquippedCardPick(cardArt,
                    new HashSet<int>(byEquipped.Keys),
                    equippedId =>
                    {
                        var picked = cards.First(c => c.InstanceId == equippedId);
                        ResolveEquipDestination(byEquipped[equippedId],
                            _art.BlackMarket(picked.DefId, picked.Shape ?? Shape.Square));
                    },
                    onCancel: ShowVictims,
                    $"{cardName} — {(trashes ? "discard" : "take")} a card from {NameOf(victim)}'s board");
                if (_table.ViewedBoardPlayer != victim)
                {
                    _table.ViewedBoardPlayer = victim;
                    _renderedRevision = -1;
                    RenderIfChanged();
                }
            }

            ShowVictims();
        }

        /// <summary>
        /// The opponents this hand card can hit, when EVERY way to play it resolves to
        /// one victim — those cards aim at the player bars (click or drag) instead of
        /// opening a menu. Null means "not an attack": use the normal menu flow.
        /// </summary>
        private ISet<int> AttackTargets(int cardInstanceId)
        {
            var groups = CurrentGroups();
            if (groups == null || !groups.HandMoves.TryGetValue(cardInstanceId, out var moves) ||
                moves.Count == 0)
            {
                return null;
            }
            var targets = new HashSet<int>();
            foreach (var move in moves)
            {
                int victim = VictimOf(move);
                if (victim < 0)
                {
                    return null;
                }
                targets.Add(victim);
            }
            return targets;
        }

        /// <summary>
        /// The equipped Black Market cards this hand card can hit directly (That's Not
        /// Fair, Finders Keepers) — while aiming at the bars, the visible copies on
        /// the viewed board are precise targets of their own.
        /// </summary>
        private ISet<int> EquipAimTargets(int cardInstanceId)
        {
            var groups = CurrentGroups();
            if (groups == null || !groups.HandMoves.TryGetValue(cardInstanceId, out var moves))
            {
                return null;
            }
            var targets = new HashSet<int>();
            foreach (var move in moves)
            {
                if (move is PlayLemonCard play && play.TargetEquippedInstanceId is int eq)
                {
                    targets.Add(eq);
                }
            }
            return targets.Count > 0 ? targets : null;
        }

        /// <summary>
        /// Aimed straight at an equipped card on someone's board: confirm the exact
        /// hit, then fall into the shared destination flow (which just submits when
        /// there's nothing left to choose).
        /// </summary>
        private void ConfirmEquipAttack(int cardInstanceId, int equippedId)
        {
            var groups = CurrentGroups();
            if (groups == null || !groups.HandMoves.TryGetValue(cardInstanceId, out var moves))
            {
                return;
            }
            var variants = moves.OfType<PlayLemonCard>()
                .Where(p => p.TargetEquippedInstanceId == equippedId)
                .Cast<GameAction>()
                .ToList();
            var info = EquippedInfoOf(equippedId);
            if (variants.Count == 0 || info == null)
            {
                return;
            }
            // The aimed click on the card is the confirmation: That's Not Fair fires
            // outright; Finders Keepers goes on to pick where the loot lands.
            var art = _art.BlackMarket(info.DefId, info.Shape ?? Shape.Square);
            ResolveEquipDestination(variants, art);
        }

        private GameAction RefreshMove() =>
            CurrentGroups()?.BarMoves.FirstOrDefault(m => m is RefreshMarket);

        private GameAction DrawMove() =>
            CurrentGroups()?.BarMoves.FirstOrDefault(m => m is DrawLemonCard);

        /// <summary>
        /// Market slots a hand card can take from (Connections), or null when the card
        /// doesn't reach the shelf — the aiming layer treats null as "not mine".
        /// </summary>
        private ISet<int> MarketTargets(int cardInstanceId)
        {
            var groups = CurrentGroups();
            if (groups == null || !groups.HandMoves.TryGetValue(cardInstanceId, out var moves) ||
                moves.Count == 0)
            {
                return null;
            }
            var slots = new HashSet<int>();
            foreach (var move in moves)
            {
                if (!(move is PlayLemonCard play) || !(play.MarketIndex is int index))
                {
                    return null; // any non-market variant: this is not a shelf card
                }
                slots.Add(index);
            }
            return slots;
        }

        /// <summary>
        /// Connections picked a market card: place it exactly like a bought one —
        /// straight in when there's one legal slot, else aim at your board.
        /// </summary>
        private void ResolveMarketTake(int cardInstanceId, int marketIndex)
        {
            var groups = CurrentGroups();
            if (groups == null || !groups.HandMoves.TryGetValue(cardInstanceId, out var moves))
            {
                return;
            }
            var taken = moves
                .Where(m => m is PlayLemonCard play && play.MarketIndex == marketIndex)
                .ToList();
            if (taken.Count == 0)
            {
                return;
            }
            var card = View.Market[marketIndex];
            ResolveEquipDestination(taken, _art.BlackMarket(card.DefId, card.Shape ?? Shape.Square));
        }

        /// <summary>The opponent a play ultimately hits, or -1 when it isn't aimed at one.</summary>
        private int VictimOf(GameAction move)
        {
            // Reactions from the hand aim at bars too: Tag's "victim" is the redirect
            // target; Rubber Glue's is the attacker the play bounces back onto.
            if (move is RespondToWindow respond)
            {
                if (respond.RedirectTargetId is int redirect && redirect != View.ViewerId)
                {
                    return redirect;
                }
                var reactionCard = View.Hand.FirstOrDefault(c => c.InstanceId == respond.CardInstanceId);
                if (reactionCard?.DefId == "im-rubber-youre-glue" && View.StackTop != null &&
                    View.StackTop.OwnerId != View.ViewerId)
                {
                    return View.StackTop.OwnerId;
                }
                return -1;
            }
            if (!(move is PlayLemonCard play))
            {
                return -1;
            }
            if (play.TargetPlayerId is int victim)
            {
                return victim == View.ViewerId ? -1 : victim;
            }
            if (play.TargetEquippedInstanceId is int equipped)
            {
                int owner = FindEquipOwner(equipped);
                return owner == View.ViewerId ? -1 : owner;
            }
            return -1;
        }

        /// <summary>
        /// An attack was aimed and released on a player bar: submit it outright, or walk
        /// the remaining choices for that victim — which equipped card to hit (board
        /// picker, BACK re-aims) or which tantrum to hand off (small menu).
        /// </summary>
        private void ResolveAttackOnPlayer(int cardInstanceId, int victimId)
        {
            var groups = CurrentGroups();
            if (groups == null || !groups.HandMoves.TryGetValue(cardInstanceId, out var moves))
            {
                return;
            }
            var onVictim = moves.Where(m => VictimOf(m) == victimId).ToList();
            if (onVictim.Count == 0)
            {
                return;
            }

            var card = View.Hand.FirstOrDefault(c => c.InstanceId == cardInstanceId);
            string cardName = _db.Lemon(card?.DefId ?? "").Name;
            if (onVictim.Count == 1)
            {
                // One way to hit this victim (HOA Violation, a Tag redirect): the
                // deliberate aim-and-release IS the confirmation — the card stages
                // itself on the table, where a Tantrum can still answer it.
                Submit(onVictim[0]);
                return;
            }
            if (onVictim.Cast<PlayLemonCard>().All(m => m.TargetEquippedInstanceId != null))
            {
                var byEquipped = onVictim.Cast<PlayLemonCard>()
                    .GroupBy(m => m.TargetEquippedInstanceId.Value)
                    .ToDictionary(g => g.Key, g => g.Cast<GameAction>().ToList());
                var cards = EquippedCardInfos(victimId, onVictim);
                // That's Not Fair! trashes what it points at; Finders Keepers takes it.
                string verb = TargetsForDiscardOnly(onVictim) ? "DISCARD" : "TAKE";
                _table.OpenDiscardPicker(
                    $"{cardName.ToUpperInvariant()}: {verb} FROM {NameOf(victimId).ToUpperInvariant()}",
                    cards, blackMarket: true,
                    c => _db.BlackMarket(c.DefId).Name,
                    equippedId =>
                    {
                        var picked = cards.First(c => c.InstanceId == equippedId);
                        ResolveEquipDestination(byEquipped[equippedId],
                            _art.BlackMarket(picked.DefId, picked.Shape ?? Shape.Square));
                    },
                    onBack: () => _table.RestartAttackTargeting(cardInstanceId), verb: verb);
                return;
            }
            _prompt.Show($"{cardName} → {NameOf(victimId)}",
                new[] { _art.Lemon(card?.DefId ?? "") }, ToOptions(onVictim), showCancel: true);
        }

        /// <summary>CardInfos for the equipped instances these moves target, in board order.</summary>
        private List<PlayerView.CardInfo> EquippedCardInfos(int victim, List<GameAction> moves)
        {
            var wanted = moves.Cast<PlayLemonCard>()
                .Select(m => m.TargetEquippedInstanceId.Value).ToHashSet();
            var player = View.Players[victim];
            return player.TurfEquipped
                .Concat(player.Stands.SelectMany(st => st.Equipped))
                .Where(c => wanted.Contains(c.InstanceId))
                .ToList();
        }

        /// <summary>
        /// Choose which discard to take via the discard-pile picker overlay. The pool is
        /// every physical copy the engine offers — duplicates included, because players
        /// count the pile — and the confirm button names what the pick actually does.
        /// A recovered Lemon card is played on the spot (Reverse Engineer), so its verb
        /// says so and there is no separate confirmation beat afterwards.
        /// </summary>
        private void OpenTakeFromDiscard(List<GameAction> takes, bool blackMarket,
            System.Action onBack = null)
        {
            _prompt.Hide();
            var byCard = takes.Cast<PlayLemonCard>()
                .GroupBy(m => (blackMarket ? m.DiscardedBmInstanceId : m.DiscardedLemonInstanceId).Value)
                .ToDictionary(g => g.Key, g => g.Cast<GameAction>().ToList());
            // The whole pile, not just the takeable copies: a hidden card reads as a
            // bug when the player has counted the discards. Untakeable copies (a
            // reaction with nothing to react to, a Black Market card with no legal
            // destination) render dimmed with the reason instead.
            var pool = (blackMarket ? View.BlackMarketDiscard : View.LemonDiscard).ToList();
            string verb = blackMarket ? "TAKE" : "TAKE AND PLAY";

            _table.OpenDiscardPicker(
                blackMarket ? "TAKE A DISCARDED BLACK MARKET CARD" : "TAKE A DISCARDED LEMON CARD",
                pool, blackMarket,
                c => blackMarket ? _db.BlackMarket(c.DefId).Name : _db.Lemon(c.DefId).Name,
                instanceId =>
                {
                    var picked = pool.First(c => c.InstanceId == instanceId);
                    var texture = blackMarket
                        ? _art.BlackMarket(picked.DefId, picked.Shape ?? Shape.Square)
                        : _art.Lemon(picked.DefId);
                    ResolveEquipDestination(byCard[instanceId], texture);
                },
                onBack: onBack, verb: verb,
                disabledReason: c => byCard.ContainsKey(c.InstanceId)
                    ? null
                    : blackMarket ? "NOWHERE TO EQUIP IT" : "CAN'T BE PLAYED NOW");
        }

        /// <summary>
        /// Finish a play whose variants differ only by equip destination: one candidate
        /// submits directly; several open the aim-at-your-board targeting (float card +
        /// arrow), with a replace-prompt when the chosen slot is full.
        /// </summary>
        private void ResolveEquipDestination(List<GameAction> candidates, Texture2D texture) =>
            ResolveEquipDestination(candidates, texture,
                m => ((PlayLemonCard)m).EquipStandInstanceId);

        private void ResolveEquipDestination(List<GameAction> candidates, Texture2D texture,
            System.Func<GameAction, int?> destinationOf, System.Action onCancel = null)
        {
            if (candidates.Count == 1)
            {
                Submit(candidates[0]);
                return;
            }
            var byDest = candidates
                .GroupBy(destinationOf)
                .ToDictionary(g => g.Key, g => g.ToList());
            System.Action<int?> pickDestination = destId =>
            {
                var atDest = byDest[destId];
                if (atDest.Count == 1)
                {
                    Submit(atDest[0]);
                }
                else
                {
                    _prompt.Show("That slot is full — replace which card?",
                        new[] { texture }, ToOptions(atDest), showCancel: true);
                }
            };
            if (byDest.Count == 1)
            {
                // Only one valid target: apply without the selector.
                pickDestination(byDest.Keys.First());
                return;
            }
            // The destinations are YOUR stands: the card rides the cursor over your
            // own board until a click on a lit stand places it (letting go drops the play).
            ShowOwnBoard();
            _table.BeginStickyPlace(texture, new HashSet<int?>(byDest.Keys), pickDestination,
                onCancel ?? (() => { }));
        }

        /// <summary>
        /// Route a "choose one of your stands" choice to the REAL board: snap home,
        /// dim everything but your row, and let the click land on the stand itself —
        /// sale numbers, tucked upgrades and earnings are exactly what the choice is
        /// decided by. Shared by the Word of Mouth decision and by hand plays whose
        /// variants differ by own-stand target (Doorbuster Sale, Rebrand). A stand
        /// with several variants (Rebrand's new types) gets a follow-up page.
        /// </summary>
        private void BeginOwnStandPick(Texture2D art, string caption,
            Dictionary<int?, List<GameAction>> byStand, System.Action onCancel)
        {
            void PickStand(int? standId)
            {
                var atStand = byStand[standId];
                if (atStand.Count == 1)
                {
                    Submit(atStand[0]);
                }
                else
                {
                    _prompt.Show(caption,
                        art != null ? new[] { art } : new Texture2D[0],
                        ToOptions(atStand), showCancel: true);
                }
            }
            if (byStand.Count == 1 && byStand.Values.First().Count == 1)
            {
                // One stand, one way: a board pick would be a dead click.
                Submit(byStand.Values.First()[0]);
                return;
            }
            // The stands are YOURS: arm first, THEN snap home if the board was on a
            // rival — the retained aim rides through the synchronous render, which
            // also keeps MaybeShowModal stood down during it.
            _table.BeginEquipTargeting(art, new HashSet<int?>(byStand.Keys),
                PickStand, onCancel, caption);
            if (_table.ViewedBoardPlayer >= 0)
            {
                _table.ViewedBoardPlayer = -1;
                _renderedRevision = -1;
                RenderIfChanged();
            }
        }

        private void OnMarketDragStart(int marketIndex)
        {
            _preview.SetDragging(true);
            var groups = CurrentGroups();
            if (groups == null || !groups.MarketMoves.TryGetValue(marketIndex, out var moves))
            {
                return;
            }
            var valid = new HashSet<int?>(moves.OfType<BuyBlackMarket>()
                .Select(m => m.TargetStandInstanceId));
            _table.SetValidDropTargets(valid);
        }

        private void OnMarketDrop(int marketIndex, int? standInstanceId)
        {
            _table.ClearDropHighlights();
            var groups = CurrentGroups();
            if (groups == null || !groups.MarketMoves.TryGetValue(marketIndex, out var moves))
            {
                return;
            }
            var matching = moves.OfType<BuyBlackMarket>()
                .Where(m => m.TargetStandInstanceId == standInstanceId)
                .Cast<GameAction>()
                .ToList();
            var card = View.Market[marketIndex];
            if (matching.Count == 1)
            {
                if (_liftedMarketIds.Contains(card.InstanceId) && _liftedCardLanding == null)
                {
                    _liftedCardLanding = Input.mousePosition; // a held card buys from under the cursor
                }
                Submit(matching[0]);
            }
            else
            {
                // A question (or nothing to buy) in between: the card waits on its slot.
                RestoreMarketCard(card.InstanceId);
                if (matching.Count > 1)
                {
                    _prompt.Show("That slot is full — replace which card?",
                        new[] { _art.BlackMarket(card.DefId, card.Shape ?? Shape.Square) },
                        ToOptions(matching), showCancel: true);
                }
            }
        }

        /// <summary>
        /// Double-click on a shelf card buys it without the drag. One legal home: the
        /// card glides from its slot to that stand and the buy lands on arrival. Several:
        /// the card rides the cursor and the next click on a lit stand places it.
        /// </summary>
        private void OnMarketDoubleClick(int marketIndex)
        {
            _preview.Hide();
            var groups = CurrentGroups();
            if (groups == null || !groups.MarketMoves.TryGetValue(marketIndex, out var moves))
            {
                return;
            }
            var card = View.Market[marketIndex];
            var art = _art.BlackMarket(card.DefId, card.Shape ?? Shape.Square);
            var buys = moves.OfType<BuyBlackMarket>().ToList();
            // One place with room decides it, even when full stands would also take
            // the card by replacing something: nobody double-clicks to trash a card.
            var destinations = buys.Where(b => b.ReplaceInstanceId == null)
                .Select(b => b.TargetStandInstanceId).Distinct().ToList();
            if (destinations.Count == 0)
            {
                destinations = buys.Select(b => b.TargetStandInstanceId).Distinct().ToList();
            }
            ShowOwnBoard();
            // The slot is measured while the card still sits in it, then empties: the
            // card itself leaves the shelf, not a copy of it.
            var slotWorld = _table.MarketSlotWorld(marketIndex);
            int instanceId = card.InstanceId;
            LiftMarketCard(instanceId);
            if (destinations.Count == 1)
            {
                int? destination = destinations[0];
                var landing = _table.DropCellWorld(destination);
                _table.GlideShelfCard(art, slotWorld, landing, () =>
                {
                    _liftedCardLanding = landing;
                    OnMarketDrop(marketIndex, destination);
                });
                return;
            }
            _table.OnStickyReturned = () => RestoreMarketCard(instanceId);
            _table.BeginMarketStickyDrag(marketIndex, art);
        }

        /// <summary>Double-click on a supply pile: with one place it can go (your first
        /// stand) it glides straight there; otherwise it rides the cursor until a click
        /// places it in your row, exactly where a drag would have dropped it.</summary>
        private void OnSupplyDoubleClick(string standTypeId)
        {
            _preview.Hide();
            var groups = CurrentGroups();
            if (groups == null || !groups.SupplyMoves.TryGetValue(standTypeId, out var moves))
            {
                return;
            }
            ShowOwnBoard();
            if (moves.Count == 1)
            {
                var only = moves[0];
                _table.GlideShelfCard(_table.SupplyArtOf(standTypeId), _table.StandSupplyWorld(standTypeId),
                    _table.NextStandSlotWorld(), () => Submit(only));
                return;
            }
            _table.BeginSupplyStickyDrag(standTypeId);
        }

        /// <summary>Double-click on Bragging Rights: it glides onto your VP column, then buys.</summary>
        private void OnBraggingDoubleClick()
        {
            _preview.Hide();
            if (CurrentGroups()?.BraggingMoves.Count > 0 != true)
            {
                return;
            }
            ShowOwnBoard();
            _table.GlideShelfCard(_table.BraggingArt, _table.BraggingShelfWorld(),
                _table.VictoryCellWorld(), BuyBraggingRights);
        }

        private void BuyBraggingRights()
        {
            var moves = CurrentGroups()?.BraggingMoves;
            if (moves != null && moves.Count > 0)
            {
                Submit(moves[0]);
            }
        }

        /// <summary>Shelf purchases land on YOUR board: snap the view home from a rival's.</summary>
        private void ShowOwnBoard()
        {
            if (_table.ViewedBoardPlayer >= 0)
            {
                _table.ViewedBoardPlayer = -1;
                _renderedRevision = -1;
                RenderIfChanged();
            }
        }

        private void OnSupplyDrop(string standTypeId, int insertIndex)
        {
            var groups = CurrentGroups();
            if (groups == null || !groups.SupplyMoves.TryGetValue(standTypeId, out var moves))
            {
                return;
            }
            var pick = moves.FirstOrDefault(m =>
                    (m as BuyStand)?.InsertIndex == insertIndex ||
                    (m as InitialBuyStand)?.InsertIndex == insertIndex)
                ?? moves[moves.Count - 1];
            Submit(pick);
        }
    }
}
