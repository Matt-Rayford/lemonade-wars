using System;
using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>
    /// First real opponent: one-ply heuristic scoring, no lookahead. Buys VP when it can,
    /// builds economy, attacks the leader (weighted by what the attack really takes and
    /// how likely the victim is to answer it), defends itself, and answers decisions
    /// sensibly. Deterministic: equal scores resolve by enumeration order.
    /// </summary>
    public sealed partial class GreedyBot : IBot
    {
        public GameAction Choose(Game game, int playerId)
        {
            var moves = game.LegalMovesFor(playerId);
            if (moves.Count == 0)
            {
                throw new InvalidOperationException(
                    $"P{playerId} has no legal moves — engine deadlock. {GameRunner.Describe(game)}");
            }

            GameAction best = moves[0];
            double bestScore = double.MinValue;
            foreach (var move in moves)
            {
                double score = Score(game, playerId, move);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = move;
                }
            }
            return best;
        }

        private static double Score(Game game, int playerId, GameAction move)
        {
            var s = game.State;
            var me = s.Players[playerId];
            bool lastLap = ThreatAssessment.IsNearGameEnd(game);

            switch (move)
            {
                // ---- setup ----
                case ChooseLemonLords choose:
                    return choose.KeepTitleIds.Sum(id => LordAffinity(id));
                case InitialBuyStand buyStand:
                    // Classic first for the mid-range economy, then bargain for frequency —
                    // unless an unclaimed First Dibs stand title tips the scales.
                    return (buyStand.StandTypeId == "classic" ? 60
                        : buyStand.StandTypeId == "bargain" ? 55 : 50)
                        + StandTitleBonus(game, me, buyStand.StandTypeId);
                case InitialBuyEnd _:
                    return 5;

                // ---- purchases ----
                case BuyBraggingRights _:
                {
                    int price = game.Db.Supporting.BraggingRightsPrices[s.BraggingRightsSold];
                    if (me.InGameVictoryPoints >= 2)
                    {
                        return 500; // closes out the game trigger
                    }
                    // In the last lap cash has no future: a VP now beats any economy.
                    return lastLap || me.Money - price >= 5 ? 90 : 40;
                }
                case BuyStand buy:
                {
                    if (me.Stands.Count >= 5)
                    {
                        return 8;
                    }
                    double typeValue = buy.StandTypeId == "gourmet" ? 46
                        : buy.StandTypeId == "classic" ? 48 : 44;
                    return typeValue - me.Stands.Count * 4 + StandTitleBonus(game, me, buy.StandTypeId)
                        - (lastLap ? 10 : 0);
                }
                case BuyBlackMarket bm:
                {
                    var instance = s.BlackMarketInstances[s.Market[bm.MarketIndex]];
                    var def = game.Db.BlackMarket(instance.DefId);
                    double value = 30 + BmAffinity(game, me, def, bm) - def.Cost * 0.5
                        + LordEquipBonus(game, me, def, bm.TargetStandInstanceId)
                        + ShapeTitleBonus(game, me, instance.Shape);
                    // Replacing an existing card is usually a downgrade path.
                    if (bm.ReplaceInstanceId != null)
                    {
                        value -= 15;
                    }
                    return value;
                }
                case RefreshMarket _:
                    return 2;
                case DrawLemonCard _:
                    return ScoreDraw(game, me, lastLap);
                case EndTurn _:
                    return 1;

                // ---- lemon plays ----
                case PlayLemonCard play:
                    return ScorePlay(game, me, play);

                // ---- windows ----
                case PassWindow _:
                    return 10;
                case RespondToWindow respond:
                    return ScoreResponse(game, me, respond);
                case UseTurnAbility ability:
                    return ScoreAbility(game, me, ability);

                // ---- decisions ----
                case SubmitAbilityChoice choice:
                    return ScoreAbilityChoice(game, me, choice);
                case SubmitDiscard discard:
                    return 20 - discard.InstanceIds.Sum(id => CardValue(game, me, id));
                case SubmitTimeoutPayment _:
                    return 20;
                case SubmitRetarget retarget:
                    return ScoreRetarget(game, me, retarget);
                case SkipFreePlay _:
                    return 3;

                default:
                    return 0;
            }
        }

        private static double ScoreDraw(Game game, PlayerState me, bool lastLap)
        {
            double draw = me.Hand.Count < 7 ? 25 : 12;
            bool feedsHoarder = false;
            foreach (string lordId in UnmetLords(game, me))
            {
                if (lordId == "hoarder")
                {
                    draw = 32; // needs 10 cards in hand — keep drawing
                    feedsHoarder = true;
                }
                else if (lordId == "fuming-phil" || lordId == "pam-the-planner" ||
                         lordId == "meltdown-master")
                {
                    draw += 5; // fishing for the counted card type
                }
            }
            // A card drawn in the last lap rarely gets played before the end.
            return lastLap && !feedsHoarder ? draw - 10 : draw;
        }

        private static double ScorePlay(Game game, PlayerState me, PlayLemonCard play)
        {
            string defId = game.State.LemonInstances[play.CardInstanceId].DefId;
            var target = AttackVictim(game, play);
            double targetFactor = target == null ? 1 : AttackTargetFactor(game, me, target);
            switch (defId)
            {
                case "automation":
                    return me.Money >= 5 ? 42 : 15;
                case "market-forecasting":
                    return 30;
                case "night-shifts":
                    return 28;
                case "doorbuster-sale":
                {
                    var stand = me.Stands.First(st => st.InstanceId == play.TargetStandInstanceId);
                    return 20 + game.StandEarnings(me, stand) * 4;
                }
                case "hoa-violation":
                case "sharing-is-caring":
                case "taxes":
                    // All three cap at the victim's cash, so the nominal steal is a lie:
                    // score what would ACTUALLY change hands from THIS victim.
                    return target == null ? 0 : MoneyAttackScore(game, defId, target) * targetFactor;
                case "smear-campaign":
                    return target == null ? 0 : (20 + Math.Min(2, target.Hand.Count) * 6) * targetFactor;
                case "trash-pandas":
                    return target == null ? 0 : Math.Max(0, target.Hand.Count - me.Hand.Count) * 6 * targetFactor;
                case "steal-the-cashbox":
                {
                    // A trap on the victim's NEXT roll, not their wallet: what matters is
                    // how much their stands earn when they sell, capped at $10.
                    if (target == null)
                    {
                        return 0;
                    }
                    double perRoll = target.Stands.Sum(
                        st => game.StandEarnings(target, st) * game.SaleNumbersOf(st).Count) / 6.0;
                    return (18 + Math.Min(perRoll, 10) * 3) * targetFactor;
                }
                case "thats-not-fair":
                    return (22 + StolenValue(game, me, play.TargetEquippedInstanceId, play.EquipStandInstanceId))
                        * targetFactor;
                case "finders-keepers":
                    return (26 + StolenValue(game, me, play.TargetEquippedInstanceId, play.EquipStandInstanceId))
                        * targetFactor;
                case "connections":
                    return 44; // a free Black Market card
                case "reduce-and-reuse":
                    return 36;
                case "reverse-engineer":
                    // Recovering is worth what the recovered card is worth to us.
                    return play.DrawInstead || !(play.DiscardedLemonInstanceId is int recoveredId)
                        ? 24
                        : 22 + CardValue(game, me, recoveredId) * 2;
                case "rummage-sale":
                    return 6; // usually better to keep upgrades
                case "rebrand":
                {
                    var swapped = me.Stands.FirstOrDefault(st => st.InstanceId == play.TargetStandInstanceId);
                    if (swapped == null)
                    {
                        return 10;
                    }
                    // Only worth an action when it moves us TOWARD an unclaimed First Dibs
                    // collection — and it's a real setback when it walks a stand back out
                    // of one we're partway through. Overflow upgrades get discarded.
                    return 10 + RebrandDibsDelta(game, me, swapped.StandTypeId, play.NewStandTypeId)
                        - play.SelectedInstanceIds.Count * 5;
                }
                case "apologize":
                    // Expert Whiner WANTS to end the game as the Whiniest Baby.
                    if (me.LemonLordKept.Contains("expert-whiner"))
                    {
                        return 4;
                    }
                    return game.State.WhiniestBabyHolder == me.PlayerId ? 35 : 8;
                case "blame-changer":
                    if (me.LemonLordKept.Contains("expert-whiner"))
                    {
                        return 5;
                    }
                    return (game.State.WhiniestBabyHolder == me.PlayerId ? 40 : 12) * targetFactor;
                default:
                    return 10;
            }
        }

        /// <summary>
        /// The player an attack lands on. That's Not Fair! names only the card, so its
        /// victim is whoever has that card equipped.
        /// </summary>
        private static PlayerState? AttackVictim(Game game, PlayLemonCard play)
        {
            if (play.TargetPlayerId is int targetId)
            {
                return game.State.Players[targetId];
            }
            return play.TargetEquippedInstanceId is int equippedId
                ? OwnerOfEquipped(game, equippedId)
                : null;
        }

        private static PlayerState? OwnerOfEquipped(Game game, int equippedId) =>
            game.State.Players.FirstOrDefault(p =>
                p.Turf.Equipped.Contains(equippedId) ||
                p.Stands.Any(st => st.Equipped.Contains(equippedId)));

        /// <summary>
        /// How much an attack on this victim is worth relative to the same attack on the
        /// ideal victim, in (0,1]: the leader at full value down to 0.55 for the player
        /// furthest behind, then discounted by the odds a reaction card answers it —
        /// a fat hand makes an equal-threat victim a worse target than an empty one.
        /// </summary>
        private static double AttackTargetFactor(Game game, PlayerState me, PlayerState target) =>
            (0.55 + 0.45 * ThreatAssessment.ThreatOf(game, me.PlayerId, target))
            * (0.6 + 0.4 * ThreatAssessment.UnansweredChance(target));

        /// <summary>
        /// Dollars a money-stealing attack would ACTUALLY move: the card's nominal steal
        /// capped by the victim's cash, exactly the way <c>StealMoney</c> caps it (and a
        /// $0 transfer is a no-op). Null for cards that never touch a player's cash pile.
        /// </summary>
        public static int? MoneyRecovered(string defId, PlayerState victim)
        {
            switch (defId)
            {
                case "hoa-violation":
                    return Math.Min(5, victim.Money);
                case "taxes":
                    return Math.Min(Math.Min(2 * victim.Stands.Count, 10), victim.Money);
                case "sharing-is-caring":
                    return Math.Min((victim.Money + 1) / 2, 10);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Worth of a money attack given what it would actually recover: nothing at $0,
        /// barely worth the action off a near-broke victim, top-tier at the $10 cap.
        /// Cash in the hands of a player one VP from ending the game is the cash that
        /// buys the last VP, so denying it is worth extra.
        /// </summary>
        private static double MoneyAttackScore(Game game, string defId, PlayerState victim)
        {
            int recovered = MoneyRecovered(defId, victim) ?? 0;
            if (recovered <= 0)
            {
                return 0;
            }
            double denial = ThreatAssessment.IsAboutToTriggerEnd(game, victim) ? 8 : 0;
            return 6 + recovered * 3.8 + denial;
        }

        /// <summary>
        /// Net First Dibs stand-count progress of a Rebrand: what the destination type
        /// gains minus what the abandoned type loses.
        /// </summary>
        private static double RebrandDibsDelta(Game game, PlayerState me, string fromType, string toType)
        {
            int Owned(string type) => me.Stands.Count(st => st.StandTypeId == type);
            // Connoisseur wants 3 Gourmet AND 1 other: on an all-gourmet board the spare
            // gourmet is exactly the stand that should be converted.
            if (game.State.FirstDibsRow.Contains("connoisseur") && fromType == "gourmet" &&
                toType != "gourmet" && Owned("gourmet") >= 4 && me.Stands.Count == Owned("gourmet"))
            {
                return 12;
            }
            return DibsStandMarginal(game, toType, Owned(toType) + 1)
                - DibsStandMarginal(game, fromType, Owned(fromType));
        }

        /// <summary>How many stands of a type an unclaimed First Dibs title wants (0 = none on offer).</summary>
        private static int DibsStandGoal(Game game, string standTypeId)
        {
            var row = game.State.FirstDibsRow;
            if (standTypeId == "bargain" && row.Contains("penny-pincher")) return 5;
            if (standTypeId == "classic" && row.Contains("local-legend")) return 4;
            if (standTypeId == "gourmet" && row.Contains("connoisseur")) return 3;
            return 0;
        }

        /// <summary>
        /// What the <paramref name="nth"/> stand of a type is worth toward an unclaimed
        /// First Dibs stand title: growing, so a started collection gets finished, and
        /// zero once the title's count is already covered.
        /// </summary>
        private static double DibsStandMarginal(Game game, string standTypeId, int nth)
        {
            int goal = DibsStandGoal(game, standTypeId);
            return goal == 0 || nth < 1 || nth > goal ? 0 : 6 + (nth - 1) * 4;
        }

        /// <summary>
        /// How much the equipped card a steal would take is worth TO US — so the bot grabs
        /// the Diluted Lemonade, not whatever enumerates first.
        /// </summary>
        private static double StolenValue(Game game, PlayerState me, int? targetEquippedId, int? equipStandId)
        {
            if (!(targetEquippedId is int eq) ||
                !game.State.BlackMarketInstances.TryGetValue(eq, out var instance))
            {
                return 6;
            }
            var def = game.Db.BlackMarket(instance.DefId);
            double lordPull = LordEquipBonus(game, me, def, equipStandId) +
                              ShapeTitleBonus(game, me, instance.Shape);
            switch (def.Name)
            {
                case "Diluted Lemonade": return 12 + lordPull;   // flat +$2 on every sale
                case "Pushy Salesman": return 9 + lordPull;
                case "Spiked Lemonade":
                    return (def.Number is int pour && game.PourNumbersOf(me).Contains(pour) ? 3 : 9)
                        + lordPull;
                case "Pink Lemonade": return 7 + lordPull;
                default:
                    switch (def.Timing)
                    {
                        case EffectTiming.OnSale: return 8 + lordPull;
                        case EffectTiming.PowerPour: return 6 + lordPull;
                        case EffectTiming.OnYourTurn: return 5 + lordPull;
                        default: return (def.Category == "Defense" ? 6 : 4) + lordPull;
                    }
            }
        }

        private static double ScoreResponse(Game game, PlayerState me, RespondToWindow respond)
        {
            var s = game.State;
            if (respond.EquippedInstanceId != null)
            {
                // Inflatable Decoy: use it when the attack is aimed at us.
                var top = s.ResponseStack[s.ResponseStack.Count - 1];
                return top.AttackTargetId == me.PlayerId ? 70 : 4;
            }

            string defId = s.LemonInstances[respond.CardInstanceId].DefId;
            var stackTop = s.ResponseStack.Count > 0 ? s.ResponseStack[s.ResponseStack.Count - 1] : null;
            switch (defId)
            {
                case "tantrum":
                {
                    if (stackTop == null)
                    {
                        return 0;
                    }
                    // Friendly Fran (zero tantrums) is a whole VP: a spotless pile
                    // makes throwing one much less attractive.
                    double franBrake = me.TantrumPile.Count == 0 &&
                        me.LemonLordKept.Contains("friendly-fran") ? 30 : 0;
                    if (stackTop.AttackTargetId == me.PlayerId)
                    {
                        return 60 - franBrake; // cancel an attack on us
                    }
                    if (IsResponseToOwnPlay(s, stackTop, me))
                    {
                        // Counter-tantrum (55): revive the play a tantrum cancelled. A Tag
                        // or Rubber-Glue on our attack rates higher still: left alone it
                        // turns the attack around (onto us, for Rubber-Glue), and the bot
                        // would end up tantruming its own card one step later.
                        bool redirectsOurAttack = stackTop.LemonDefId == "tag-youre-it" ||
                                                  stackTop.LemonDefId == "im-rubber-youre-glue";
                        return (redirectsOurAttack ? 58 : 55) - franBrake;
                    }
                    // A player one VP from triggering the end: deny them anything big.
                    if (s.Players[stackTop.OwnerId].InGameVictoryPoints >=
                        game.Db.Config.VictoryPointsToTriggerEnd - 1)
                    {
                        if (stackTop.Kind == StackItemKind.BlackMarketPurchase)
                        {
                            var def = game.Db.BlackMarket(s.BlackMarketInstances[stackTop.BmInstanceId!.Value].DefId);
                            return (def.Cost >= 6 ? 30 : 12) - franBrake;
                        }
                        return 25 - franBrake;
                    }
                    // A play that doesn't touch us: burning a tantrum (and feeding the
                    // Whiniest Baby fine) on it is spite, not strategy — passing (10) wins.
                    // Expert Whiner is the exception: played tantrums ARE its VP condition.
                    return me.LemonLordKept.Contains("expert-whiner") ? 12 : 2 - franBrake;
                }
                case "tag-youre-it":
                {
                    if (stackTop?.AttackTargetId != me.PlayerId)
                    {
                        return 5;
                    }
                    // Pass the attack on to whoever is winning (2-player: no choice).
                    double redirectThreat = respond.RedirectTargetId is int newVictim
                        ? ThreatAssessment.ThreatOf(game, me.PlayerId, s.Players[newVictim])
                        : 0;
                    return 65 + 5 * redirectThreat;
                }
                case "im-rubber-youre-glue":
                    return stackTop?.AttackTargetId == me.PlayerId ? 68 : 4;
                case "out-of-stock":
                {
                    // Reroll when the current value earns us nothing.
                    int value = s.PendingRoll?.Value ?? 0;
                    bool earns = me.Stands.Any(st => game.SaleNumbersOf(st).Contains(value)) ||
                                 game.PourNumbersOf(me).Contains(value);
                    return earns ? 2 : 14;
                }
                case "profit-share":
                    return 80;
                default:
                    return 5;
            }
        }

        /// <summary>The stack top is a reaction (Tantrum / Tag / Rubber-Glue) aimed at a play of ours.</summary>
        private static bool IsResponseToOwnPlay(GameState s, StackItem stackTop, PlayerState me) =>
            stackTop.RespondingToItemId is int answeredId &&
            s.ResponseStack.Any(it => it.ItemId == answeredId && it.OwnerId == me.PlayerId);

        private static double ScoreAbilityChoice(Game game, PlayerState me, SubmitAbilityChoice choice)
        {
            var s = game.State;
            if (choice.TargetPlayerId is int victimId)
            {
                var victim = s.Players[victimId];
                return 20 + victim.Money * 0.6 + 10 * ThreatAssessment.ThreatOf(game, me.PlayerId, victim);
            }
            if (choice.CardInstanceIds.Count > 0)
            {
                double value = choice.CardInstanceIds.Sum(id => CardValue(game, me, id));
                // Picking from a revealed hand takes the best card; a give-back or a
                // forced discard sheds the worst one.
                bool picksFromVictim = FirstAbilityDecision(s, me)?.Kind == DecisionKind.AbilityPickCard;
                return picksFromVictim ? 20 + value : 20 - value;
            }
            if (choice.EquippedInstanceId is int copiedId)
            {
                // Innovation copies a Power Pour: the pricier card has the bigger effect.
                return 20 + game.Db.BlackMarket(s.BlackMarketInstances[copiedId].DefId).Cost;
            }
            if (choice.StandInstanceId is int soldStandId)
            {
                var stand = me.Stands.First(st => st.InstanceId == soldStandId);
                return 20 + game.StandEarnings(me, stand);
            }
            return 20;
        }

        /// <summary>
        /// The decision a SubmitAbilityChoice answers: the engine takes the player's first
        /// pending decision of the ability group, and legal moves are enumerated for it.
        /// </summary>
        private static PendingDecision? FirstAbilityDecision(GameState s, PlayerState me) =>
            s.PendingDecisions.FirstOrDefault(d => d.PlayerId == me.PlayerId && IsAbilityDecision(d.Kind));

        private static bool IsAbilityDecision(DecisionKind kind) =>
            kind == DecisionKind.AbilityVictim || kind == DecisionKind.AbilityPickCard ||
            kind == DecisionKind.AbilityGiveBack || kind == DecisionKind.AbilityDiscard ||
            kind == DecisionKind.InnovationCopy || kind == DecisionKind.WordOfMouthStand;

        /// <summary>
        /// Re-picking the loot after a Tag moved our attack: Finders Keepers takes what
        /// helps US most, That's Not Fair! trashes the victim's priciest card.
        /// </summary>
        private static double ScoreRetarget(Game game, PlayerState me, SubmitRetarget retarget)
        {
            var s = game.State;
            var attack = s.ResponseStack.FirstOrDefault(it => it.ItemId == retarget.StackItemId);
            if (attack == null || !(retarget.TargetEquippedInstanceId is int lootId))
            {
                return 20;
            }
            if (attack.LemonDefId == "finders-keepers")
            {
                return 20 + StolenValue(game, me, lootId, retarget.EquipStandInstanceId);
            }
            return 20 + game.Db.BlackMarket(s.BlackMarketInstances[lootId].DefId).Cost;
        }

        private static double ScoreAbility(Game game, PlayerState me, UseTurnAbility ability)
        {
            var s = game.State;
            string defId = game.Db.BlackMarket(s.BlackMarketInstances[ability.EquippedInstanceId].DefId).Id;
            if (defId == "liquid-energy")
            {
                return 26;
            }
            if (s.PendingRoll == null)
            {
                return 0;
            }

            int Earn(int value) =>
                me.Stands.Sum(st => game.SaleNumbersOf(st).Contains(value) ? game.StandEarnings(me, st) : 0) +
                (game.PourNumbersOf(me).Contains(value) ? 1 : 0);

            int current = Earn(s.PendingRoll.Value);
            switch (defId)
            {
                case "downsell":
                    return Earn(Math.Max(1, s.PendingRoll.Value - 1)) > current ? 30 : 1;
                case "sugared-up":
                    return Earn(Math.Min(6, s.PendingRoll.Value + 1)) > current ? 30 : 1;
                case "take-two":
                    return current == 0 ? 22 : 1;
                default:
                    return 1;
            }
        }

        /// <summary>Rough desirability of a Black Market card beyond its sticker price.</summary>
        private static double BmAffinity(Game game, PlayerState me, BlackMarketCardDef def, BuyBlackMarket bm)
        {
            switch (def.Name)
            {
                case "Pushy Salesman":
                {
                    // A sale number the target stand already sells on earns nothing.
                    var stand = me.Stands.FirstOrDefault(st => st.InstanceId == bm.TargetStandInstanceId);
                    if (def.Number is int sale && stand != null &&
                        game.SaleNumbersOf(stand).Contains(sale))
                    {
                        return -25;
                    }
                    return 8;   // more sale numbers = more income
                }
                case "Spiked Lemonade":
                {
                    // A pour number we already cover adds nothing — unless we secretly
                    // hold Pour Master (end with 2+ Spiked Lemonades) and collect anyway.
                    if (def.Number is int pour && game.PourNumbersOf(me).Contains(pour))
                    {
                        return me.LemonLordKept.Contains("pour-master") ? 5 : -25;
                    }
                    return 7;
                }
                default:
                    switch (def.Timing)
                    {
                        case EffectTiming.OnSale: return 6;
                        case EffectTiming.PowerPour: return 4;
                        case EffectTiming.OnYourTurn: return 3;
                        default: return def.Category == "Defense" ? 5 : 2;
                    }
            }
        }

        /// <summary>How much we like keeping a card (higher = keep; discard/give low values).</summary>
        private static double CardValue(Game game, PlayerState me, int instanceId)
        {
            var def = game.Db.Lemon(game.State.LemonInstances[instanceId].DefId);
            double value;
            switch (def.Type)
            {
                case LemonCardType.Instant:
                    value = def.Id == "tantrum" ? 8 : 6;
                    break;
                case LemonCardType.Attack:
                    value = 5;
                    break;
                default:
                    value = 4;
                    break;
            }
            // Cards that count toward an unmet hand-composition lord are keepers.
            foreach (string lordId in UnmetLords(game, me))
            {
                if (lordId == "fuming-phil" && def.Type == LemonCardType.Attack)
                {
                    value += 5;
                }
                else if (lordId == "pam-the-planner" && def.Type == LemonCardType.Plan)
                {
                    value += 5;
                }
                else if (lordId == "meltdown-master" && def.Id == "tantrum")
                {
                    value += 6;
                }
                else if (lordId == "hoarder")
                {
                    value += 2;
                }
            }
            return value;
        }
    }
}
