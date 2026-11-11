using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>The headline and the card art of a decision surface.</summary>
    public sealed partial class LemonadeWarsApp
    {
        private string ModalTitle()
        {
            var decision = View.MyDecisions.FirstOrDefault();
            if (decision != null)
            {
                return DecisionTitle(decision);
            }
            if (View.StackTop != null)
            {
                return StackResponseTitle(View.StackTop);
            }
            if (View.PendingRollValue is int roll)
            {
                // The ordinary turn sale stays short; the surprising rolls say what they are.
                return RollScopeName(View.PendingRollPurpose, View.PendingRollStandInstanceId)
                        is string why
                    ? $"{why} roll: the die shows {roll} — respond?"
                    : $"The die shows {roll} — respond?";
            }
            if (View.TheftOnMe)
            {
                return TheftResponseTitle();
            }
            return "Your choice";
        }

        private string DecisionTitle(PlayerView.DecisionInfo decision)
        {
            switch (decision.Kind)
            {
                case DecisionKind.TimeoutFine:
                    return $"Timeout! Pay the ${decision.RequiredMoney} fine " +
                           $"(you have ${View.Players[View.ViewerId].Money})";
                case DecisionKind.AttackRetarget: return "Your attack was redirected — pick a new target";
                case DecisionKind.FreePlayOffer: return "Smear Campaign: free play?";
                case DecisionKind.ForcedPlay:
                    // Only reached when the play still needs a choice.
                    return RecoveredCardDefId(decision) is string recovered
                        ? $"Reverse Engineer: play {LemonName(recovered)}"
                        : "Reverse Engineer: play the recovered card";
                case DecisionKind.BouncerAttack:
                    // The attack has already left the stack, so the title names the provocation.
                    return decision.ContextDefId != null && decision.ContextPlayerId is int attacker
                        ? $"{NameOf(attacker)} attacked you with " +
                          $"{LemonName(decision.ContextDefId)} — strike back?"
                        : "Bouncer: strike back?";
                case DecisionKind.AbilityVictim: return WithSourcePrefix(decision, "choose who to rob");
                case DecisionKind.InnovationCopy: return "Innovation: copy which ability?";
                case DecisionKind.WordOfMouthStand: return "Word of Mouth: which stand sells?";
                default: return decision.Kind.ToString();
            }
        }

        private string StackResponseTitle(PlayerView.StackTopInfo top)
        {
            string cardName = top.IsPurchase
                ? _db.BlackMarket(top.DefId).Name
                : _db.Lemon(top.DefId).Name;
            if (top.AttackTargetId is int targetId)
            {
                return AttackResponseTitle(top, cardName, targetId);
            }
            // Responses name WHAT they answer: "Dex tantrums" alone does not say which card.
            string answered = ModalAnsweredPhrase(top);
            if (top.DefId == "tantrum" && answered != null)
            {
                return $"{NameOf(top.OwnerId)} tantrums {answered}! — respond?";
            }
            if (top.DefId == "tag-youre-it" && top.RedirectTargetId is int redirect)
            {
                // Who it is going to, with the intel that decides a counter-tantrum.
                var target = View.Players[redirect];
                string who = redirect == View.ViewerId ? "YOU" : NameOf(redirect);
                return $"{NameOf(top.OwnerId)} tags {answered ?? "the attack"} to {who} " +
                       $"(${target.Money}, {target.HandCount} cards) — respond?";
            }
            if (top.DefId == "im-rubber-youre-glue" && answered != null)
            {
                string backAt = top.RespondingToOwnerId is int attacker
                    ? (attacker == View.ViewerId ? "YOU" : NameOf(attacker))
                    : "the attacker";
                return $"{NameOf(top.OwnerId)} reflects {answered} back at {backAt}! — respond?";
            }
            string what = top.IsPurchase
                ? $"{NameOf(top.OwnerId)} is buying {cardName}"
                : $"{NameOf(top.OwnerId)} played {cardName}";
            if (!string.IsNullOrEmpty(top.TakenDefId))
            {
                // Connections / Reduce and Reuse / Reverse Engineer: a tantrum hinges on WHAT they reach for.
                what += $", taking {CardName(top.TakenDefId)}";
            }
            return $"{what} — respond?";
        }

        private string AttackResponseTitle(PlayerView.StackTopInfo top, string cardName, int targetId)
        {
            string victim = targetId == View.ViewerId ? "you" : NameOf(targetId);
            string title = $"{NameOf(top.OwnerId)} attacks {victim} with {cardName}!";
            if (!string.IsNullOrEmpty(top.StolenDefId))
            {
                // Finders Keepers / That's Not Fair: name the loot.
                title += $"  Target: {_db.BlackMarket(top.StolenDefId).Name}.";
            }
            return title;
        }

        /// <summary>What a response answers ("your Slander", "the Bouncer purchase"); null for a plain play.</summary>
        private string ModalAnsweredPhrase(PlayerView.StackTopInfo top)
        {
            if (string.IsNullOrEmpty(top.RespondingToDefId))
            {
                return null;
            }
            string answered = top.RespondingToIsPurchase
                ? $"the {_db.BlackMarket(top.RespondingToDefId).Name} purchase"
                : _db.Lemon(top.RespondingToDefId).Name;
            if (top.RespondingToOwnerId is int owner && !top.RespondingToIsPurchase)
            {
                answered = (owner == View.ViewerId ? "your " : NameOf(owner) + "'s ") + answered;
            }
            return answered;
        }

        /// <summary>"$4 stolen, $2 back" IS the decision, so the thief and the numbers are the headline.</summary>
        private string TheftResponseTitle()
        {
            string thief = View.TheftAttackerId is int robber ? NameOf(robber) : "Someone";
            string stolen = View.TheftAmount is int amount ? $"${amount}" : "money";
            return View.TheftRecoverable is int back && back > 0
                ? $"{thief} stole {stolen} from you — play Profit Share to take ${back} back?"
                : $"{thief} stole {stolen} from you — Profit Share?";
        }

        /// <summary>"Juice Box Joey: do the thing" when the ability's source card is known.</summary>
        private string WithSourcePrefix(PlayerView.DecisionInfo decision, string action)
        {
            string source = SourceCardName(decision);
            return source != null
                ? $"{source}: {action}"
                : char.ToUpperInvariant(action[0]) + action.Substring(1);
        }

        /// <summary>"Juice Box Joey" for a decision raised by an equipped ability; null otherwise.</summary>
        private string SourceCardName(PlayerView.DecisionInfo decision)
        {
            if (!(decision?.SourceInstanceId is int sourceId))
            {
                return null;
            }
            var info = EquippedInfoOf(sourceId);
            return info == null ? null : BlackMarketName(info.DefId);
        }

        /// <summary>The card a ForcedPlay decision is about (it sits in hand by then).</summary>
        private string RecoveredCardDefId(PlayerView.DecisionInfo decision) =>
            decision.CardInstanceId is int instanceId ? HandCardDefId(instanceId) : null;

        private List<Texture2D> ModalCards()
        {
            var cards = new List<Texture2D>();
            var decision = View.MyDecisions.FirstOrDefault();
            if (decision != null && decision.Kind == DecisionKind.ForcedPlay &&
                RecoveredCardDefId(decision) is string recovered)
            {
                cards.Add(_art.Lemon(recovered));
            }
            // The play that provoked the decision (Bouncer: the attack that just hit you).
            if (decision?.ContextDefId != null)
            {
                cards.Add(_art.Lemon(decision.ContextDefId));
            }
            // The equipped card whose ability is asking.
            if (decision?.SourceInstanceId is int sourceId)
            {
                var sourceArt = EquippedArt(sourceId);
                if (sourceArt != null)
                {
                    cards.Add(sourceArt);
                }
            }
            if (View.StackTop != null)
            {
                AddStackTopCards(cards, View.StackTop);
            }
            return cards;
        }

        /// <summary>The resolving card, then what it steals, takes or answers.</summary>
        private void AddStackTopCards(List<Texture2D> cards, PlayerView.StackTopInfo top)
        {
            cards.Add(top.IsPurchase
                ? _art.BlackMarket(top.DefId, top.Shape ?? Shape.Square)
                : _art.Lemon(top.DefId));
            if (!string.IsNullOrEmpty(top.StolenDefId))
            {
                cards.Add(_art.BlackMarket(top.StolenDefId, top.StolenShape ?? Shape.Square));
            }
            if (!string.IsNullOrEmpty(top.TakenDefId))
            {
                cards.Add(top.TakenIsLemon
                    ? _art.Lemon(top.TakenDefId)
                    : _art.BlackMarket(top.TakenDefId, top.TakenShape ?? Shape.Square));
            }
            if (!string.IsNullOrEmpty(top.RespondingToDefId))
            {
                cards.Add(top.RespondingToIsPurchase
                    ? _art.BlackMarket(top.RespondingToDefId, top.RespondingToShape ?? Shape.Square)
                    : _art.Lemon(top.RespondingToDefId));
            }
        }
    }
}
