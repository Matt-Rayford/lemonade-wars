using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.BotArena;

internal static partial class StrategyMode
{
    private sealed class BehaviorCounters
    {
        public readonly int[] Attacks;
        public readonly int[] Draws;
        public readonly Dictionary<string, int>[] StandsBought;
        public readonly List<string>[] BlackMarketBought;

        public BehaviorCounters(int players)
        {
            Attacks = new int[players];
            Draws = new int[players];
            StandsBought = Enumerable.Range(0, players).Select(_ => new Dictionary<string, int>()).ToArray();
            BlackMarketBought = Enumerable.Range(0, players).Select(_ => new List<string>()).ToArray();
        }

        /// <summary>Must run BEFORE the action is applied: it reads the hand and shelf the action names.</summary>
        public void Count(Game game, int actor, GameAction action)
        {
            switch (action)
            {
                case PlayLemonCard play when game.State.LemonInstances.ContainsKey(play.CardInstanceId):
                    if (game.Db.Lemon(game.State.LemonInstances[play.CardInstanceId].DefId).Type
                        == LemonCardType.Attack)
                    {
                        Attacks[actor]++;
                    }
                    break;
                case DrawLemonCard _:
                    Draws[actor]++;
                    break;
                case BuyStand buyStand:
                    CountStand(actor, buyStand.StandTypeId);
                    break;
                case InitialBuyStand initial:
                    CountStand(actor, initial.StandTypeId);
                    break;
                case BuyBlackMarket buy when buy.MarketIndex < game.State.Market.Count:
                    BlackMarketBought[actor].Add(
                        game.State.BlackMarketInstances[game.State.Market[buy.MarketIndex]].DefId);
                    break;
            }
        }

        private void CountStand(int actor, string standTypeId)
        {
            var bought = StandsBought[actor];
            bought[standTypeId] = bought.TryGetValue(standTypeId, out int count) ? count + 1 : 1;
        }
    }
}
