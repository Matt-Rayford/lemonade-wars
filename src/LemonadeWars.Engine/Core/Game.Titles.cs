using System;
using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Victory points beyond Bragging Rights. First Dibs titles are claimed automatically
    /// the instant their condition holds (checked after every action settles); Lemon Lord
    /// titles are secret and scored once at game end. Every title is worth 1 VP, and
    /// every VP gain re-checks who holds Spoiled Rotten.
    /// </summary>
    public sealed partial class Game
    {
        // ------------------------------------------------------ first dibs

        /// <summary>Award every face-up First Dibs title whose condition a player now meets.</summary>
        private void CheckFirstDibs(List<GameEvent> events)
        {
            int playerCount = State.Players.Count;
            foreach (string titleId in State.FirstDibsRow.ToList())
            {
                // Turn order from the active player breaks simultaneous claims.
                for (int offset = 0; offset < playerCount; offset++)
                {
                    var player = State.Players[(State.ActivePlayer + offset) % playerCount];
                    if (!MeetsFirstDibs(player, titleId))
                    {
                        continue;
                    }
                    State.FirstDibsRow.Remove(titleId);
                    player.FirstDibsClaimed.Add(titleId);
                    events.Add(new TitleClaimed { PlayerId = player.PlayerId, TitleId = titleId });
                    AssignSpoiledRotten(events);
                    break;
                }
            }
        }

        private bool MeetsFirstDibs(PlayerState player, string titleId)
        {
            var rollStats = State.RollStats.TryGetValue(player.PlayerId, out var tracked) ? tracked : null;
            switch (titleId)
            {
                case "balanced-set":
                {
                    var shapes = ShapesOf(player);
                    return shapes.Count(shape => shape == Shape.Circle) >= 2 &&
                           shapes.Count(shape => shape == Shape.Diamond) >= 2 &&
                           shapes.Count(shape => shape == Shape.Square) >= 2;
                }
                case "block-builder":
                    return ShapesOf(player).Count(shape => shape == Shape.Square) >= 6;
                case "diamond-dealer":
                    return ShapesOf(player).Count(shape => shape == Shape.Diamond) >= 6;
                case "full-circle":
                    return ShapesOf(player).Count(shape => shape == Shape.Circle) >= 6;

                case "big-earner":
                    return rollStats != null && rollStats.Earned >= 9;
                case "clean-getaway":
                    return rollStats != null && rollStats.MoneyStolen >= 3;
                case "sticky-fingers":
                    return rollStats != null && rollStats.CardsStolen >= 2;
                case "double-scoop":
                    return rollStats != null && rollStats.CardsKept >= 2;

                case "connoisseur":
                    return CountStands(player, "gourmet") >= 3 &&
                           player.Stands.Count(stand => stand.StandTypeId != "gourmet") >= 1;
                case "local-legend":
                    return CountStands(player, "classic") >= 4;
                case "penny-pincher":
                    return CountStands(player, "bargain") >= 5;

                case "cranky-pants":
                    return player.TantrumPile.Count >= 3;
                case "expander":
                    return player.Turf.Equipped.Count >= 5;
                case "innovator":
                    return player.Stands.Sum(stand => stand.Equipped.Count) >= 5;
                case "shopaholic":
                    return player.PlayerId == State.ActivePlayer && State.SpentThisTurn >= 12;
                case "stand-surge":
                    return player.Stands.Any(stand =>
                        StandEarnings(player, stand) - Db.StandType(stand.StandTypeId).BaseEarnings >= 3);

                default:
                    return false;
            }
        }

        /// <summary>All shape icons a player controls: their stands plus every equipped Black Market copy.</summary>
        private List<Shape> ShapesOf(PlayerState player)
        {
            var shapes = player.Stands.Select(stand => stand.Shape).ToList();
            shapes.AddRange(player.Turf.Equipped
                .Concat(player.Stands.SelectMany(stand => stand.Equipped))
                .Select(id => State.BlackMarketInstances[id].Shape));
            return shapes;
        }

        private int CountStands(PlayerState player, string typeId) =>
            player.Stands.Count(stand => stand.StandTypeId == typeId);

        // ------------------------------------------------------ lemon lord

        /// <summary>Is this end-game condition satisfied right now?</summary>
        public bool MeetsLemonLord(PlayerState player, string titleId)
        {
            switch (titleId)
            {
                case "bare-bones":
                    return player.Stands.Count(stand => stand.Equipped.Count == 0) >= 3;
                case "budget-beast":
                    return player.Stands.Any(stand =>
                        stand.StandTypeId == "bargain" && StandEarnings(player, stand) >= 3);
                case "elite-squeezer":
                    return player.Stands.Any(stand => stand.StandTypeId == "gourmet" && stand.Equipped.Count >= 4);
                case "expert-whiner":
                    return State.WhiniestBabyHolder == player.PlayerId;
                case "friendly-fran":
                    return player.TantrumPile.Count == 0;

                case "fuming-phil":
                    return CountHand(player, LemonCardType.Attack) >= 5;
                case "pam-the-planner":
                    return CountHand(player, LemonCardType.Plan) >= 5;
                case "hoarder":
                    return player.Hand.Count >= 10;
                case "meltdown-master":
                    return player.Hand.Count(id => State.LemonInstances[id].DefId == "tantrum") >= 3;

                case "cash-queen":
                    return EquippedDefs(player).Count(def => def.Cost >= 6) >= 3;
                case "pour-master":
                    return EquippedDefs(player).Count(def => def.Name == "Spiked Lemonade") >= 2;
                case "pushover":
                    return EquippedDefs(player).Count(def => def.Name == "Pushy Salesman") >= 4;

                // Icon-count conditions, validated against the deck by the data pipeline.
                case "card-shark":
                    return EquippedDefs(player).Count(def => def.Icons.Contains("draw")) >= 2;
                case "flow-master":
                    return EquippedDefs(player).Count(def => def.Timing == EffectTiming.PowerPour) >= 4;
                case "no-bullies-allowed":
                    return EquippedDefs(player).Count(def => def.Category == "Defense") >= 2;
                case "profit-pusher":
                    return EquippedDefs(player).Count(def => def.Icons.Contains("dollar")) >= 3;
                case "roll-wrangler":
                    return EquippedDefs(player).Count(def => def.Category == "Roll Modification") >= 3;
                case "sales-engine":
                    return EquippedDefs(player).Count(def => def.Timing == EffectTiming.OnSale) >= 3;
                case "swindling-sammy":
                    return EquippedDefs(player).Count(def => def.Icons.Contains("steal-card")) >= 2;
                case "thieving-tommy":
                    return EquippedDefs(player).Count(def => def.Icons.Contains("steal-dollar")) >= 2;

                default:
                    return false;
            }
        }

        private int CountHand(PlayerState player, LemonCardType type) =>
            player.Hand.Count(id => Db.Lemon(State.LemonInstances[id].DefId).Type == type);

        private IEnumerable<BlackMarketCardDef> EquippedDefs(PlayerState player) =>
            player.Turf.Equipped
                .Concat(player.Stands.SelectMany(stand => stand.Equipped))
                .Select(EquippedDef);

        // -------------------------------------------------- spoiled rotten

        /// <summary>Each VP gain re-checks Spoiled Rotten: sole last place holds it (rulebook p12).</summary>
        private void AssignSpoiledRotten(List<GameEvent> events)
        {
            int fewestPoints = State.Players.Min(p => p.InGameVictoryPoints);
            var lastPlace = State.Players.Where(p => p.InGameVictoryPoints == fewestPoints).ToList();
            int? newHolder = lastPlace.Count == 1 ? lastPlace[0].PlayerId : (int?)null;
            if (newHolder != State.SpoiledRottenHolder)
            {
                events.Add(new SpoiledRottenMoved
                {
                    FromPlayerId = State.SpoiledRottenHolder,
                    ToPlayerId = newHolder,
                });
                State.SpoiledRottenHolder = newHolder;
            }
        }

        // ---------------------------------------------------- final scoring

        /// <summary>Final scoring: in-game VP plus each kept Lemon Lord title that holds (rulebook p2/p13).</summary>
        private void FinishGameWithScores(List<GameEvent> events)
        {
            State.Stage = GameStage.Finished;

            var scores = new Dictionary<int, int>();
            foreach (var player in State.Players)
            {
                scores[player.PlayerId] = player.InGameVictoryPoints + ScoreLemonLords(player, events);
            }

            int bestScore = scores.Values.Max();
            var contenders = State.Players.Where(p => scores[p.PlayerId] == bestScore).ToList();
            State.Winners.AddRange(BreakTies(contenders).Select(p => p.PlayerId));

            events.Add(new StageChanged { Stage = State.Stage });
            events.Add(new GameEnded { Winners = State.Winners.ToList(), Scores = scores });
        }

        private int ScoreLemonLords(PlayerState player, List<GameEvent> events)
        {
            int points = 0;
            foreach (string titleId in player.LemonLordKept)
            {
                if (MeetsLemonLord(player, titleId))
                {
                    points += Db.Title(titleId).VictoryPoints;
                    events.Add(new LemonLordMet { PlayerId = player.PlayerId, TitleId = titleId });
                }
            }
            return points;
        }

        /// <summary>
        /// Rulebook p14: on equal victory points the most money wins, then the fewest played
        /// tantrums. A tie surviving both is settled away from the table with rock paper scissors,
        /// so the engine reports those players as joint winners rather than inventing a result.
        /// </summary>
        private static List<PlayerState> BreakTies(List<PlayerState> contenders) =>
            KeepBest(KeepBest(contenders, p => p.Money), p => -p.TantrumPile.Count);

        private static List<PlayerState> KeepBest(List<PlayerState> players, Func<PlayerState, int> key)
        {
            int best = players.Max(key);
            return players.Where(p => key(p) == best).ToList();
        }
    }
}
