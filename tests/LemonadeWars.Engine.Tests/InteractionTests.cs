using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using Xunit;

namespace LemonadeWars.Engine.Tests
{
    /// <summary>
    /// The tantrum stack, response windows, and card effects. Scenarios rig hands
    /// deterministically by moving specific card instances between zones.
    /// </summary>
    public class InteractionTests
    {
        private static readonly string[] FourPlayers = { "Ana", "Ben", "Cal", "Dee" };

        private static Game ReadyToPlay(ulong seed = 7, string[]? names = null)
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

        /// <summary>Move one instance of a lemon def into the player's hand, from wherever it is.</summary>
        private static int GiveCard(Game game, int playerId, string defId)
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
            return found;
        }

        /// <summary>Remove every copy of the given defs from all hands (buried at the deck bottom).</summary>
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

        /// <summary>Move both Timeout cards to the bottom of the deck so rigged draws are safe.</summary>
        private static void BuryTimeouts(Game game)
        {
            var s = game.State;
            var timeouts = s.LemonDeck.Where(id => s.LemonInstances[id].DefId == "timeout").ToList();
            foreach (int id in timeouts)
            {
                s.LemonDeck.Remove(id);
                s.LemonDeck.Add(id);
            }
        }

        /// <summary>Pass open windows only — leaves pending decisions for the test to answer.</summary>
        private static void PassWindowsOnly(Game game)
        {
            while (game.State.AwaitingResponse.Count > 0)
            {
                game.Apply(new PassWindow { PlayerId = game.State.AwaitingResponse[0] });
            }
        }

        /// <summary>Equip a Black Market card instance straight onto a player's turf (test rig).</summary>
        private static int RigTurfEquip(Game game, int playerId)
        {
            var s = game.State;
            int id = s.BlackMarketDeck[0];
            s.BlackMarketDeck.RemoveAt(0);
            s.Players[playerId].Turf.Equipped.Add(id);
            return id;
        }

        private static int Active(Game game) => game.State.ActivePlayer;

        /// <summary>The player {offset} seats after the active player.</summary>
        private static int Seat(Game game, int offset) =>
            (game.State.ActivePlayer + offset) % game.State.Players.Count;

        // ---------------------------------------------------- basic contests

        [Fact]
        public void UncontestedPlanResolvesSynchronously()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int a = Active(game);
            int card = GiveCard(game, a, "automation");

            game.Apply(new PlayLemonCard { PlayerId = a, CardInstanceId = card });

