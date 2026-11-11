using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using Xunit;
using static LemonadeWars.Engine.Tests.GameScenario;
using static LemonadeWars.Engine.Tests.StateRigging;

namespace LemonadeWars.Engine.Tests
{
    public class LemonCardEffectTests
    {
        [Fact]
        public void SmearCampaignStealsTwoAndCostsNoAction()
        {
            var game = ReadyToPlay(seed: 7);
            StripAttackReactions(game);
            int attacker = ActivePlayer(game);
            int victim = SeatAfterActive(game, 1);
            int attack = GiveCard(game, attacker, "smear-campaign");
            int attackerHandBefore = game.State.Players[attacker].Hand.Count;
            int victimHandBefore = game.State.Players[victim].Hand.Count;
            int actionsBefore = game.State.ActionsRemaining;

            game.Apply(new PlayLemonCard { PlayerId = attacker, CardInstanceId = attack, TargetPlayerId = victim });

            Assert.Equal(attackerHandBefore - 1 + 2, game.State.Players[attacker].Hand.Count);
            Assert.Equal(victimHandBefore - 2, game.State.Players[victim].Hand.Count);
            // "Then you may play any card" means the smear costs no action: the spent action
            // comes straight back, with no free-play decision.
            Assert.Empty(game.State.PendingDecisions);
            Assert.Equal(actionsBefore, game.State.ActionsRemaining);
        }

        [Fact]
        public void TrashPandasSwapsHands()
        {
            var game = ReadyToPlay(seed: 7);
            StripAttackReactions(game);
            int attacker = ActivePlayer(game);
            int victim = SeatAfterActive(game, 1);
            int card = GiveCard(game, attacker, "trash-pandas");
            var attackerCards = game.State.Players[attacker].Hand.Where(id => id != card).ToList();
            var victimCards = game.State.Players[victim].Hand.ToList();

            game.Apply(new PlayLemonCard { PlayerId = attacker, CardInstanceId = card, TargetPlayerId = victim });

            Assert.Equal(victimCards, game.State.Players[attacker].Hand);
            Assert.Equal(attackerCards, game.State.Players[victim].Hand);
        }

        [Fact]
        public void RummageSaleSellsEquipsForHalfRoundedUp()
        {
            var game = ReadyToPlay(seed: 7);
            StripHands(game, "tantrum");
            int seller = ActivePlayer(game);
            int equipped = RigTurfEquipFromDeckTop(game, seller);
            int cost = BlackMarketDefOf(game, equipped).Cost;
            int card = GiveCard(game, seller, "rummage-sale");
            int moneyBefore = game.State.Players[seller].Money;

            game.Apply(new PlayLemonCard
            {
                PlayerId = seller,
                CardInstanceId = card,
                SelectedInstanceIds = new List<int> { equipped },
            });

            Assert.Equal(moneyBefore + (cost + 1) / 2, game.State.Players[seller].Money);
            Assert.Contains(equipped, game.State.BlackMarketDiscard);
        }

        [Fact]
        public void RummageSalePaysOnceForEverythingSold()
        {
            var game = ReadyToPlay(seed: 7);
            StripHands(game, "tantrum");
            int seller = ActivePlayer(game);
            int first = RigTurfEquipFromDeckTop(game, seller);
            int second = RigTurfEquipFromDeckTop(game, seller);
            int proceeds = (BlackMarketDefOf(game, first).Cost + 1) / 2 + (BlackMarketDefOf(game, second).Cost + 1) / 2;
            int card = GiveCard(game, seller, "rummage-sale");

            var events = game.Apply(new PlayLemonCard
            {
                PlayerId = seller,
                CardInstanceId = card,
                SelectedInstanceIds = new List<int> { first, second },
            });

            var payout = Assert.Single(events.OfType<MoneyChanged>(), change => change.Reason == "rummage sale");
            Assert.Equal(proceeds, payout.Amount);
        }

        [Fact]
        public void ReverseEngineerForcesThePlayOfARecoveredCard()
        {
            var game = ReadyToPlay(seed: 7);
            StripHands(game, "tantrum");
            int player = ActivePlayer(game);
            int automation = PutInLemonDiscard(game, "automation");
            int card = GiveCard(game, player, "reverse-engineer");

            game.Apply(new PlayLemonCard
            {
                PlayerId = player,
                CardInstanceId = card,
                DiscardedLemonInstanceId = automation,
            });

            var decision = Assert.Single(game.State.PendingDecisions);
            Assert.Equal(DecisionKind.ForcedPlay, decision.Kind);
            // Only the recovered card may be played.
            Assert.Throws<InvalidActionException>(() => game.Apply(new PlayLemonCard
            {
                PlayerId = player,
                CardInstanceId = game.State.Players[player].Hand.First(id => id != automation),
            }));
            game.Apply(new PlayLemonCard { PlayerId = player, CardInstanceId = automation });

            // Reverse Engineer cost the action; Automation added 2: 2 - 1 + 2 = 3.
            Assert.Equal(3, game.State.ActionsRemaining);
        }

        [Fact]
        public void ReverseEngineerOffersEveryDiscardedCopy()
        {
            var game = ReadyToPlay(seed: 7);
            StripHands(game, "tantrum");
            int player = ActivePlayer(game);
            // The picker shows the physical pile, so BOTH discarded Automations must be
            // offered, not one row per distinct card.
            var copies = new List<int>
            {
                PutInLemonDiscard(game, "automation"),
                PutInLemonDiscard(game, "automation"),
            };

            int card = GiveCard(game, player, "reverse-engineer");
            var offered = game.LegalMovesFor(player).OfType<PlayLemonCard>()
                .Where(m => m.CardInstanceId == card && m.DiscardedLemonInstanceId != null)
                .Select(m => m.DiscardedLemonInstanceId!.Value)
                .ToList();

            Assert.Contains(copies[0], offered);
            Assert.Contains(copies[1], offered);
            // Every playable card in the pile, each exactly once.
            Assert.Equal(offered.Count, offered.Distinct().Count());
            int playable = game.State.LemonDiscard.Count(id =>
            {
                var def = TestData.Db.Lemon(game.State.LemonInstances[id].DefId);
                return def.Type == LemonCardType.Plan || def.Type == LemonCardType.Attack;
            });
            Assert.Equal(playable, offered.Count);
        }

        [Fact]
        public void ReduceAndReuseOffersEveryDiscardedBlackMarketCopy()
        {
            var game = ReadyToPlay(seed: 7);
            StripHands(game, "tantrum");
            int player = ActivePlayer(game);
            var state = game.State;
            // Two copies of one Black Market card sit in the discard.
            var copies = state.BlackMarketDeck
                .GroupBy(id => state.BlackMarketInstances[id].DefId)
                .First(sameCard => sameCard.Count() >= 2)
                .Take(2).ToList();
            foreach (int id in copies)
            {
                state.BlackMarketDeck.Remove(id);
                state.BlackMarketDiscard.Add(id);
            }

            int card = GiveCard(game, player, "reduce-and-reuse");
            var offered = game.LegalMovesFor(player).OfType<PlayLemonCard>()
                .Where(m => m.CardInstanceId == card && m.DiscardedBmInstanceId != null)
                .Select(m => m.DiscardedBmInstanceId!.Value)
                .Distinct().ToList();

            Assert.Contains(copies[0], offered);
            Assert.Contains(copies[1], offered);
            Assert.Equal(state.BlackMarketDiscard.Count, offered.Count);
        }
    }
}
