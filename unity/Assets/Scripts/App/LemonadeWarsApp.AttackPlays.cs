using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>Hand cards aimed at an opponent: who and what they can hit, and the
    /// choices left once the aim lands on a player bar or an equipped card.</summary>
    public sealed partial class LemonadeWarsApp
    {
        /// <summary>The opponents this hand card can hit when EVERY way to play it resolves to one
        /// victim: such cards aim at the player bars. Null means "not an attack": use the menu.</summary>
        private ISet<int> AttackTargets(int cardInstanceId)
        {
            if (!TryGetHandMoves(cardInstanceId, out var moves) || moves.Count == 0)
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

        /// <summary>Equipped Black Market cards this hand card can hit directly (That's Not Fair,
        /// Finders Keepers): precise targets on the viewed board while aiming at the bars.</summary>
        private ISet<int> EquipAimTargets(int cardInstanceId)
        {
            if (!TryGetHandMoves(cardInstanceId, out var moves))
            {
                return null;
            }
            var targets = new HashSet<int>();
            foreach (var move in moves)
            {
                if (move is PlayLemonCard play && play.TargetEquippedInstanceId is int equippedId)
                {
                    targets.Add(equippedId);
                }
            }
            return targets.Count > 0 ? targets : null;
        }

        /// <summary>The opponent a play ultimately hits, or -1 when it isn't aimed at one.</summary>
        private int VictimOf(GameAction move)
        {
            // Hand reactions aim at bars too: Tag's victim is the redirect target, Rubber
            // Glue's the attacker the play bounces back onto.
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

        /// <summary>The aimed click on an equipped card is the confirmation: That's Not Fair fires
        /// outright; Finders Keepers goes on to pick where the loot lands.</summary>
        private void ConfirmEquipAttack(int cardInstanceId, int equippedId)
        {
            if (!TryGetHandMoves(cardInstanceId, out var moves))
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
            var art = _art.BlackMarket(info.DefId, info.Shape ?? Shape.Square);
            ResolveEquipDestination(variants, art);
        }

        /// <summary>An attack was aimed and released on a player bar: submit it, or walk the choices
        /// left for that victim (which equipped card to hit, or which variant from a small menu).</summary>
        private void ResolveAttackOnPlayer(int cardInstanceId, int victimId)
        {
            if (!TryGetHandMoves(cardInstanceId, out var moves))
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
                // One way to hit this victim: the aim-and-release IS the confirmation. The
                // card stages itself on the table, where a Tantrum can still answer it.
                Submit(onVictim[0]);
                return;
            }
            if (onVictim.Cast<PlayLemonCard>().All(m => m.TargetEquippedInstanceId != null))
            {
                OpenEquippedTargetPicker(cardInstanceId, cardName, victimId, onVictim);
                return;
            }
            _prompt.Show($"{cardName} → {NameOf(victimId)}",
                new[] { _art.Lemon(card?.DefId ?? "") }, ToOptions(onVictim), showCancel: true);
        }

        private void OpenEquippedTargetPicker(int cardInstanceId, string cardName, int victimId,
            List<GameAction> onVictim)
        {
            var byEquipped = onVictim.Cast<PlayLemonCard>()
                .GroupBy(m => m.TargetEquippedInstanceId.Value)
                .ToDictionary(g => g.Key, g => g.Cast<GameAction>().ToList());
            var cards = EquippedCardInfos(victimId, onVictim);
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
        }

        /// <summary>Finders Keepers / That's Not Fair! from the menu: pick the victim, then the card
        /// off their board, with BACK to reconsider the victim.</summary>
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
                bool discardsTarget = TargetsForDiscardOnly(victimMoves);
                // Picked on the victim's REAL board: which stand an upgrade sits on often decides the take.
                // Arm BEFORE the snap render: the retained aim rides through it and keeps MaybeShowModal stood down.
                _table.BeginEquippedCardPick(cardArt,
                    new HashSet<int>(byEquipped.Keys),
                    equippedId =>
                    {
                        var picked = cards.First(c => c.InstanceId == equippedId);
                        ResolveEquipDestination(byEquipped[equippedId],
                            _art.BlackMarket(picked.DefId, picked.Shape ?? Shape.Square));
                    },
                    onCancel: ShowVictims,
                    $"{cardName} — {(discardsTarget ? "discard" : "take")} a card from {NameOf(victim)}'s board");
                if (_table.ViewedBoardPlayer != victim)
                {
                    _table.ViewedBoardPlayer = victim;
                    _renderedRevision = -1;
                    RenderIfChanged();
                }
            }

            ShowVictims();
        }

        /// <summary>That's Not Fair! discards its target instead of stealing it.</summary>
        private bool TargetsForDiscardOnly(IEnumerable<GameAction> moves) =>
            moves.OfType<PlayLemonCard>().Any(m =>
                View.Hand.FirstOrDefault(c => c.InstanceId == m.CardInstanceId)?.DefId
                    == "thats-not-fair");

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
    }
}
