using System;
using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;

namespace LemonadeWars.Engine.Ai
{
    /// <summary>Drives a game to completion with a bot in every seat.</summary>
    public static class GameRunner
    {
        /// <summary>One-line state summary for deadlock/timeout diagnostics.</summary>
        public static string Describe(Game game)
        {
            var state = game.State;
            string decisions = string.Join(",", state.PendingDecisions.Select(d => $"P{d.PlayerId}:{d.Kind}"));
            string money = string.Join(",", state.Players.Select(p => $"P{p.PlayerId}=${p.Money}"));
            return $"[stage={state.Stage} phase={state.Phase} active={state.ActivePlayer} " +
                   $"decisions=({decisions}) awaiting=({string.Join(",", state.AwaitingResponse)}) " +
                   $"stack={state.ResponseStack.Count} roll={state.PendingRoll?.Value.ToString() ?? "-"} " +
                   $"buyQueue=({string.Join(",", state.InitialBuyQueue)}) " +
                   $"standDone={state.InitialBuyStandDone} {money}]";
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
