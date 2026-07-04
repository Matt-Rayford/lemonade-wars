using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;
using Xunit;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>Black Market card effects, activated abilities, reactions, and title claiming.</summary>
    public class BlackMarketTests
    {
        private static readonly string[] FourPlayers = { "Ana", "Ben", "Cal", "Dee" };

        private static Game ReadyToPlay(ulong seed = 11, string[]? names = null)
        {
            var game = Game.Create(TestData.Db, names ?? FourPlayers, seed);
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
            GameFlowTests.PassAll(game);
            return game;
        }

        // ------------------------------------------------------- rig helpers

        private static int Active(Game game) => game.State.ActivePlayer;

        private static int Seat(Game game, int offset) =>
            (game.State.ActivePlayer + offset) % game.State.Players.Count;

        /// <summary>Pull a specific Black Market def out of the deck/market and equip it directly (test rig).</summary>
        private static int RigEquip(Game game, int playerId, string defId, int? standInstanceId = null)
        {
            var s = game.State;
            int found = s.BlackMarketDeck.Concat(s.Market)
                .First(id => s.BlackMarketInstances[id].DefId == defId);
            s.BlackMarketDeck.Remove(found);
            s.Market.Remove(found);
            var player = s.Players[playerId];
            if (standInstanceId is int standId)
            {
                player.Stands.First(st => st.InstanceId == standId).Equipped.Add(found);
            }
            else
            {
                player.Turf.Equipped.Add(found);
            }
            return found;
        }

        private static int GiveCard(Game game, int playerId, string defId)
        {
            var s = game.State;
            int found = s.LemonDeck.Concat(s.LemonDiscard)
                .Concat(s.Players.SelectMany(p => p.Hand))
                .First(id => s.LemonInstances[id].DefId == defId);
            s.LemonDeck.Remove(found);
            s.LemonDiscard.Remove(found);
            foreach (var p in s.Players)
            {
                p.Hand.Remove(found);
            }
            s.Players[playerId].Hand.Add(found);
            return found;
        }

        private static void StripHands(Game game, params string[] defIds)
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

        /// <summary>End the turn with a chosen die value (an Out of Stock holder keeps the window open).</summary>
        private static void EndTurnWithRoll(Game game, int value)
        {
            int holder = Seat(game, 1);
            int stock = GiveCard(game, holder, "out-of-stock");
            game.Apply(new EndTurn { PlayerId = Active(game) });
            Assert.NotNull(game.State.PendingRoll);
            game.State.PendingRoll!.Value = value;
            GameFlowTests.PassAll(game);
            // Park the helper card back in the deck so it cannot affect later windows.
            var holderHand = game.State.Players[holder].Hand;
            if (holderHand.Remove(stock))
            {
                game.State.LemonDeck.Add(stock);
            }
        }

        // ---------------------------------------------------- computed stats

        [Fact]
        public void PushySalesmanAddsItsPrintedNumber()
        {
            var game = ReadyToPlay();
            var player = game.State.Players[Active(game)];
            var stand = player.Stands[0]; // bargain: 1-3
            RigEquip(game, player.PlayerId, "pushy-salesman-6", stand.InstanceId);

            Assert.Equal(new[] { 1, 2, 3, 6 }, game.SaleNumbersOf(stand).OrderBy(x => x));
        }

        [Fact]
        public void SpikedLemonadeAddsPourNumberAndInterestPaysPerNumber()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int a = Active(game);
            var player = game.State.Players[a];
            player.Turf.PowerPourNumber = 2;
            RigEquip(game, a, "spiked-lemonade-5");
            RigEquip(game, a, "interest");
            Assert.Equal(new[] { 2, 5 }, game.PourNumbersOf(player).OrderBy(x => x));

            var money = game.State.Players.Select(p => p.Money).ToList();
            EndTurnWithRoll(game, 5);

            // Roll 5: bargains (1-3) silent; A pours on the Spiked 5:
            // base $1 + Interest $2 (two pour numbers). Other players: pour only if their number is 5.
            Assert.Equal(money[a] + 3, player.Money);
        }

        [Fact]
        public void LeftyAndRightyBoostNeighborStands()
        {
            var game = ReadyToPlay();
            var player = game.State.Players[Active(game)];
            var left = player.Stands[0];
            var right = player.Stands[1];
            RigEquip(game, player.PlayerId, "lefty-loosey", right.InstanceId);  // boosts stand to its left
            RigEquip(game, player.PlayerId, "righty-tighty", left.InstanceId); // boosts stand to its right

            Assert.Equal(2, game.StandEarnings(player, left));  // 1 base + lefty on right neighbor
            Assert.Equal(2, game.StandEarnings(player, right)); // 1 base + righty on left neighbor
        }

        [Fact]
        public void StandRowInsertPositionIsRespected()
        {
            var game = ReadyToPlay();
            int a = Active(game);
            var player = game.State.Players[a];
            player.Money = 50;
            var originalFirst = player.Stands[0].InstanceId;

            game.Apply(new BuyStand { PlayerId = a, StandTypeId = "classic", InsertIndex = 0 });

            Assert.Equal("classic", player.Stands[0].StandTypeId);
            Assert.Equal(originalFirst, player.Stands[1].InstanceId);
        }

        // -------------------------------------------------- sale/pour triggers

        [Fact]
        public void MeditationDrawsOnSale()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int a = Active(game);
            var player = game.State.Players[a];
            RigEquip(game, a, "meditation", player.Stands[0].InstanceId);
            int hand = player.Hand.Count;

            EndTurnWithRoll(game, 1); // both bargains sell; meditation draws 1

            Assert.Equal(hand + 1, player.Hand.Count);
        }

        [Fact]
        public void JuiceBoxJoeyStealsFromAChosenVictim()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int a = Active(game);
            int b = Seat(game, 1);
            var player = game.State.Players[a];
            RigEquip(game, a, "juice-box-joey", player.Stands[0].InstanceId);
            foreach (var p in game.State.Players)
            {
                p.Money = 20;
            }

            int holder = Seat(game, 1);
            GiveCard(game, holder, "out-of-stock");
            game.Apply(new EndTurn { PlayerId = a });
            game.State.PendingRoll!.Value = 2;
            while (game.State.AwaitingResponse.Count > 0)
            {
                game.Apply(new PassWindow { PlayerId = game.State.AwaitingResponse[0] });
            }

            // The steal needs a victim.
            var decision = Assert.Single(game.State.PendingDecisions);
            Assert.Equal(DecisionKind.AbilityVictim, decision.Kind);
            game.Apply(new SubmitAbilityChoice { PlayerId = a, TargetPlayerId = b });
            GameFlowTests.PassAll(game);

            // A: 2 bargains sold (+2) + stolen $1; B: -$1 (+ B's own sales if any).
            Assert.Equal(20 + 2 + 1 + (game.PourNumbersOf(player).Contains(2) ? 1 : 0),
                game.State.Players[a].Money);
        }

        [Fact]
        public void WhispersOfFateDrawsTwoDiscardsOne()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int a = Active(game);
            var player = game.State.Players[a];
            player.Turf.PowerPourNumber = 4;
            RigEquip(game, a, "whispers-of-fate");
            int hand = player.Hand.Count;

            int holder = Seat(game, 1);
            GiveCard(game, holder, "out-of-stock");
            game.Apply(new EndTurn { PlayerId = a });
            game.State.PendingRoll!.Value = 4;
            while (game.State.AwaitingResponse.Count > 0)
            {
                game.Apply(new PassWindow { PlayerId = game.State.AwaitingResponse[0] });
            }

            var discard = game.State.PendingDecisions
                .Single(d => d.Kind == DecisionKind.AbilityDiscard);
            Assert.Equal(a, discard.PlayerId);
            game.Apply(new SubmitAbilityChoice
            {
                PlayerId = a,
                CardInstanceIds = new List<int> { player.Hand[^1] },
            });
            GameFlowTests.PassAll(game);

            Assert.Equal(hand + 1, player.Hand.Count); // +2 drawn, -1 discarded
        }

        [Fact]
        public void HalfPintHarryStealsARandomCardAndGivesOneBack()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int a = Active(game);
            int b = Seat(game, 1);
            var player = game.State.Players[a];
            var victim = game.State.Players[b];
            player.Turf.PowerPourNumber = 6;
            RigEquip(game, a, "half-pint-harry");
            int aHand = player.Hand.Count;
            int bHand = victim.Hand.Count;

            int holder = Seat(game, 1);
            GiveCard(game, holder, "out-of-stock");
            bHand = victim.Hand.Count; // may have changed if b == holder
            game.Apply(new EndTurn { PlayerId = a });
            game.State.PendingRoll!.Value = 6;
            while (game.State.AwaitingResponse.Count > 0)
            {
                game.Apply(new PassWindow { PlayerId = game.State.AwaitingResponse[0] });
            }

            game.Apply(new SubmitAbilityChoice { PlayerId = a, TargetPlayerId = b });
            var giveBack = game.State.PendingDecisions
                .Single(d => d.Kind == DecisionKind.AbilityGiveBack);
            int stolen = giveBack.StolenCardId!.Value;
            int given = player.Hand.First(id => id != stolen);
            game.Apply(new SubmitAbilityChoice
            {
                PlayerId = a,
                CardInstanceIds = new List<int> { given },
            });
            GameFlowTests.PassAll(game);

            Assert.Contains(stolen, player.Hand);
            Assert.Contains(given, victim.Hand);
            Assert.Equal(aHand, player.Hand.Count);      // net zero: +1 stolen, -1 given
            // Victim: -1 stolen, +1 given back, +1 turn-start draw (they are next player).
            Assert.Equal(bHand + 1, victim.Hand.Count);
        }

        // ------------------------------------------------------ turn abilities

        [Fact]
        public void DownsellModifiesThePendingRoll()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "out-of-stock");
            int a = Active(game);
            var player = game.State.Players[a];
            int downsell = RigEquip(game, a, "downsell");
            // A bystander with Out of Stock keeps the roll window open around the modify.
            GiveCard(game, Seat(game, 1), "out-of-stock");
            int money = player.Money;

            game.Apply(new EndTurn { PlayerId = a });
            // Downsell keeps the window open for its owner too.
            Assert.Contains(a, game.State.AwaitingResponse);
            game.State.PendingRoll!.Value = 4;
            game.Apply(new UseTurnAbility { PlayerId = a, EquippedInstanceId = downsell });
            Assert.Equal(3, game.State.PendingRoll!.Value);

            // Once per turn.
            Assert.Throws<InvalidActionException>(() =>
                game.Apply(new UseTurnAbility { PlayerId = a, EquippedInstanceId = downsell }));
            GameFlowTests.PassAll(game);

            // Roll 3: both bargains sell.
            Assert.True(game.State.Players[a].Money >= money + 2);
        }

        [Fact]
        public void LiquidEnergyGrantsAnExtraTableSale()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "out-of-stock");
            int a = Active(game);
            int liquid = RigEquip(game, a, "liquid-energy");
            var money = game.State.Players.Select(p => p.Money).ToList();

            var events = game.Apply(new UseTurnAbility { PlayerId = a, EquippedInstanceId = liquid }).ToList();
            int roll = events.OfType<SaleRolled>().Single().Value;

            // Table-wide: everyone's bargains sell on 1-3.
            if (roll <= 3)
            {
                foreach (var p in game.State.Players)
                {
                    Assert.True(p.Money >= money[p.PlayerId] + 2);
                }
            }
            Assert.Equal(TurnPhase.Play, game.State.Phase); // turn continues
        }

        [Fact]
        public void ShoppingSpreeAndPeddlinPeteDiscountPurchases()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int next = Seat(game, 1);
            RigEquip(game, next, "shopping-spree");
            RigEquip(game, next, "peddlin-pete");

            GameFlowTests.ApplyAndPass(game, new EndTurn { PlayerId = Active(game) });
            Assert.Equal(next, Active(game));
            Assert.Equal(1, game.State.BmOnlyActionsRemaining);

            var player = game.State.Players[next];
            player.Money = 50;
            // Burn both normal actions first: the BM buy must still work via Shopping Spree.
            GameFlowTests.ApplyAndPass(game, new DrawLemonCard { PlayerId = next });
            GameFlowTests.ApplyAndPass(game, new DrawLemonCard { PlayerId = next });

            int bmInstance = game.State.Market[0];
            var def = TestData.Db.BlackMarket(game.State.BlackMarketInstances[bmInstance].DefId);
            var buy = new BuyBlackMarket { PlayerId = next, MarketIndex = 0 };
            if (def.Target == EquipTarget.Stand)
            {
                buy.TargetStandInstanceId = player.Stands[0].InstanceId;
            }
            GameFlowTests.ApplyAndPass(game, buy);

            Assert.Equal(50 - (def.Cost - 1), player.Money); // Pete's $1 discount
            Assert.Equal(0, game.State.BmOnlyActionsRemaining);
        }

        // ---------------------------------------------------------- reactions

        [Fact]
        public void SwearJarChargesTantrumThrowers()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int a = Active(game);
            int b = Seat(game, 1);
            RigEquip(game, a, "swear-jar");
            int plan = GiveCard(game, a, "market-forecasting");
            int tantrum = GiveCard(game, b, "tantrum");
            game.State.Players[a].Money = 20;
            game.State.Players[b].Money = 20;

            game.Apply(new PlayLemonCard { PlayerId = a, CardInstanceId = plan });
            game.Apply(new RespondToWindow { PlayerId = b, CardInstanceId = tantrum });
            GameFlowTests.PassAll(game);

            // The tantrum still cancelled the plan, but B paid A $2 for it.
            Assert.Equal(22, game.State.Players[a].Money);
            Assert.Equal(18, game.State.Players[b].Money);
        }

        [Fact]
        public void InflatableDecoyDiscardsAnAttack()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "tag-youre-it", "im-rubber-youre-glue", "profit-share");
            int a = Active(game);
            int b = Seat(game, 1);
            int decoy = RigEquip(game, b, "inflatable-decoy");
            int attack = GiveCard(game, a, "hoa-violation");
            game.State.Players[a].Money = 20;
            game.State.Players[b].Money = 20;

            game.Apply(new PlayLemonCard { PlayerId = a, CardInstanceId = attack, TargetPlayerId = b });
            Assert.Contains(b, game.State.AwaitingResponse); // decoy makes B eligible
            game.Apply(new RespondToWindow { PlayerId = b, EquippedInstanceId = decoy });

            Assert.Equal(20, game.State.Players[a].Money); // attack never resolved
            Assert.Equal(20, game.State.Players[b].Money);
            Assert.Contains(decoy, game.State.BlackMarketDiscard);
            Assert.Contains(attack, game.State.LemonDiscard);
        }

        [Fact]
        public void BouncerLetsTheVictimCounterAttack()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "tag-youre-it", "im-rubber-youre-glue", "profit-share");
            int a = Active(game);
            int b = Seat(game, 1);
            RigEquip(game, b, "bouncer");
            int attack = GiveCard(game, a, "hoa-violation");
            int counter = GiveCard(game, b, "taxes");
            game.State.Players[a].Money = 20;
            game.State.Players[b].Money = 20;

            game.Apply(new PlayLemonCard { PlayerId = a, CardInstanceId = attack, TargetPlayerId = b });

            var decision = Assert.Single(game.State.PendingDecisions);
            Assert.Equal(DecisionKind.BouncerAttack, decision.Kind);
            Assert.Equal(b, decision.PlayerId);
            // Free counter-attack: Taxes steals $2 per stand (A has 2) = $4.
            game.Apply(new PlayLemonCard { PlayerId = b, CardInstanceId = counter, TargetPlayerId = a });
            GameFlowTests.PassAll(game);

            Assert.Equal(20 + 5 - 4, game.State.Players[a].Money);
            Assert.Equal(20 - 5 + 4, game.State.Players[b].Money);
        }

        // -------------------------------------------------------------- titles

        [Fact]
        public void PennyPincherIsClaimedOnTheFifthBargainStand()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int a = Active(game);
            var player = game.State.Players[a];
            player.Money = 100;
            game.State.FirstDibsRow.Clear();
            game.State.FirstDibsRow.Add("penny-pincher");

            // 2 from the draft; buy 2 more, end turn, buy the 5th next round.
            GameFlowTests.ApplyAndPass(game, new BuyStand { PlayerId = a, StandTypeId = "bargain" });
            GameFlowTests.ApplyAndPass(game, new BuyStand { PlayerId = a, StandTypeId = "bargain" });
            Assert.Empty(player.FirstDibsClaimed);

            for (int i = 0; i < game.State.Players.Count; i++)
            {
                GameFlowTests.ApplyAndPass(game, new EndTurn { PlayerId = Active(game) });
            }
            Assert.Equal(a, Active(game));
            player.Money = 100;
            GameFlowTests.ApplyAndPass(game, new BuyStand { PlayerId = a, StandTypeId = "bargain" });

            Assert.Contains("penny-pincher", player.FirstDibsClaimed);
            Assert.Empty(game.State.FirstDibsRow);
            Assert.Equal(1, player.InGameVictoryPoints);
        }

        [Fact]
        public void BigEarnerTriggersFromRollEarnings()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int a = Active(game);
            game.State.FirstDibsRow.Clear();
            game.State.FirstDibsRow.Add("big-earner");
            game.State.RollStats[a] = new RollStats { Earned = 9 };

            GameFlowTests.ApplyAndPass(game, new DrawLemonCard { PlayerId = a });

            Assert.Contains("big-earner", game.State.Players[a].FirstDibsClaimed);
        }

        [Fact]
        public void CrankyPantsCountsPileTantrums()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int a = Active(game);
            var player = game.State.Players[a];
            game.State.FirstDibsRow.Clear();
            game.State.FirstDibsRow.Add("cranky-pants");
            for (int i = 0; i < 3; i++)
            {
                int t = GiveCard(game, a, "tantrum");
                player.Hand.Remove(t);
                player.TantrumPile.Add(new TantrumRecord
                {
                    InstanceId = t,
                    GainSeq = game.State.NextTantrumGainSeq++,
                });
            }

            GameFlowTests.ApplyAndPass(game, new DrawLemonCard { PlayerId = a });

            Assert.Contains("cranky-pants", player.FirstDibsClaimed);
        }

        [Fact]
        public void LemonLordTitlesScoreAtGameEnd()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "out-of-stock");
            var s = game.State;
            int first = s.FirstPlayer;
            var player = s.Players[first];

            // Rig kept titles to verifiable ones: Friendly Fran (no tantrums) + Hoarder (10+ cards).
            player.LemonLordKept.Clear();
            player.LemonLordKept.AddRange(new[] { "friendly-fran", "hoarder" });
            while (player.Hand.Count < 10)
            {
                int id = s.LemonDeck.First(x => s.LemonInstances[x].DefId != "timeout");
                s.LemonDeck.Remove(id);
                player.Hand.Add(id);
            }
            // Other players keep nothing so the winner is unambiguous.
            foreach (var p in s.Players.Where(p => p.PlayerId != first))
            {
                p.LemonLordKept.Clear();
            }

            Assert.True(game.MeetsLemonLord(player, "friendly-fran"));
            Assert.True(game.MeetsLemonLord(player, "hoarder"));

            // Drive to game end: first player buys 3 Bragging Rights over 3 rounds.
            for (int round = 0; round < 3 && s.Stage != GameStage.Finished; round++)
            {
                for (int i = 0; i < s.Players.Count && s.Stage != GameStage.Finished; i++)
                {
                    var active = s.Players[s.ActivePlayer];
                    if (active.PlayerId == first)
                    {
                        active.Money = 100;
                        GameFlowTests.ApplyAndPass(game, new BuyBraggingRights { PlayerId = first });
                    }
                    if (s.Stage != GameStage.Finished)
                    {
                        GameFlowTests.ApplyAndPass(game, new EndTurn { PlayerId = s.ActivePlayer });
                    }
                }
            }

            Assert.Equal(GameStage.Finished, s.Stage);
            // 3 bragging rights + 2 fulfilled Lemon Lords = 5 VP; sole winner.
            Assert.Equal(new List<int> { first }, s.Winners);
        }
    }
}