            Assert.Empty(game.State.ResponseStack);
            Assert.Empty(game.State.AwaitingResponse);
            Assert.Equal(3, game.State.ActionsRemaining); // 2 - 1 action + 2 from Automation
            Assert.Contains(card, game.State.LemonDiscard);
        }

        [Fact]
        public void TantrumCancelsPlayAndAssignsWhiniestBaby()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int a = Active(game);
            int b = Seat(game, 1);
            int plan = GiveCard(game, a, "market-forecasting");
            int tantrum = GiveCard(game, b, "tantrum");
            int handBefore = game.State.Players[a].Hand.Count;

            game.Apply(new PlayLemonCard { PlayerId = a, CardInstanceId = plan });
            Assert.Equal(new[] { b }, game.State.AwaitingResponse);

            game.Apply(new RespondToWindow { PlayerId = b, CardInstanceId = tantrum });

            Assert.Empty(game.State.ResponseStack);
            // Cancelled: no cards drawn, plan discarded, action still spent.
            Assert.Equal(handBefore - 1, game.State.Players[a].Hand.Count);
            Assert.Contains(plan, game.State.LemonDiscard);
            Assert.Equal(1, game.State.ActionsRemaining);
            // The tantrum is in B's pile and B is the Whiniest Baby.
            Assert.Single(game.State.Players[b].TantrumPile);
            Assert.Equal(b, game.State.WhiniestBabyHolder);
        }

        [Fact]
        public void CounterTantrumLetsThePlayResolve()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            BuryTimeouts(game);
            int a = Active(game);
            int b = Seat(game, 1);
            int plan = GiveCard(game, a, "market-forecasting");
            int tantrumB = GiveCard(game, b, "tantrum");
            int tantrumA = GiveCard(game, a, "tantrum");
            int handBefore = game.State.Players[a].Hand.Count;

            game.Apply(new PlayLemonCard { PlayerId = a, CardInstanceId = plan });
            game.Apply(new RespondToWindow { PlayerId = b, CardInstanceId = tantrumB });
            // A may tantrum B's tantrum; the window is now on B's tantrum.
            game.Apply(new RespondToWindow { PlayerId = a, CardInstanceId = tantrumA });

            // Chain: A's tantrum cancels B's; Market Forecasting resolves (draw 3).
            // Hand: -plan -tantrumA +3 draws.
            Assert.Equal(handBefore - 2 + 3, game.State.Players[a].Hand.Count);
            // Both tantrums were gained; tie at 1 each -> most recent gain (A) takes the baby.
            Assert.Single(game.State.Players[a].TantrumPile);
            Assert.Single(game.State.Players[b].TantrumPile);
            Assert.Equal(a, game.State.WhiniestBabyHolder);
        }

        [Fact]
        public void CancelledPurchaseRefundsMoneyButNotAction()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int a = Active(game);
            int b = Seat(game, 1);
            int tantrum = GiveCard(game, b, "tantrum");
            var player = game.State.Players[a];
            player.Money = 50;

            int bmInstance = game.State.Market[0];
            var def = TestData.Db.BlackMarket(game.State.BlackMarketInstances[bmInstance].DefId);
            var buy = new BuyBlackMarket { PlayerId = a, MarketIndex = 0 };
            if (def.Target == LemonadeWars.Engine.Data.EquipTarget.Stand)
            {
                buy.TargetStandInstanceId = player.Stands[0].InstanceId;
            }

            game.Apply(buy);
            Assert.Equal(50 - def.Cost, player.Money); // paid up front
            game.Apply(new RespondToWindow { PlayerId = b, CardInstanceId = tantrum });

            Assert.Equal(50, player.Money);                     // refunded
            Assert.Equal(1, game.State.ActionsRemaining);       // action NOT refunded
            Assert.Contains(bmInstance, game.State.BlackMarketDiscard);
            Assert.Empty(player.Stands[0].Equipped);
            Assert.Empty(player.Turf.Equipped);
        }

        [Fact]
        public void CannotTantrumYourOwnCardOrRespondUninvited()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int a = Active(game);
            int b = Seat(game, 1);
            int plan = GiveCard(game, a, "market-forecasting");
            int ownTantrum = GiveCard(game, a, "tantrum");
            GiveCard(game, b, "tantrum");

            game.Apply(new PlayLemonCard { PlayerId = a, CardInstanceId = plan });
            // A is not in the awaiting list for their own play.
            Assert.DoesNotContain(a, game.State.AwaitingResponse);
            Assert.Throws<InvalidActionException>(() =>
                game.Apply(new RespondToWindow { PlayerId = a, CardInstanceId = ownTantrum }));
            GameFlowTests.PassAll(game);
        }

        [Fact]
        public void ApologizeCannotBeTantrummedAndClearsATantrum()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int a = Active(game);
            int b = Seat(game, 1);
            GiveCard(game, b, "tantrum"); // B is armed, but Apologize is exempt

            // Rig: A already has a tantrum in their pile.
            int pileTantrum = GiveCard(game, a, "tantrum");
            game.State.Players[a].Hand.Remove(pileTantrum);
            game.State.Players[a].TantrumPile.Add(new TantrumRecord
            {
                InstanceId = pileTantrum,
                GainSeq = game.State.NextTantrumGainSeq++,
            });

            int apologize = GiveCard(game, a, "apologize");
            game.Apply(new PlayLemonCard
            {
                PlayerId = a,
                CardInstanceId = apologize,
                TantrumInstanceId = pileTantrum,
            });

            // No window opened — resolved instantly despite B holding a tantrum.
            Assert.Empty(game.State.AwaitingResponse);
            Assert.Empty(game.State.Players[a].TantrumPile);
            Assert.Contains(pileTantrum, game.State.LemonDiscard);
        }

        // -------------------------------------------------- attack reactions

        [Fact]
        public void HoaViolationStealsFive()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "tag-youre-it", "im-rubber-youre-glue", "profit-share");
            int a = Active(game);
            int b = Seat(game, 1);
            int card = GiveCard(game, a, "hoa-violation");
            game.State.Players[a].Money = 20;
            game.State.Players[b].Money = 20;

            game.Apply(new PlayLemonCard { PlayerId = a, CardInstanceId = card, TargetPlayerId = b });

            Assert.Equal(25, game.State.Players[a].Money);
            Assert.Equal(15, game.State.Players[b].Money);
        }

        [Fact]
        public void TagMovesTheAttackToAnotherPlayer()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "tag-youre-it", "im-rubber-youre-glue", "profit-share");
            int a = Active(game);
            int b = Seat(game, 1);
            int c = Seat(game, 2);
            int d = Seat(game, 3);
            int attack = GiveCard(game, a, "hoa-violation");
            int tag = GiveCard(game, c, "tag-youre-it");
            foreach (var p in game.State.Players)
            {
                p.Money = 20;
            }

            game.Apply(new PlayLemonCard { PlayerId = a, CardInstanceId = attack, TargetPlayerId = b });
            // The attack cannot be tagged onto the attacker (card text).
            Assert.Throws<InvalidActionException>(() => game.Apply(
                new RespondToWindow { PlayerId = c, CardInstanceId = tag, RedirectTargetId = a }));
            game.Apply(new RespondToWindow { PlayerId = c, CardInstanceId = tag, RedirectTargetId = d });
            GameFlowTests.PassAll(game);

            Assert.Equal(25, game.State.Players[a].Money);
            Assert.Equal(20, game.State.Players[b].Money); // dodged
            Assert.Equal(15, game.State.Players[d].Money); // tagged
        }

        [Fact]
        public void RubberGlueReflectsTheAttack()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "tag-youre-it", "im-rubber-youre-glue", "profit-share");
            int a = Active(game);
            int b = Seat(game, 1);
            int attack = GiveCard(game, a, "hoa-violation");
            int rubber = GiveCard(game, b, "im-rubber-youre-glue");
            game.State.Players[a].Money = 20;
            game.State.Players[b].Money = 20;

            game.Apply(new PlayLemonCard { PlayerId = a, CardInstanceId = attack, TargetPlayerId = b });
            game.Apply(new RespondToWindow { PlayerId = b, CardInstanceId = rubber });
            GameFlowTests.PassAll(game);

            // Attack reflected: A pays B $5.
            Assert.Equal(15, game.State.Players[a].Money);
            Assert.Equal(25, game.State.Players[b].Money);
        }

        [Fact]
        public void TwoPlayerTagDiscardsTheAttack()
        {
            var game = ReadyToPlay(names: new[] { "Ana", "Ben" });
            StripHands(game, "tantrum", "tag-youre-it", "im-rubber-youre-glue", "profit-share");
            int a = Active(game);
            int b = Seat(game, 1);
            int attack = GiveCard(game, a, "hoa-violation");
            int tag = GiveCard(game, b, "tag-youre-it");
            game.State.Players[a].Money = 20;
            game.State.Players[b].Money = 20;

            game.Apply(new PlayLemonCard { PlayerId = a, CardInstanceId = attack, TargetPlayerId = b });
            game.Apply(new RespondToWindow { PlayerId = b, CardInstanceId = tag });
            GameFlowTests.PassAll(game);

            // 2-player rule: the attack is discarded instead of moved.
            Assert.Equal(20, game.State.Players[a].Money);
            Assert.Equal(20, game.State.Players[b].Money);
            Assert.Contains(attack, game.State.LemonDiscard);
        }

        [Fact]
        public void ProfitShareRecoversHalfRoundedUp()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "tag-youre-it", "im-rubber-youre-glue", "profit-share");
            int a = Active(game);
            int b = Seat(game, 1);
            int attack = GiveCard(game, a, "sharing-is-caring");
            int share = GiveCard(game, b, "profit-share");
            game.State.Players[a].Money = 20;
            game.State.Players[b].Money = 17; // steal ceil(17/2) = 9

            game.Apply(new PlayLemonCard { PlayerId = a, CardInstanceId = attack, TargetPlayerId = b });
            // Theft resolved; victim's Profit Share window is open.
            Assert.Equal(new[] { b }, game.State.AwaitingResponse);
            game.Apply(new RespondToWindow { PlayerId = b, CardInstanceId = share });

            // B recovers ceil(9/2) = 5: A = 20 + 9 - 5 = 24, B = 17 - 9 + 5 = 13.
            Assert.Equal(24, game.State.Players[a].Money);
            Assert.Equal(13, game.State.Players[b].Money);
        }

        [Fact]
        public void TaggedThatsNotFairRequiresRetarget()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "tag-youre-it", "im-rubber-youre-glue", "profit-share");
            int a = Active(game);
            int b = Seat(game, 1);
            int c = Seat(game, 2);
            int d = Seat(game, 3);
            int equippedB = RigTurfEquip(game, b);
            int equippedD = RigTurfEquip(game, d);
            int attack = GiveCard(game, a, "thats-not-fair");
            int tag = GiveCard(game, c, "tag-youre-it");

            game.Apply(new PlayLemonCard
            {
                PlayerId = a,
                CardInstanceId = attack,
                TargetEquippedInstanceId = equippedB,
            });
            game.Apply(new RespondToWindow { PlayerId = c, CardInstanceId = tag, RedirectTargetId = d });
            PassWindowsOnly(game);

            // Attack now aims at D but its chosen card belonged to B: attacker re-picks.
            var decision = Assert.Single(game.State.PendingDecisions);
            Assert.Equal(DecisionKind.AttackRetarget, decision.Kind);
            Assert.Equal(a, decision.PlayerId);

            game.Apply(new SubmitRetarget
            {
                PlayerId = a,
                StackItemId = decision.StackItemId!.Value,
                TargetEquippedInstanceId = equippedD,
            });

            Assert.Contains(equippedD, game.State.BlackMarketDiscard);   // D's card destroyed
            Assert.Contains(equippedB, game.State.Players[b].Turf.Equipped); // B's survived
        }

        [Fact]
        public void FindersKeepersStealsAnEquippedCard()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "tag-youre-it", "im-rubber-youre-glue", "profit-share");
            int a = Active(game);
            int b = Seat(game, 1);
            int equippedB = RigTurfEquip(game, b);
            bool turfCard = TestData.Db
                .BlackMarket(game.State.BlackMarketInstances[equippedB].DefId).Target
                == LemonadeWars.Engine.Data.EquipTarget.Turf;
            int attack = GiveCard(game, a, "finders-keepers");

            game.Apply(new PlayLemonCard
            {
                PlayerId = a,
                CardInstanceId = attack,
                TargetPlayerId = b,
                TargetEquippedInstanceId = equippedB,
                EquipStandInstanceId = turfCard ? (int?)null : game.State.Players[a].Stands[0].InstanceId,
            });

            Assert.DoesNotContain(equippedB, game.State.Players[b].Turf.Equipped);
            bool onOwnBoard = game.State.Players[a].Turf.Equipped.Contains(equippedB) ||
                game.State.Players[a].Stands.Any(s => s.Equipped.Contains(equippedB));
            Assert.True(onOwnBoard);
        }

        // ------------------------------------------------------- roll windows

        [Fact]
        public void OutOfStockRerollsTheSaleDie()
        {
            var game = ReadyToPlay();
            StripHands(game, "out-of-stock");
            int a = Active(game);
            int b = Seat(game, 1);
            int reroll = GiveCard(game, b, "out-of-stock");

            var events = new List<GameEvent>();
            events.AddRange(game.Apply(new EndTurn { PlayerId = a }));
            Assert.NotNull(game.State.PendingRoll);
            int firstRoll = game.State.PendingRoll!.Value;

            events.AddRange(game.Apply(new RespondToWindow { PlayerId = b, CardInstanceId = reroll }));
            while (game.State.AwaitingResponse.Count > 0)
            {
                events.AddRange(game.Apply(new PassWindow
                {
                    PlayerId = game.State.AwaitingResponse[0],
                }));
            }

            var rerolled = events.OfType<DieRerolled>().Single();
            Assert.Equal(b, rerolled.ByPlayerId);
            // The sale applied and the turn moved on.
            Assert.Null(game.State.PendingRoll);
            Assert.NotEqual(a, game.State.ActivePlayer);
            Assert.Contains(reroll, game.State.LemonDiscard);
        }

        [Fact]
        public void NightShiftsSellsForTheRollerOnly()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "out-of-stock");
            int a = Active(game);
            int card = GiveCard(game, a, "night-shifts");
            var moneyBefore = game.State.Players.Select(p => p.Money).ToList();

            var events = game.Apply(new PlayLemonCard { PlayerId = a, CardInstanceId = card }).ToList();
            int roll = events.OfType<SaleRolled>().Single().Value;

            // Everyone drafted 2 bargain stands: roller earns on 1-3, others never do.
            var active = game.State.Players[a];
            int expected = moneyBefore[a]
                + (roll <= 3 ? 2 : 0)
                + (active.Turf.PowerPourNumber == roll ? 1 : 0);
            Assert.Equal(expected, active.Money);
            foreach (var p in game.State.Players.Where(p => p.PlayerId != a))
            {
                Assert.Equal(moneyBefore[p.PlayerId], p.Money);
            }
        }

        [Fact]
        public void StealTheCashboxSkimsTheNextSale()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "tag-youre-it", "im-rubber-youre-glue",
                "profit-share", "out-of-stock");
            int a = Active(game);
            int b = Seat(game, 1);
            int trap = GiveCard(game, a, "steal-the-cashbox");
            foreach (var p in game.State.Players)
            {
                p.Money = 20;
            }

            game.Apply(new PlayLemonCard { PlayerId = a, CardInstanceId = trap, TargetPlayerId = b });
            Assert.Equal(trap, game.State.Players[b].Turf.TrapInstanceId);

            var events = game.Apply(new EndTurn { PlayerId = a }).ToList();
            int roll = events.OfType<SaleRolled>().Single().Value;

            var bState = game.State.Players[b];
            int bEarned = (roll <= 3 ? 2 : 0) + (bState.Turf.PowerPourNumber == roll ? 1 : 0);
            // Trap is spent either way; B's earnings (up to $10) went to A.
            Assert.Null(bState.Turf.TrapInstanceId);
            Assert.Contains(trap, game.State.LemonDiscard);
            Assert.Equal(20, bState.Money);
            int aEarned = (roll <= 3 ? 2 : 0) +
                (game.State.Players[a].Turf.PowerPourNumber == roll ? 1 : 0);
            Assert.Equal(20 + aEarned + bEarned, game.State.Players[a].Money);
        }

        // ------------------------------------------------------ card effects

        [Fact]
        public void SmearCampaignStealsTwoAndOffersAFreePlay()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "tag-youre-it", "im-rubber-youre-glue", "profit-share");
            int a = Active(game);
            int b = Seat(game, 1);
            int attack = GiveCard(game, a, "smear-campaign");
            int aHand = game.State.Players[a].Hand.Count;
            int bHand = game.State.Players[b].Hand.Count;

            game.Apply(new PlayLemonCard { PlayerId = a, CardInstanceId = attack, TargetPlayerId = b });

            Assert.Equal(aHand - 1 + 2, game.State.Players[a].Hand.Count);
            Assert.Equal(bHand - 2, game.State.Players[b].Hand.Count);

            var decision = Assert.Single(game.State.PendingDecisions);
            Assert.Equal(DecisionKind.FreePlayOffer, decision.Kind);
            game.Apply(new SkipFreePlay { PlayerId = a });
            Assert.Empty(game.State.PendingDecisions);
            Assert.Equal(1, game.State.ActionsRemaining);
        }

        [Fact]
        public void BlameChangerHandsOverATantrumAndTheBaby()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "tag-youre-it", "im-rubber-youre-glue");
            int a = Active(game);
            int b = Seat(game, 1);

            int pileTantrum = GiveCard(game, a, "tantrum");
            game.State.Players[a].Hand.Remove(pileTantrum);
            game.State.Players[a].TantrumPile.Add(new TantrumRecord
            {
                InstanceId = pileTantrum,
                GainSeq = game.State.NextTantrumGainSeq++,
            });
            game.State.WhiniestBabyHolder = a;

            int card = GiveCard(game, a, "blame-changer");
            game.Apply(new PlayLemonCard
            {
                PlayerId = a,
                CardInstanceId = card,
                TargetPlayerId = b,
                TantrumInstanceId = pileTantrum,
            });

            Assert.Empty(game.State.Players[a].TantrumPile);
            Assert.Single(game.State.Players[b].TantrumPile);
            Assert.Equal(b, game.State.WhiniestBabyHolder);
        }

        [Fact]
        public void TrashPandasSwapsHands()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "tag-youre-it", "im-rubber-youre-glue", "profit-share");
            int a = Active(game);
            int b = Seat(game, 1);
            int card = GiveCard(game, a, "trash-pandas");

            var aCards = game.State.Players[a].Hand.Where(id => id != card).ToList();
            var bCards = game.State.Players[b].Hand.ToList();

            game.Apply(new PlayLemonCard { PlayerId = a, CardInstanceId = card, TargetPlayerId = b });

            Assert.Equal(bCards, game.State.Players[a].Hand);
            Assert.Equal(aCards, game.State.Players[b].Hand);
        }

        [Fact]
        public void RummageSaleSellsEquipsForHalfRoundedUp()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int a = Active(game);
            int equipped = RigTurfEquip(game, a);
            int cost = TestData.Db.BlackMarket(game.State.BlackMarketInstances[equipped].DefId).Cost;
            int card = GiveCard(game, a, "rummage-sale");
            int moneyBefore = game.State.Players[a].Money;

            game.Apply(new PlayLemonCard
            {
                PlayerId = a,
                CardInstanceId = card,
                SelectedInstanceIds = new List<int> { equipped },
            });

            Assert.Equal(moneyBefore + (cost + 1) / 2, game.State.Players[a].Money);
            Assert.Contains(equipped, game.State.BlackMarketDiscard);
        }

        [Fact]
        public void ReverseEngineerForcesThePlayOfARecoveredCard()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int a = Active(game);

            // Rig the discard with an Automation.
            int automation = GiveCard(game, a, "automation");
            game.State.Players[a].Hand.Remove(automation);
            game.State.LemonDiscard.Add(automation);

            int card = GiveCard(game, a, "reverse-engineer");
            game.Apply(new PlayLemonCard
            {
                PlayerId = a,
                CardInstanceId = card,
                DiscardedLemonInstanceId = automation,
            });

            var decision = Assert.Single(game.State.PendingDecisions);
            Assert.Equal(DecisionKind.ForcedPlay, decision.Kind);
            // The wrong card is rejected; the recovered card must be played.
            Assert.Throws<InvalidActionException>(() => game.Apply(new PlayLemonCard
            {
                PlayerId = a,
                CardInstanceId = game.State.Players[a].Hand.First(id => id != automation),
            }));
            game.Apply(new PlayLemonCard { PlayerId = a, CardInstanceId = automation });

            // Reverse Engineer cost the action; Automation added 2: 2 - 1 + 2 = 3.
            Assert.Equal(3, game.State.ActionsRemaining);
        }

        // ------------------------------------------------------ status cards

        [Fact]
        public void TimeoutFinesTheBabyAndPassesTheCard()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum");
            int a = Active(game);
            int b = Seat(game, 1);
            int c = Seat(game, 2);
            var s = game.State;

            // Rig: B is the baby with 2 tantrums and little money; C has 1 tantrum.
            foreach (var (player, count) in new[] { (b, 2), (c, 1) })
            {
                for (int i = 0; i < count; i++)
                {
                    int t = GiveCard(game, player, "tantrum");
                    s.Players[player].Hand.Remove(t);
                    s.Players[player].TantrumPile.Add(new TantrumRecord
                    {
                        InstanceId = t,
                        GainSeq = s.NextTantrumGainSeq++,
                    });
                }
            }
            s.WhiniestBabyHolder = b;
            s.Players[b].Money = 4; // fine is $6: must sell something

            // Rig a Timeout on top of the deck and draw it.
            int timeout = s.LemonDeck.First(id => s.LemonInstances[id].DefId == "timeout");
            s.LemonDeck.Remove(timeout);
            s.LemonDeck.Insert(0, timeout);
            game.Apply(new DrawLemonCard { PlayerId = a });

            var fine = Assert.Single(s.PendingDecisions);
            Assert.Equal(DecisionKind.TimeoutFine, fine.Kind);
            Assert.Equal(6, fine.RequiredMoney);

            // Selling with nothing listed can't cover it.
            Assert.Throws<InvalidActionException>(() =>
                game.Apply(new SubmitTimeoutPayment { PlayerId = b }));

            // Sell a bargain stand (+$2) to cover the $6.
            game.Apply(new SubmitTimeoutPayment
            {
                PlayerId = b,
                SellStandInstanceIds = new List<int> { s.Players[b].Stands[0].InstanceId },
            });

            Assert.Equal(0, s.Players[b].Money);              // 4 + 2 - 6
            Assert.Single(s.Players[b].Stands);               // sold one of two
            Assert.Empty(s.Players[b].TantrumPile);           // tantrums discarded
            Assert.Equal(c, s.WhiniestBabyHolder);            // passed to next-most tantrums
            Assert.Contains(timeout, s.LemonDiscard);
        }

        [Fact]
        public void WhiniestBabyDrawsTwoAndDiscardsOne()
        {
            var game = ReadyToPlay();
            StripHands(game, "tantrum", "out-of-stock");
            int a = Active(game);
            int next = Seat(game, 1);
            game.State.WhiniestBabyHolder = next;

            GameFlowTests.ApplyAndPass(game, new EndTurn { PlayerId = a });

            // The baby's turn-start forced a draw-2-discard-1 (handled by PassAll's default);
            // net hand change is +1, same as a normal draw.
            Assert.Equal(next, game.State.ActivePlayer);
            Assert.Equal(TurnPhase.Play, game.State.Phase);
        }

        [Fact]
        public void SpoiledRottenGoesToSoleLastAndGrantsAFreeRoll()
        {
            var game = ReadyToPlay(names: new[] { "Ana", "Ben" });
            StripHands(game, "tantrum", "out-of-stock");
            int a = Active(game);
            int b = Seat(game, 1);
            game.State.Players[a].Money = 50;

            game.Apply(new BuyBraggingRights { PlayerId = a });
            // 2-player: B is now sole last and takes Spoiled Rotten.
            Assert.Equal(b, game.State.SpoiledRottenHolder);

            var events = new List<GameEvent>();
            events.AddRange(game.Apply(new EndTurn { PlayerId = a }));
            while (game.State.AwaitingResponse.Count > 0)
            {
                events.AddRange(game.Apply(new PassWindow
                {
                    PlayerId = game.State.AwaitingResponse[0],
                }));
            }

            // B's turn began with a free personal sale roll: two SaleRolled events,
            // the second rolled by B before B's Play phase.
            var rolls = events.OfType<SaleRolled>().ToList();
            Assert.Equal(2, rolls.Count);
            Assert.Equal(a, rolls[0].PlayerId);
            Assert.Equal(b, rolls[1].PlayerId);
            Assert.Equal(b, game.State.ActivePlayer);
            Assert.Equal(TurnPhase.Play, game.State.Phase);
        }

        // ------------------------------------------------------- determinism

        [Fact]
        public void ContestedGamesAreDeterministic()
        {
            string Run()
            {
                var game = ReadyToPlay(99);
                StripHands(game, "tantrum");
                int a = Active(game);
                int b = Seat(game, 1);
                int plan = GiveCard(game, a, "market-forecasting");
                int tantrumB = GiveCard(game, b, "tantrum");
                int tantrumA = GiveCard(game, a, "tantrum");
                game.Apply(new PlayLemonCard { PlayerId = a, CardInstanceId = plan });
                game.Apply(new RespondToWindow { PlayerId = b, CardInstanceId = tantrumB });
                game.Apply(new RespondToWindow { PlayerId = a, CardInstanceId = tantrumA });
                GameFlowTests.ApplyAndPass(game, new EndTurn { PlayerId = a });
                return game.SnapshotJson();
            }

            Assert.Equal(Run(), Run());
        }
    }
}
