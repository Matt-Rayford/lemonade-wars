using System;
using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>A computer opponent: given the game and its seat, pick one legal action.</summary>
    public interface IBot
    {
        GameAction Choose(Game game, int playerId);
    }

    /// <summary>
    /// Uniform-random legal play. Terrible opponent, perfect fuzzer: it walks every corner
    /// of the rules engine and proves no reachable state deadlocks or throws.
    /// </summary>
    public sealed class RandomBot : IBot
    {
        private readonly DeterministicRng _rng;

        public RandomBot(ulong seed)
        {
            _rng = new DeterministicRng(seed);
        }

        public GameAction Choose(Game game, int playerId)
        {
            var moves = game.LegalMovesFor(playerId);
            if (moves.Count == 0)
            {
                throw new InvalidOperationException(
                    $"P{playerId} has no legal moves — engine deadlock. {GameRunner.Describe(game)}");
            }
            return moves[_rng.Next(moves.Count)];
        }
    }

    /// <summary>
    /// First real opponent: one-ply heuristic scoring, no lookahead. Buys VP when it can,
    /// builds economy, attacks the richest player, defends itself, and answers decisions
    /// sensibly. Deterministic: equal scores resolve by enumeration order.
    /// </summary>
    public sealed class GreedyBot : IBot
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

        // (deadlock diagnostics shared with RandomBot live in GameRunner.Describe)

        private static double Score(Game game, int playerId, GameAction move)
        {
            var s = game.State;
            var me = s.Players[playerId];
            var richest = s.Players.Where(p => p.PlayerId != playerId)
                .OrderByDescending(p => p.Money).First();

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
                    return me.Money - price >= 5 ? 90 : 40;
                }
                case BuyStand buy:
                {
                    if (me.Stands.Count >= 5)
                    {
                        return 8;
                    }
                    double typeValue = buy.StandTypeId == "gourmet" ? 46
                        : buy.StandTypeId == "classic" ? 48 : 44;
                    return typeValue - me.Stands.Count * 4 + StandTitleBonus(game, me, buy.StandTypeId);
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
                {
                    double draw = me.Hand.Count < 7 ? 25 : 12;
                    foreach (string lordId in UnmetLords(game, me))
                    {
                        if (lordId == "hoarder")
                        {
                            draw = 32; // needs 10 cards in hand — keep drawing
                        }
                        else if (lordId == "fuming-phil" || lordId == "pam-the-planner" ||
                                 lordId == "meltdown-master")
                        {
                            draw += 5; // fishing for the counted card type
                        }
                    }
                    return draw;
                }
                case EndTurn _:
                    return 1;

                // ---- lemon plays ----
                case PlayLemonCard play:
                    return ScorePlay(game, me, richest, play);

                // ---- windows ----
                case PassWindow _:
                    return 10;
                case RespondToWindow respond:
                    return ScoreResponse(game, me, respond);
                case UseTurnAbility ability:
                    return ScoreAbility(game, me, ability);

                // ---- decisions ----
                case SubmitAbilityChoice choice:
                    if (choice.TargetPlayerId is int victim)
                    {
                        return 20 + s.Players[victim].Money; // rob the richest
                    }
                    if (choice.CardInstanceIds.Count > 0)
                    {
                        // Give away / discard the least valuable card.
                        return 20 - choice.CardInstanceIds.Sum(id => CardValue(game, me, id));
                    }
                    return 20;
                case SubmitDiscard discard:
                    return 20 - discard.InstanceIds.Sum(id => CardValue(game, me, id));
                case SubmitTimeoutPayment _:
                    return 20;
                case SubmitRetarget _:
                    return 20;
                case SkipFreePlay _:
                    return 3;

                default:
                    return 0;
            }
        }

        private static double ScorePlay(Game game, PlayerState me, PlayerState richest, PlayLemonCard play)
        {
            string defId = game.State.LemonInstances[play.CardInstanceId].DefId;
            var target = play.TargetPlayerId is int t ? game.State.Players[t] : null;
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
                    return target == null ? 0 : 20 + Math.Min(5, target.Money) * 4;
                case "sharing-is-caring":
                    return target == null ? 0 : 15 + Math.Min((target.Money + 1) / 2, 10) * 3;
                case "taxes":
                    return target == null ? 0 : 15 + Math.Min(2 * target.Stands.Count, 10) * 3;
                case "smear-campaign":
                    return target == null ? 0 : 20 + Math.Min(2, target.Hand.Count) * 6;
                case "trash-pandas":
                    return target == null ? 0 : (target.Hand.Count - me.Hand.Count) * 6;
                case "steal-the-cashbox":
                    return target == null ? 0 : 18 + target.Stands.Count * 3;
                case "thats-not-fair":
                    return 22 + StolenValue(game, me, play);
                case "finders-keepers":
                    return 26 + StolenValue(game, me, play);
                case "connections":
                    return 44; // a free Black Market card
                case "reduce-and-reuse":
                    return 36;
                case "reverse-engineer":
                    return play.DrawInstead ? 24 : 32;
                case "rummage-sale":
                    return 6; // usually better to keep upgrades
                case "rebrand":
                    return 10;
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
                    return game.State.WhiniestBabyHolder == me.PlayerId ? 40 : 12;
                default:
                    return 10;
            }
        }

        /// <summary>
        /// How much the equipped card a steal would take is worth TO US — so the bot grabs
        /// the Diluted Lemonade, not whatever enumerates first.
        /// </summary>
        private static double StolenValue(Game game, PlayerState me, PlayLemonCard play)
        {
            if (!(play.TargetEquippedInstanceId is int eq) ||
                !game.State.BlackMarketInstances.TryGetValue(eq, out var instance))
            {
                return 6;
            }
            var def = game.Db.BlackMarket(instance.DefId);
            double lordPull = LordEquipBonus(game, me, def, play.EquipStandInstanceId) +
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
                    if (stackTop.Kind == StackItemKind.BlackMarketPurchase)
                    {
                        var def = game.Db.BlackMarket(s.BlackMarketInstances[stackTop.BmInstanceId!.Value].DefId);
                        return (def.Cost >= 6 ? 30 : 6) - franBrake;
                    }
                    return 6 - franBrake;
                }
                case "tag-youre-it":
                    return stackTop?.AttackTargetId == me.PlayerId ? 65 : 5;
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

        /// <summary>
        /// Pull toward the stand type an unclaimed First Dibs title rewards (penny-pincher:
        /// 5 bargain, local-legend: 4 classic, connoisseur: 3 gourmet + 1 other). Grows with
        /// each matching stand owned, so a bot that starts a collection commits to it.
        /// </summary>
        private static double StandTitleBonus(Game game, PlayerState me, string standTypeId)
        {
            var row = game.State.FirstDibsRow;
            int Owned(string type) => me.Stands.Count(st => st.StandTypeId == type);
            if (standTypeId == "bargain" && row.Contains("penny-pincher") && Owned("bargain") < 5)
            {
                return 6 + Owned("bargain") * 4;
            }
            if (standTypeId == "classic" && row.Contains("local-legend") && Owned("classic") < 4)
            {
                return 6 + Owned("classic") * 4;
            }
            if (row.Contains("connoisseur"))
            {
                if (standTypeId == "gourmet" && Owned("gourmet") < 3)
                {
                    return 6 + Owned("gourmet") * 4;
                }
                // Three gourmets down, all-gourmet board: the title's "1 other Stand".
                if (standTypeId != "gourmet" && Owned("gourmet") >= 3 && me.Stands.Count == Owned("gourmet"))
                {
                    return 12;
                }
            }
            // Kept lords with stand-flavored conditions pull the stand mix too.
            double lordBonus = 0;
            foreach (string lordId in UnmetLords(game, me))
            {
                if (lordId == "bare-bones")
                {
                    lordBonus += 4; // more stands = easier to keep 3 of them empty
                }
                else if (lordId == "budget-beast" && standTypeId == "bargain")
                {
                    lordBonus += 4;
                }
                else if (lordId == "elite-squeezer" && standTypeId == "gourmet" &&
                         !me.Stands.Any(st => st.StandTypeId == "gourmet"))
                {
                    lordBonus += 5;
                }
            }
            // The next stand comes off the top of its supply pile: its shape may feed
            // an unclaimed First Dibs shape title.
            if (game.State.StandSupply.TryGetValue(standTypeId, out var pile) && pile.Count > 0)
            {
                lordBonus += ShapeTitleBonus(game, me, pile[0]);
            }
            return lordBonus;
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

        // -------------------------------------------- Lemon Lord awareness

        private static IEnumerable<string> UnmetLords(Game game, PlayerState me) =>
            me.LemonLordKept.Where(id => !game.MeetsLemonLord(me, id));

        /// <summary>Does equipping this card advance the counted condition of a lord?</summary>
        private static bool LordWantsEquip(string lordId, BlackMarketCardDef def)
        {
            switch (lordId)
            {
                case "pour-master": return def.Name == "Spiked Lemonade";
                case "pushover": return def.Name == "Pushy Salesman";
                case "cash-queen": return def.Cost >= 6;
                case "card-shark": return def.Icons.Contains("draw");
                case "flow-master": return def.Timing == EffectTiming.PowerPour;
                case "no-bullies-allowed": return def.Category == "Defense";
                case "profit-pusher": return def.Icons.Contains("dollar");
                case "roll-wrangler": return def.Category == "Roll Modification";
                case "sales-engine": return def.Timing == EffectTiming.OnSale;
                case "swindling-sammy": return def.Icons.Contains("steal-card");
                case "thieving-tommy": return def.Icons.Contains("steal-dollar");
                default: return false;
            }
        }

        /// <summary>
        /// A kept lord is a full VP at game end: acquiring an equip that advances one
        /// (buy, steal, Connections take) earns a strong pull, destination-aware for
        /// the stand-specific lords, with a brake for Bare Bones boards.
        /// </summary>
        private static double LordEquipBonus(Game game, PlayerState me, BlackMarketCardDef def,
            int? destinationStandId)
        {
            double bonus = 0;
            foreach (string lordId in UnmetLords(game, me))
            {
                if (LordWantsEquip(lordId, def))
                {
                    bonus += 9;
                }
                switch (lordId)
                {
                    case "elite-squeezer":
                        if (destinationStandId is int gourmetDest && me.Stands.Any(st =>
                                st.InstanceId == gourmetDest && st.StandTypeId == "gourmet"))
                        {
                            bonus += 6; // 4 equips on one gourmet stand
                        }
                        break;
                    case "budget-beast":
                        if ((def.Id == "diluted-lemonade" || def.Id == "pink-lemonade") &&
                            destinationStandId is int bargainDest && me.Stands.Any(st =>
                                st.InstanceId == bargainDest && st.StandTypeId == "bargain"))
                        {
                            bonus += 8; // a bargain stand earning $3+
                        }
                        break;
                    case "bare-bones":
                        if (def.Target == EquipTarget.Stand &&
                            me.Stands.Count(st => st.Equipped.Count == 0) <= 3)
                        {
                            bonus -= 7; // equipping would spend a needed empty stand
                        }
                        break;
                }
            }
            return bonus;
        }

        /// <summary>
        /// First Dibs shape titles (6 squares/diamonds/circles, 2 of each): once a
        /// collection is started, matching shapes get a growing pull.
        /// </summary>
        private static double ShapeTitleBonus(Game game, PlayerState me, Shape? shape)
        {
            if (!(shape is Shape wanted))
            {
                return 0;
            }
            var row = game.State.FirstDibsRow;
            int owned = ShapeCount(game, me, wanted);
            double bonus = 0;
            string title = wanted == Shape.Square ? "block-builder"
                : wanted == Shape.Diamond ? "diamond-dealer" : "full-circle";
            if (row.Contains(title) && owned >= 2 && owned < 6)
            {
                bonus += 1.5 + owned;
            }
            if (row.Contains("balanced-set") && owned < 2)
            {
                bonus += 3;
            }
            return bonus;
        }

        private static int ShapeCount(Game game, PlayerState me, Shape shape)
        {
            int count = me.Stands.Count(st => st.Shape == shape);
            foreach (int id in me.Turf.Equipped)
            {
                if (game.State.BlackMarketInstances[id].Shape == shape)
                {
                    count++;
                }
            }
            foreach (var stand in me.Stands)
            {
                foreach (int id in stand.Equipped)
                {
                    if (game.State.BlackMarketInstances[id].Shape == shape)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        /// <summary>Static preference between Lemon Lord titles at the keep-2 choice.</summary>
        private static double LordAffinity(string titleId)
        {
            switch (titleId)
            {
                case "friendly-fran": return 9;  // passive: just avoid tantrums
                case "hoarder": return 8;        // drawing is always available
                case "bare-bones": return 7;
                case "expert-whiner": return 3;  // conflicts with friendly play
                case "elite-squeezer": return 4;
                default: return 5;
            }
        }
    }

    /// <summary>Drives a game to completion with a bot in every seat.</summary>
    public static class GameRunner
    {
        /// <summary>One-line state summary for deadlock/timeout diagnostics.</summary>
        public static string Describe(Game game)
        {
            var s = game.State;
            string decisions = string.Join(",", s.PendingDecisions.Select(d => $"P{d.PlayerId}:{d.Kind}"));
            string money = string.Join(",", s.Players.Select(p => $"P{p.PlayerId}=${p.Money}"));
            return $"[stage={s.Stage} phase={s.Phase} active={s.ActivePlayer} " +
                   $"decisions=({decisions}) awaiting=({string.Join(",", s.AwaitingResponse)}) " +
                   $"stack={s.ResponseStack.Count} roll={s.PendingRoll?.Value.ToString() ?? "-"} " +
                   $"buyQueue=({string.Join(",", s.InitialBuyQueue)}) standDone={s.InitialBuyStandDone} {money}]";
        }

        /// <summary>Play until Finished. Returns the number of actions taken. Throws on deadlock.</summary>
        public static int PlayOut(Game game, IReadOnlyDictionary<int, IBot> bots, int maxActions = 20000)
        {
            int actions = 0;
            while (game.State.Stage != GameStage.Finished)
            {
                if (actions++ > maxActions)
                {
                    throw new InvalidOperationException(
                        $"Game exceeded {maxActions} actions — likely an infinite loop.");
                }
                var acting = game.ActingPlayers();
                if (acting.Count == 0)
                {
                    throw new InvalidOperationException("No acting players but the game is not finished.");
                }
                int playerId = acting[0];
                var action = bots[playerId].Choose(game, playerId);
                game.Apply(action);
            }
            return actions;
        }
    }
}
