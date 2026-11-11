using System;
using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Victory points beyond Bragging Rights. First Dibs titles are claimed the instant their
    /// condition holds (checked after every action settles); Lemon Lord titles are secret and
    /// scored once at game end. Every title is worth 1 VP.
    /// </summary>
    public sealed partial class Game
    {
        // ------------------------------------------------------ first dibs

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
                case FirstDibsIds.BalancedSet:
                {
                    var shapes = ShapesOf(player);
                    return shapes.Count(shape => shape == Shape.Circle) >= 2 &&
                           shapes.Count(shape => shape == Shape.Diamond) >= 2 &&
                           shapes.Count(shape => shape == Shape.Square) >= 2;
                }
                case FirstDibsIds.BlockBuilder:
                    return ShapesOf(player).Count(shape => shape == Shape.Square) >= 6;
                case FirstDibsIds.DiamondDealer:
                    return ShapesOf(player).Count(shape => shape == Shape.Diamond) >= 6;
                case FirstDibsIds.FullCircle:
                    return ShapesOf(player).Count(shape => shape == Shape.Circle) >= 6;

                case FirstDibsIds.BigEarner:
                    return rollStats != null && rollStats.Earned >= 9;
                case FirstDibsIds.CleanGetaway:
                    return rollStats != null && rollStats.MoneyStolen >= 3;
                case FirstDibsIds.StickyFingers:
                    return rollStats != null && rollStats.CardsStolen >= 2;
                case FirstDibsIds.DoubleScoop:
                    return rollStats != null && rollStats.CardsKept >= 2;

                case FirstDibsIds.Connoisseur:
                    return CountStands(player, StandTypeIds.Gourmet) >= 3 &&
                           player.Stands.Count(stand => stand.StandTypeId != StandTypeIds.Gourmet) >= 1;
                case FirstDibsIds.LocalLegend:
                    return CountStands(player, StandTypeIds.Classic) >= 4;
                case FirstDibsIds.PennyPincher:
                    return CountStands(player, StandTypeIds.Bargain) >= 5;

                case FirstDibsIds.CrankyPants:
                    return player.TantrumPile.Count >= 3;
                case FirstDibsIds.Expander:
                    return player.Turf.Equipped.Count >= 5;
                case FirstDibsIds.Innovator:
                    return player.Stands.Sum(stand => stand.Equipped.Count) >= 5;
                case FirstDibsIds.Shopaholic:
                    return player.PlayerId == State.ActivePlayer && State.SpentThisTurn >= 12;
                case FirstDibsIds.StandSurge:
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

        public bool MeetsLemonLord(PlayerState player, string titleId)
        {
            switch (titleId)
            {
                case LemonLordIds.BareBones:
                    return player.Stands.Count(stand => stand.Equipped.Count == 0) >= 3;
                case LemonLordIds.BudgetBeast:
                    return player.Stands.Any(stand =>
                        stand.StandTypeId == StandTypeIds.Bargain && StandEarnings(player, stand) >= 3);
                case LemonLordIds.EliteSqueezer:
                    return player.Stands.Any(stand => stand.StandTypeId == StandTypeIds.Gourmet && stand.Equipped.Count >= 4);
                case LemonLordIds.ExpertWhiner:
                    return State.WhiniestBabyHolder == player.PlayerId;
                case LemonLordIds.FriendlyFran:
                    return player.TantrumPile.Count == 0;

                case LemonLordIds.FumingPhil:
                    return CountHand(player, LemonCardType.Attack) >= 5;
                case LemonLordIds.PamThePlanner:
                    return CountHand(player, LemonCardType.Plan) >= 5;
                case LemonLordIds.Hoarder:
                    return player.Hand.Count >= 10;
                case LemonLordIds.MeltdownMaster:
                    return player.Hand.Count(id => State.LemonInstances[id].DefId == LemonCardIds.Tantrum) >= 3;

                case LemonLordIds.CashQueen:
                    return EquippedDefs(player).Count(def => def.Cost >= 6) >= 3;
                case LemonLordIds.PourMaster:
                    return EquippedDefs(player).Count(def => def.Name == "Spiked Lemonade") >= 2;
                case LemonLordIds.Pushover:
                    return EquippedDefs(player).Count(def => def.Name == "Pushy Salesman") >= 4;

                // Icon-count conditions, validated against the deck by the data pipeline.
                case LemonLordIds.CardShark:
                    return EquippedDefs(player).Count(def => def.Icons.Contains("draw")) >= 2;
                case LemonLordIds.FlowMaster:
                    return EquippedDefs(player).Count(def => def.Timing == EffectTiming.PowerPour) >= 4;
                case LemonLordIds.NoBulliesAllowed:
                    return EquippedDefs(player).Count(def => def.Category == "Defense") >= 2;
                case LemonLordIds.ProfitPusher:
                    return EquippedDefs(player).Count(def => def.Icons.Contains("dollar")) >= 3;
                case LemonLordIds.RollWrangler:
                    return EquippedDefs(player).Count(def => def.Category == "Roll Modification") >= 3;
                case LemonLordIds.SalesEngine:
                    return EquippedDefs(player).Count(def => def.Timing == EffectTiming.OnSale) >= 3;
                case LemonLordIds.SwindlingSammy:
                    return EquippedDefs(player).Count(def => def.Icons.Contains("steal-card")) >= 2;
                case LemonLordIds.ThievingTommy:
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
        /// Rulebook p14: most money wins a VP tie, then fewest played tantrums. A tie surviving both
        /// is settled by rock paper scissors at the table, so the engine reports joint winners.
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
