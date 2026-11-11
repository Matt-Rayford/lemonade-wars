using System;
using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>
    /// One-ply heuristic scoring, no lookahead: buys VP when it can, builds economy,
    /// attacks the leader, defends itself. Deterministic: equal scores resolve by
    /// enumeration order.
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

        /// <summary>Best-first by the greedy score; stable, so ties keep the engine's enumeration order.</summary>
        public List<GameAction> Ranked(Game game, int playerId)
        {
            var moves = game.LegalMovesFor(playerId);
            return moves
                .Select(move => (Move: move, Score: Score(game, playerId, move)))
                .OrderByDescending(scored => scored.Score)
                .Select(scored => scored.Move)
                .ToList();
        }

        private static double Score(Game game, int playerId, GameAction move)
        {
            var me = game.State.Players[playerId];
            bool isLastLap = ThreatAssessment.IsNearGameEnd(game);

            switch (move)
            {
                case ChooseLemonLords choose:
                    return choose.KeepTitleIds.Sum(id => LordAffinity(id));
                case InitialBuyStand draftStand:
                    return ScoreDraftStand(game, me, draftStand);
                case InitialBuyEnd _:
                    return 5;

                case BuyBraggingRights _:
                    return ScoreBraggingRights(game, me, isLastLap);
                case BuyStand buyStand:
                    return ScoreStand(game, me, buyStand, isLastLap);
                case BuyBlackMarket purchase:
                    return ScoreBlackMarketPurchase(game, me, purchase);
                case RefreshMarket _:
                    return 2;
                case DrawLemonCard _:
                    return ScoreDraw(game, me, isLastLap);
                case EndTurn _:
                    return 1;

                case PlayLemonCard play:
                    return ScorePlay(game, me, play);

                case PassWindow _:
                    return 10;
                case RespondToWindow respond:
                    return ScoreResponse(game, me, respond);
                case UseTurnAbility ability:
                    return ScoreAbility(game, me, ability);

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

        /// <summary>How much we like keeping a card (higher = keep; discard/give low values).</summary>
        private static double CardValue(Game game, PlayerState me, int instanceId)
        {
            var def = game.Db.Lemon(game.State.LemonInstances[instanceId].DefId);
            double value;
            switch (def.Type)
            {
                case LemonCardType.Instant:
                    value = def.Id == LemonCardIds.Tantrum ? 8 : 6;
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
                if (lordId == LemonLordIds.FumingPhil && def.Type == LemonCardType.Attack)
                {
                    value += 5;
                }
                else if (lordId == LemonLordIds.PamThePlanner && def.Type == LemonCardType.Plan)
                {
                    value += 5;
                }
                else if (lordId == LemonLordIds.MeltdownMaster && def.Id == LemonCardIds.Tantrum)
                {
                    value += 6;
                }
                else if (lordId == LemonLordIds.Hoarder)
                {
                    value += 2;
                }
            }
            return value;
        }
    }
}
