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
        private static int _rigId = 9100;

        /// <summary>Deal through setup and the first draw, stopping in the active player's Play phase.</summary>
        public static Game ReadyToAct(ulong seed, int players)
        {
            var names = new[] { "Ana", "Ben", "Cal", "Dee" }.Take(players).ToArray();
            var game = Game.Create(TestData.Db, names, seed);
            foreach (var p in game.State.Players)
            {
                game.Apply(new ChooseLemonLords
                {
                    PlayerId = p.PlayerId,
                    KeepTitleIds = p.LemonLordDealt.Take(2).ToList(),
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
            var s = game.State;
            int Find(List<int> zone) => zone.FirstOrDefault(id => s.LemonInstances[id].DefId == defId);

            int found = Find(s.LemonDeck);
            if (found != 0)
            {
                s.LemonDeck.Remove(found);
            }
            else if ((found = Find(s.LemonDiscard)) != 0)
            {
                s.LemonDiscard.Remove(found);
            }
            else
            {
                foreach (var p in s.Players)
                {
                    found = Find(p.Hand);
                    if (found != 0)
                    {
                        p.Hand.Remove(found);
                        break;
                    }
                }
            }
            Assert.NotEqual(0, found);
            s.Players[playerId].Hand.Add(found);
        }

        /// <summary>GiveCard, but returns the instance id that landed in the hand.</summary>
        public static int Hold(Game game, int playerId, string defId)
        {
            GiveCard(game, playerId, defId);
            return game.State.Players[playerId].Hand[^1];
        }

        /// <summary>Bury the player's whole hand at the bottom of the deck.</summary>
        public static void BuryHand(Game game, int playerId)
        {
            var hand = game.State.Players[playerId].Hand;
            game.State.LemonDeck.AddRange(hand);
            hand.Clear();
        }

        /// <summary>Bury the player's hand at the bottom of the deck, then deal them one named card.</summary>
        public static void StripToOneCard(Game game, int playerId, string defId)
        {
            BuryHand(game, playerId);
            GiveCard(game, playerId, defId);
        }

        /// <summary>Bury every copy of the defs from ALL hands.</summary>
        public static void StripAllHands(Game game, params string[] defIds)
        {
            var s = game.State;
            foreach (var p in s.Players)
            {
                var strip = p.Hand.Where(id => defIds.Contains(s.LemonInstances[id].DefId)).ToList();
                foreach (int id in strip)
                {
                    p.Hand.Remove(id);
                    s.LemonDeck.Add(id);
                }
            }
        }

        public static StandInstance AddStand(PlayerState player, string typeId)
        {
            var stand = new StandInstance
            {
                InstanceId = _rigId++,
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
            var s = game.State;
            int found = s.BlackMarketDeck.First(id => s.BlackMarketInstances[id].DefId == defId);
            s.BlackMarketDeck.Remove(found);
            (stand?.Equipped ?? player.Turf.Equipped).Add(found);
            return found;
        }
    }
}
