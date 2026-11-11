using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Unity
{
    /// <summary>Decisions answered by aiming at the live table (a player bar, a stand, a tucked card) instead of a modal.</summary>
    public sealed partial class LemonadeWarsApp
    {
        /// <summary>
        /// "Choose who to rob" aims at the player bars: money, hand size and VP are exactly
        /// the stats a victim is picked by.
        /// </summary>
        private bool TryShowPlayerPick(MoveGroups groups)
        {
            var decision = View.MyDecisions.FirstOrDefault();
            if (decision == null || decision.Kind != DecisionKind.AbilityVictim)
            {
                return false;
            }
            if (_table.PlayerPickActive)
            {
                return true; // already aiming this decision
            }
            var byVictim = new Dictionary<int, GameAction>();
            GameAction decline = null;
            foreach (var move in groups.ModalMoves)
            {
                if (move is SubmitAbilityChoice choice && choice.TargetPlayerId is int victim)
                {
                    byVictim[victim] = move;
                }
                else if (move is SubmitAbilityChoice pass && pass.Decline)
                {
                    decline = move;
                }
            }
            if (byVictim.Count == 0)
            {
                return false;
            }
            var art = decision.SourceInstanceId is int sourceId ? EquippedArt(sourceId) : null;
            _table.BeginPlayerPick(art, new HashSet<int>(byVictim.Keys),
                picked => Submit(byVictim[picked]),
                onPass: decline != null ? () => Submit(decline) : null);
            return true;
        }

        /// <summary>Word of Mouth's "which stand sells?" aims at the real stands, sale numbers and tucked upgrades in view.</summary>
        private bool TryShowStandPick(MoveGroups groups)
        {
            var decision = View.MyDecisions.FirstOrDefault();
            if (decision == null || decision.Kind != DecisionKind.WordOfMouthStand)
            {
                return false;
            }
            var byStand = new Dictionary<int?, List<GameAction>>();
            foreach (var move in groups.ModalMoves)
            {
                if (move is SubmitAbilityChoice choice && choice.StandInstanceId is int stand)
                {
                    if (!byStand.TryGetValue(stand, out var list))
                    {
                        byStand[stand] = list = new List<GameAction>();
                    }
                    list.Add(move);
                }
            }
            if (byStand.Count == 0)
            {
                return false;
            }
            var art = decision.SourceInstanceId is int sourceId ? EquippedArt(sourceId) : null;
            // The choice is mandatory: a click on empty felt re-arms it instead of cancelling.
            BeginOwnStandPick(art, "Word of Mouth — choose a stand to sell", byStand,
                onCancel: ReopenModalNextTick);
            return true;
        }

        /// <summary>
        /// A Tag sent a card-stealing attack at someone else: show THEIR board, aim at their tucked
        /// cards, then pick where a steal lands. False when the retarget is not a card pick.
        /// </summary>
        private bool TryBeginRetargetPick(MoveGroups groups)
        {
            var retargets = groups.ModalMoves.OfType<SubmitRetarget>()
                .Where(m => m.TargetEquippedInstanceId != null).ToList();
            if (retargets.Count == 0 || retargets.Count != groups.ModalMoves.Count)
            {
                return false;
            }
            var byCard = retargets
                .GroupBy(m => m.TargetEquippedInstanceId.Value)
                .ToDictionary(g => g.Key, g => g.Cast<GameAction>().ToList());
            int victim = FindEquipOwner(byCard.Keys.First());
            _prompt.Hide();
            _reaction.Hide();
            if (victim >= 0 && victim != View.ViewerId && _table.ViewedBoardPlayer != victim)
            {
                // Their board renders on the next tick. Arming now is fine (the aim re-finds
                // its cells every frame) and avoids re-entering the render.
                _table.ViewedBoardPlayer = victim;
                _renderedRevision = -1;
            }
            // The retarget has to be answered: a click-off anywhere just re-arms the aim.
            System.Action reArm = ReopenModalNextTick;
            string attack = View.StackTop != null ? CardName(View.StackTop.DefId) : "your attack";
            var attackArt = View.StackTop != null ? _art.Lemon(View.StackTop.DefId) : null;
            _table.BeginEquippedCardPick(attackArt, new HashSet<int>(byCard.Keys),
                equippedId => ResolveEquipDestination(byCard[equippedId], EquippedArt(equippedId),
                    m => ((SubmitRetarget)m).EquipStandInstanceId, reArm),
                reArm,
                $"Tagged to {NameOf(victim)} — aim {attack} at one of their cards");
            return true;
        }
    }
}
