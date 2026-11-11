using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>Hand-card gestures into moves: the card menu, guided flows for multi-variant
    /// plays, discard-pile takes and Connections' market take.</summary>
    public sealed partial class LemonadeWarsApp
    {
        private bool TryGetHandMoves(int cardInstanceId, out List<GameAction> moves)
        {
            moves = null;
            var groups = CurrentGroups();
            return groups != null && groups.HandMoves.TryGetValue(cardInstanceId, out moves);
        }

        private GameAction RefreshMove() =>
            CurrentGroups()?.BarMoves.FirstOrDefault(m => m is RefreshMarket);

        private GameAction DrawMove() =>
            CurrentGroups()?.BarMoves.FirstOrDefault(m => m is DrawLemonCard);

        private void OpenHandMenu(int cardInstanceId)
        {
            if (!TryGetHandMoves(cardInstanceId, out var moves))
            {
                return;
            }
            // In a reaction window the click IS the response. (Tag aims at the bars via
            // AttackTargets and never reaches this menu.)
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

        /// <summary>Multi-variant plays become one option each that opens a guided flow; the rest come back
        /// in <paramref name="direct"/>. Shared with the modal variant page, so a card plays the same either way.</summary>
        private List<Prompt.Option> GuidedOptions(string cardName, Texture2D cardArt,
            IEnumerable<GameAction> moves, System.Action onBack, out List<GameAction> direct)
        {
            var all = moves.ToList();
            var blackMarketTakes = LemonPlaysWhere(all, m => m.DiscardedBmInstanceId != null);
            var lemonTakes = LemonPlaysWhere(all, m => m.DiscardedLemonInstanceId != null);
            var equipSteals = LemonPlaysWhere(all, m => m.TargetEquippedInstanceId != null);
            var ownStandPlays = LemonPlaysWhere(all,
                m => m.TargetStandInstanceId != null && m.TargetEquippedInstanceId == null);
            var rummageSales = LemonPlaysWhere(all, m => m.SelectedInstanceIds.Count > 0 && IsRummageSale(m));
            direct = all
                .Where(m => !blackMarketTakes.Contains(m) && !lemonTakes.Contains(m) &&
                            !equipSteals.Contains(m) && !ownStandPlays.Contains(m) &&
                            !rummageSales.Contains(m)).ToList();

            var options = new List<Prompt.Option>();
            if (rummageSales.Count > 0)
            {
                int rummageCardId = ((PlayLemonCard)rummageSales[0]).CardInstanceId;
                options.Add(new Prompt.Option("Choose cards to sell on your board...",
                    () => BeginRummageSale(rummageCardId)));
            }
            if (ownStandPlays.Count > 0)
            {
                // Doorbuster Sale / Rebrand target one of YOUR stands: picked on the real board.
                var byStand = ownStandPlays.Cast<PlayLemonCard>()
                    .GroupBy(m => m.TargetStandInstanceId)
                    .ToDictionary(g => g.Key, g => g.Cast<GameAction>().ToList());
                options.Add(new Prompt.Option("Choose a stand on your board...",
                    () => BeginOwnStandPick(cardArt, $"{cardName} — choose a stand", byStand,
                        onCancel: onBack ?? (() => { }))));
            }
            if (blackMarketTakes.Count > 0)
            {
                options.Add(new Prompt.Option("Take a discarded Black Market card...",
                    () => OpenTakeFromDiscard(blackMarketTakes, blackMarket: true, onBack)));
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

        private static List<GameAction> LemonPlaysWhere(List<GameAction> moves,
            System.Func<PlayLemonCard, bool> matches) =>
            moves.OfType<PlayLemonCard>().Where(matches).Cast<GameAction>().ToList();

        /// <summary>Pick a discard to take in the pile overlay. A recovered Lemon card is played on the
        /// spot (Reverse Engineer), so its confirm verb says so.</summary>
        private void OpenTakeFromDiscard(List<GameAction> takes, bool blackMarket,
            System.Action onBack = null)
        {
            _prompt.Hide();
            var byCard = takes.Cast<PlayLemonCard>()
                .GroupBy(m => (blackMarket ? m.DiscardedBmInstanceId : m.DiscardedLemonInstanceId).Value)
                .ToDictionary(g => g.Key, g => g.Cast<GameAction>().ToList());
            // The whole pile, duplicates included, because players count it: untakeable
            // copies render dimmed with the reason instead of vanishing.
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

        /// <summary>Market slots a hand card can take from (Connections); null when the card does not
        /// reach the shelf, which the aiming layer reads as "not mine".</summary>
        private ISet<int> MarketTargets(int cardInstanceId)
        {
            if (!TryGetHandMoves(cardInstanceId, out var moves) || moves.Count == 0)
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

        /// <summary>Connections picked a market card: it is placed exactly like a bought one.</summary>
        private void ResolveMarketTake(int cardInstanceId, int marketIndex)
        {
            if (!TryGetHandMoves(cardInstanceId, out var moves))
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
    }
}
