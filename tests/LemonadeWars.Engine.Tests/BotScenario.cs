using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Ai;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using Xunit;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>
    /// Scripted-position rig for bot tests: deal a game to the first Play phase, then
    /// mutate <c>game.State</c> directly (hands, stands, equips) so a single decision
    /// is isolated from everything else the bot could do.
    /// </summary>
    internal static class BotScenario
    {
        private static readonly string[] ReactionCardDefIds = { "tantrum", "tag-youre-it", "im-rubber-youre-glue" };

        private static int _nextRigStandId = 9100;

        /// <summary>Deal through setup and the first draw, stopping in the active player's Play phase.</summary>
        public static Game ReadyToAct(ulong seed, int players)
        {
            var names = new[] { "Ana", "Ben", "Cal", "Dee" }.Take(players).ToArray();
            var game = Game.Create(TestData.Db, names, seed);
            foreach (var player in game.State.Players)
            {
                game.Apply(new ChooseLemonLords
                {
                    PlayerId = player.PlayerId,
                    KeepTitleIds = player.LemonLordDealt.Take(2).ToList(),
                });
            }
            while (game.State.Stage == GameStage.InitialBuys)
            {
                int buyer = game.State.InitialBuyQueue[0];
                game.Apply(new InitialBuyStand { PlayerId = buyer, StandTypeId = "bargain" });
                game.Apply(new InitialBuyEnd { PlayerId = buyer });
            }
            var warmup = new GreedyBot();
            while (game.State.Phase != TurnPhase.Play && game.State.Stage != GameStage.Finished)
            {
                game.Apply(warmup.Choose(game, game.ActingPlayers()[0]));
            }
            return game;
        }

        /// <summary>Move one instance of a lemon def into the player's hand, from wherever it is.</summary>
        public static void GiveCard(Game game, int playerId, string defId)
        {
            var state = game.State;
            int FirstIn(List<int> zone) => zone.FirstOrDefault(id => state.LemonInstances[id].DefId == defId);

            int found = FirstIn(state.LemonDeck);
            if (found != 0)
            {
                state.LemonDeck.Remove(found);
            }
            else if ((found = FirstIn(state.LemonDiscard)) != 0)
            {
                state.LemonDiscard.Remove(found);
            }
            else
            {
                foreach (var player in state.Players)
                {
                    found = FirstIn(player.Hand);
                    if (found != 0)
                    {
                        player.Hand.Remove(found);
                        break;
                    }
                }
            }
            Assert.NotEqual(0, found);
            state.Players[playerId].Hand.Add(found);
        }

        /// <summary>GiveCard, but returns the instance id that landed in the hand.</summary>
        public static int Hold(Game game, int playerId, string defId)
        {
            GiveCard(game, playerId, defId);
            return game.State.Players[playerId].Hand[^1];
        }

        /// <summary>The hand goes to the bottom of the deck.</summary>
        public static void BuryHand(Game game, int playerId)
        {
            var hand = game.State.Players[playerId].Hand;
            game.State.LemonDeck.AddRange(hand);
            hand.Clear();
        }

        public static void StripToOneCard(Game game, int playerId, string defId)
        {
            BuryHand(game, playerId);
            GiveCard(game, playerId, defId);
        }

        /// <summary>Bury every copy of the defs from ALL hands.</summary>
        public static void StripAllHands(Game game, params string[] defIds)
        {
            var state = game.State;
            foreach (var player in state.Players)
            {
                var stripped = player.Hand.Where(id => defIds.Contains(state.LemonInstances[id].DefId)).ToList();
                foreach (int id in stripped)
                {
                    player.Hand.Remove(id);
                    state.LemonDeck.Add(id);
                }
            }
        }

        /// <summary>Nobody can answer a play except with the cards the test then deals.</summary>
        public static void StripReactionCards(Game game) => StripAllHands(game, ReactionCardDefIds);

        /// <summary>Secret titles (Friendly Fran, Expert Whiner...) sway bot scores; clear them from every seat.</summary>
        public static void ClearLemonLords(Game game)
        {
            foreach (var player in game.State.Players)
            {
                player.LemonLordKept.Clear();
            }
        }

        public static StandInstance AddStand(PlayerState player, string typeId)
        {
            var stand = new StandInstance
            {
                InstanceId = _nextRigStandId++,
                StandTypeId = typeId,
                Shape = Shape.Circle,
            };
            player.Stands.Add(stand);
            return stand;
        }

        /// <summary>
        /// Pull one copy of a Black Market def out of the draw pile and equip it on the
        /// given stand (or the player's turf when null); returns its instance id.
        /// </summary>
        public static int EquipFromDeck(Game game, PlayerState player, string defId, StandInstance? stand = null)
        {
            var state = game.State;
            int found = state.BlackMarketDeck.First(id => state.BlackMarketInstances[id].DefId == defId);
            state.BlackMarketDeck.Remove(found);
            (stand?.Equipped ?? player.Turf.Equipped).Add(found);
            return found;
        }
    }
}
