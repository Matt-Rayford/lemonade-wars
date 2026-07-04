using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Title evaluation: First Dibs are claimed automatically the instant their condition
    /// holds (checked after every action settles); Lemon Lord titles are secret and scored
    /// once at game end. Every title is worth 1 VP.
    /// </summary>
    public sealed partial class Game
    {
        // ------------------------------------------------------ first dibs

        /// <summary>Award every face-up First Dibs title whose condition a player now meets.</summary>
        private void CheckFirstDibs(List<GameEvent> events)
        {
            foreach (string titleId in State.FirstDibsRow.ToList())
            {
                // Turn order from the active player breaks simultaneous claims.
                int n = State.Players.Count;
                for (int offset = 0; offset < n; offset++)
                {
                    var player = State.Players[(State.ActivePlayer + offset) % n];
                    if (!MeetsFirstDibs(player, titleId))
                    {
                        continue;
                    }
                    State.FirstDibsRow.Remove(titleId);
                    player.FirstDibsClaimed.Add(titleId);
                    events.Add(new TitleClaimed { PlayerId = player.PlayerId, TitleId = titleId });
                    // Claiming a VP may hand off Spoiled Rotten (rulebook p12).
                    AssignSpoiledRotten(events);
                    break;
                }
            }
        }

        private bool MeetsFirstDibs(PlayerState p, string titleId)
        {
            var stats = State.RollStats.TryGetValue(p.PlayerId, out var s) ? s : null;
            switch (titleId)
            {
                case "balanced-set":
                {
                    var shapes = ShapesOf(p);
                    return shapes.Count(sh => sh == Shape.Circle) >= 2 &&
                           shapes.Count(sh => sh == Shape.Diamond) >= 2 &&
                           shapes.Count(sh => sh == Shape.Square) >= 2;
                }
                case "block-builder":
                    return ShapesOf(p).Count(sh => sh == Shape.Square) >= 6;
                case "diamond-dealer":
                    return ShapesOf(p).Count(sh => sh == Shape.Diamond) >= 6;
                case "full-circle":
                    return ShapesOf(p).Count(sh => sh == Shape.Circle) >= 6;

                case "big-earner":
                    return stats != null && stats.Earned >= 9;
                case "clean-getaway":
                    return stats != null && stats.MoneyStolen >= 3;
                case "sticky-fingers":
                    return stats != null && stats.CardsStolen >= 2;
                case "double-scoop":
                    return stats != null && stats.CardsKept >= 2;

                case "connoisseur":
                    return CountStands(p, "gourmet") >= 3 &&
                           p.Stands.Count(st => st.StandTypeId != "gourmet") >= 1;
                case "local-legend":
                    return CountStands(p, "classic") >= 4;
                case "penny-pincher":
                    return CountStands(p, "bargain") >= 5;

                case "cranky-pants":
                    return p.TantrumPile.Count >= 3;
                case "expander":
                    return p.Turf.Equipped.Count >= 5;
                case "innovator":
                    return p.Stands.Sum(st => st.Equipped.Count) >= 5;
                case "shopaholic":
                    return p.PlayerId == State.ActivePlayer && State.SpentThisTurn >= 12;
                case "stand-surge":
                    return p.Stands.Any(st =>
                        StandEarnings(p, st) - Db.StandType(st.StandTypeId).BaseEarnings >= 3);

                default:
                    return false; // unknown title: never auto-claimed
            }
        }

        /// <summary>All shape icons a player controls: their stands plus every equipped Black Market copy.</summary>
        private List<Shape> ShapesOf(PlayerState p)
        {
            var shapes = p.Stands.Select(st => st.Shape).ToList();
            shapes.AddRange(p.Turf.Equipped
                .Concat(p.Stands.SelectMany(st => st.Equipped))
                .Select(id => State.BlackMarketInstances[id].Shape));
            return shapes;
        }

        private int CountStands(PlayerState p, string typeId) =>
            p.Stands.Count(st => st.StandTypeId == typeId);

        // ------------------------------------------------------ lemon lord

        /// <summary>Is this end-game condition satisfied right now?</summary>
        public bool MeetsLemonLord(PlayerState p, string titleId)
        {
            switch (titleId)
            {
                case "bare-bones":
                    return p.Stands.Count(st => st.Equipped.Count == 0) >= 3;
                case "budget-beast":
                    return p.Stands.Any(st => st.StandTypeId == "bargain" && StandEarnings(p, st) >= 3);
                case "elite-squeezer":
                    return p.Stands.Any(st => st.StandTypeId == "gourmet" && st.Equipped.Count >= 4);
                case "expert-whiner":
                    return State.WhiniestBabyHolder == p.PlayerId;
                case "friendly-fran":
                    return p.TantrumPile.Count == 0;

                case "fuming-phil":
                    return CountHand(p, LemonCardType.Attack) >= 5;
                case "pam-the-planner":
                    return CountHand(p, LemonCardType.Plan) >= 5;
                case "hoarder":
                    return p.Hand.Count >= 10;
                case "meltdown-master":
                    return p.Hand.Count(id => State.LemonInstances[id].DefId == "tantrum") >= 3;

                case "cash-queen":
                    return EquippedDefs(p).Count(d => d.Cost >= 6) >= 3;
                case "pour-master":
                    return EquippedDefs(p).Count(d => d.Name == "Spiked Lemonade") >= 2;
                case "pushover":
                    return EquippedDefs(p).Count(d => d.Name == "Pushy Salesman") >= 4;

                // Icon-count conditions, validated against the deck by the data pipeline.
                case "card-shark":
                    return EquippedDefs(p).Count(d => d.Icons.Contains("draw")) >= 2;
                case "flow-master":
                    return EquippedDefs(p).Count(d => d.Timing == EffectTiming.PowerPour) >= 4;
                case "no-bullies-allowed":
                    return EquippedDefs(p).Count(d => d.Category == "Defense") >= 2;
                case "profit-pusher":
                    return EquippedDefs(p).Count(d => d.Icons.Contains("dollar")) >= 3;
                case "roll-wrangler":
                    return EquippedDefs(p).Count(d => d.Category == "Roll Modification") >= 3;
                case "sales-engine":
                    return EquippedDefs(p).Count(d => d.Timing == EffectTiming.OnSale) >= 3;
                case "swindling-sammy":
                    return EquippedDefs(p).Count(d => d.Icons.Contains("steal-card")) >= 2;
                case "thieving-tommy":
                    return EquippedDefs(p).Count(d => d.Icons.Contains("steal-dollar")) >= 2;

                default:
                    return false;
            }
        }

        private int CountHand(PlayerState p, LemonCardType type) =>
            p.Hand.Count(id => Db.Lemon(State.LemonInstances[id].DefId).Type == type);

        private IEnumerable<BlackMarketCardDef> EquippedDefs(PlayerState p) =>
            p.Turf.Equipped
                .Concat(p.Stands.SelectMany(st => st.Equipped))
                .Select(EquippedDef);

        /// <summary>Final scoring: in-game VP plus each kept Lemon Lord title that holds (rulebook p2/p13).</summary>
        private void FinishGameWithScores(List<GameEvent> events)
        {
            State.Stage = GameStage.Finished;

            var scores = new Dictionary<int, int>();
            foreach (var player in State.Players)
            {
                int total = player.InGameVictoryPoints;
                foreach (string titleId in player.LemonLordKept)
                {
                    if (MeetsLemonLord(player, titleId))
                    {
                        total += Db.Title(titleId).VictoryPoints;
                        events.Add(new LemonLordMet { PlayerId = player.PlayerId, TitleId = titleId });
                    }
                }
                scores[player.PlayerId] = total;
            }

            int best = scores.Values.Max();
            State.Winners.AddRange(
                State.Players.Where(p => scores[p.PlayerId] == best).Select(p => p.PlayerId));

            events.Add(new StageChanged { Stage = State.Stage });
            events.Add(new GameEnded { Winners = State.Winners.ToList(), Scores = scores });
        }
    }
}
