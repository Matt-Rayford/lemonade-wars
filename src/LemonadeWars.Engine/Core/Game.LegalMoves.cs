using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Legal move enumeration: every action a player could submit right now, fully
    /// parameterized so that each returned action Applies cleanly. This is the contract
    /// the UI (button states), AI opponents, and the fuzz tests all share.
    ///
    /// Combinatorial choices (multi-card discards, Rummage Sale subsets) are capped at a
    /// bounded sample; the cap trades exhaustive coverage for move-list size and is noted
    /// inline wherever it applies.
    /// </summary>
    public sealed partial class Game
    {
        /// <summary>Players the game is currently waiting on.</summary>
        public IReadOnlyList<int> ActingPlayers()
        {
            var acting = new List<int>();
            switch (State.Stage)
            {
                case GameStage.Finished:
                    return acting;
                case GameStage.ChoosingLemonLords:
                    acting.AddRange(State.Players
                        .Where(p => p.LemonLordKept.Count == 0)
                        .Select(p => p.PlayerId));
                    return acting;
            }

            if (State.PendingDecisions.Count > 0)
            {
                acting.AddRange(State.PendingDecisions.Select(d => d.PlayerId).Distinct());
                return acting;
            }
            if (State.AwaitingResponse.Count > 0)
            {
                acting.AddRange(State.AwaitingResponse);
                return acting;
            }
            if (State.Stage == GameStage.InitialBuys)
            {
                acting.Add(CurrentInitialBuyer);
                return acting;
            }
            acting.Add(State.ActivePlayer);
            return acting;
        }

        /// <summary>All legal actions for one player in the current state.</summary>
        public List<GameAction> LegalMovesFor(int playerId)
        {
            var moves = new List<GameAction>();
            var player = Player(playerId);

            if (State.Stage == GameStage.Finished)
            {
                return moves;
            }

            if (State.Stage == GameStage.ChoosingLemonLords)
            {
                if (player.LemonLordKept.Count == 0)
                {
                    var dealt = player.LemonLordDealt;
                    for (int i = 0; i < dealt.Count; i++)
                    {
                        for (int j = i + 1; j < dealt.Count; j++)
                        {
                            moves.Add(new ChooseLemonLords
                            {
                                PlayerId = playerId,
                                KeepTitleIds = new List<string> { dealt[i], dealt[j] },
                            });
                        }
                    }
                }
                return moves;
            }

            // Blocking decisions come first — nothing else is legal while they pend.
            // Each Apply handler answers the FIRST pending decision of its group, so only
            // enumerate for the first decision per group to keep move shapes aligned.
            if (State.PendingDecisions.Count > 0)
            {
                var seenGroups = new HashSet<string>();
                foreach (var decision in State.PendingDecisions.Where(d => d.PlayerId == playerId))
                {
                    if (seenGroups.Add(DecisionGroup(decision.Kind)))
                    {
                        AddDecisionMoves(moves, player, decision);
                    }
                }
                return moves;
            }

            // Open response window.
            if (State.AwaitingResponse.Contains(playerId))
            {
                AddWindowMoves(moves, player);
                return moves;
            }
            if (State.AwaitingResponse.Count > 0 || State.ResponseStack.Count > 0 ||
                State.PendingRoll != null || State.TheftQueue.Count > 0)
            {
                return moves; // interaction pending, but not on this player
            }

            if (State.Stage == GameStage.InitialBuys)
            {
                AddInitialBuyMoves(moves, player);
                return moves;
            }

            if (playerId == State.ActivePlayer && State.Phase == TurnPhase.Play)
            {
                AddTurnMoves(moves, player);
            }
            return moves;
        }

        // -------------------------------------------------------- decisions

        /// <summary>Decisions answered by the same action type share a group (FIFO within it).</summary>
        private static string DecisionGroup(DecisionKind kind)
        {
            switch (kind)
            {
                case DecisionKind.DiscardToHandLimit:
                case DecisionKind.WhiniestBabyDiscard:
                    return "discard";
                case DecisionKind.AbilityVictim:
                case DecisionKind.AbilityPickCard:
                case DecisionKind.AbilityGiveBack:
                case DecisionKind.AbilityDiscard:
                case DecisionKind.InnovationCopy:
                case DecisionKind.WordOfMouthStand:
                    return "ability";
                case DecisionKind.FreePlayOffer:
                case DecisionKind.ForcedPlay:
                case DecisionKind.BouncerAttack:
                    return "freeplay";
                default:
                    return kind.ToString();
            }
        }

        private void AddDecisionMoves(List<GameAction> moves, PlayerState player, PendingDecision decision)
        {
            switch (decision.Kind)
            {
                case DecisionKind.DiscardToHandLimit:
                case DecisionKind.WhiniestBabyDiscard:
                    foreach (var combo in CardCombos(
                        decision.EligibleCardIds ?? player.Hand, decision.RequiredCount))
                    {
                        moves.Add(new SubmitDiscard { PlayerId = player.PlayerId, InstanceIds = combo });
                    }
                    break;

                case DecisionKind.TimeoutFine:
                    moves.Add(BuildTimeoutPayment(player, decision.RequiredMoney));
                    break;

                case DecisionKind.AttackRetarget:
                {
                    var item = FindStackItem(decision.StackItemId);
                    if (item == null)
                    {
                        break;
                    }
                    var victim = Player(item.AttackTargetId!.Value);
                    foreach (int eq in AllEquipped(victim))
                    {
                        if (item.LemonDefId == "finders-keepers")
                        {
                            foreach (var (standId, replaceId) in DestinationsFor(player, EquippedDef(eq)))
                            {
                                moves.Add(new SubmitRetarget
                                {
                                    PlayerId = player.PlayerId,
                                    StackItemId = item.ItemId,
                                    TargetEquippedInstanceId = eq,
                                    EquipStandInstanceId = standId,
                                    EquipReplaceInstanceId = replaceId,
                                });
                            }
                        }
                        else
                        {
                            moves.Add(new SubmitRetarget
                            {
                                PlayerId = player.PlayerId,
                                StackItemId = item.ItemId,
                                TargetEquippedInstanceId = eq,
                            });
                        }
                    }
                    break;
                }

                case DecisionKind.FreePlayOffer:
                case DecisionKind.BouncerAttack:
                case DecisionKind.ForcedPlay:
                {
                    bool attacksOnly = decision.Kind == DecisionKind.BouncerAttack;
                    var candidates = decision.Kind == DecisionKind.ForcedPlay
                        ? new List<int> { decision.CardInstanceId!.Value }
                        : player.Hand.ToList();
                    bool anyPlay = false;
                    foreach (int cardId in candidates)
                    {
                        var def = Db.Lemon(State.LemonInstances[cardId].DefId);
                        if (def.Type != LemonCardType.Plan && def.Type != LemonCardType.Attack)
                        {
                            continue;
                        }
                        if (attacksOnly && def.Type != LemonCardType.Attack)
                        {
                            continue;
                        }
                        foreach (var play in PlayParamCombos(player, cardId, def))
                        {
                            moves.Add(play);
                            anyPlay = true;
                        }
                    }
                    // Optional offers are always skippable; a forced play is skippable only
                    // when the recovered card has no legal parameterization.
                    if (decision.Kind != DecisionKind.ForcedPlay || !anyPlay)
                    {
                        moves.Add(new SkipFreePlay { PlayerId = player.PlayerId });
                    }
                    break;
                }

                case DecisionKind.AbilityVictim:
                    foreach (var other in State.Players.Where(p => p.PlayerId != player.PlayerId))
                    {
                        moves.Add(new SubmitAbilityChoice
                        {
                            PlayerId = player.PlayerId,
                            TargetPlayerId = other.PlayerId,
                        });
                    }
                    break;

                case DecisionKind.AbilityPickCard:
                    foreach (int cardId in Player(decision.ChosenPlayerId!.Value).Hand)
                    {
                        moves.Add(new SubmitAbilityChoice
                        {
                            PlayerId = player.PlayerId,
                            CardInstanceIds = new List<int> { cardId },
                        });
                    }
                    break;

                case DecisionKind.AbilityGiveBack:
                    foreach (int cardId in player.Hand.Where(id => id != decision.StolenCardId))
                    {
                        moves.Add(new SubmitAbilityChoice
                        {
                            PlayerId = player.PlayerId,
                            CardInstanceIds = new List<int> { cardId },
                        });
                    }
                    break;

                case DecisionKind.AbilityDiscard:
                    foreach (var combo in CardCombos(player.Hand, decision.RequiredCount))
                    {
                        moves.Add(new SubmitAbilityChoice { PlayerId = player.PlayerId, CardInstanceIds = combo });
                    }
                    break;

                case DecisionKind.InnovationCopy:
                    foreach (int id in player.Turf.Equipped)
                    {
                        var def = EquippedDef(id);
                        if (id != decision.SourceInstanceId &&
                            def.Timing == EffectTiming.PowerPour && def.Id != "innovation")
                        {
                            moves.Add(new SubmitAbilityChoice { PlayerId = player.PlayerId, EquippedInstanceId = id });
                        }
                    }
                    break;

                case DecisionKind.WordOfMouthStand:
                    foreach (var stand in player.Stands)
                    {
                        moves.Add(new SubmitAbilityChoice
                        {
                            PlayerId = player.PlayerId,
                            StandInstanceId = stand.InstanceId,
                        });
                    }
                    break;
            }
        }

        /// <summary>Canonical Timeout payment: sell cheapest assets until the fine is covered.</summary>
        private SubmitTimeoutPayment BuildTimeoutPayment(PlayerState player, int fine)
        {
            var payment = new SubmitTimeoutPayment { PlayerId = player.PlayerId };
            int cash = player.Money;
            if (cash >= fine)
            {
                return payment;
            }

            var sellables = AllEquipped(player)
                .Select(id => (Id: id, Price: EquippedDef(id).Cost, IsStand: false))
                .Concat(player.Stands.Select(s =>
                    (Id: s.InstanceId, Price: Db.StandType(s.StandTypeId).BaseCost, IsStand: true)))
                .OrderBy(x => x.Price)
                .ToList();
            foreach (var asset in sellables)
            {
                if (cash >= fine)
                {
                    break;
                }
                cash += asset.Price;
                if (asset.IsStand)
                {
                    payment.SellStandInstanceIds.Add(asset.Id);
                }
                else
                {
                    payment.SellBmInstanceIds.Add(asset.Id);
                }
            }
            return payment; // if still short, the engine takes everything (bankrupt path)
        }

        // ---------------------------------------------------------- windows

        private void AddWindowMoves(List<GameAction> moves, PlayerState player)
        {
            moves.Add(new PassWindow { PlayerId = player.PlayerId });

            List<int> InHand(string defId) =>
                player.Hand.Where(id => State.LemonInstances[id].DefId == defId).Take(1).ToList();

            if (State.ResponseStack.Count > 0)
            {
                var top = State.ResponseStack[State.ResponseStack.Count - 1];
                if (top.OwnerId == player.PlayerId)
                {
                    return;
                }
                if (IsTantrummable(top))
                {
                    foreach (int id in InHand(TantrumId))
                    {
                        moves.Add(new RespondToWindow { PlayerId = player.PlayerId, CardInstanceId = id });
                    }
                }
                if (IsAttackItem(top))
                {
                    foreach (int id in InHand(TagId))
                    {
                        if (State.Players.Count == 2)
                        {
                            moves.Add(new RespondToWindow { PlayerId = player.PlayerId, CardInstanceId = id });
                            continue;
                        }
                        foreach (var target in State.Players)
                        {
                            if (target.PlayerId != top.OwnerId && target.PlayerId != top.AttackTargetId)
                            {
                                moves.Add(new RespondToWindow
                                {
                                    PlayerId = player.PlayerId,
                                    CardInstanceId = id,
                                    RedirectTargetId = target.PlayerId,
                                });
                            }
                        }
                    }
                    foreach (int id in InHand(RubberGlueId))
                    {
                        moves.Add(new RespondToWindow { PlayerId = player.PlayerId, CardInstanceId = id });
                    }
                    if (top.AttackTargetId == player.PlayerId)
                    {
                        // The decoy defends its owner only — targets of the attack.
                        foreach (int decoy in player.Turf.Equipped
                            .Where(id => EquippedDef(id).Id == "inflatable-decoy").Take(1))
                        {
                            moves.Add(new RespondToWindow { PlayerId = player.PlayerId, EquippedInstanceId = decoy });
                        }
                    }
                }
                return;
            }

            if (State.TheftQueue.Count > 0)
            {
                if (State.TheftQueue[0].VictimId == player.PlayerId)
                {
                    foreach (int id in InHand(ProfitShareId))
                    {
                        moves.Add(new RespondToWindow { PlayerId = player.PlayerId, CardInstanceId = id });
                    }
                }
                return;
            }

            if (State.PendingRoll != null)
            {
                foreach (int id in InHand(OutOfStockId))
                {
                    moves.Add(new RespondToWindow { PlayerId = player.PlayerId, CardInstanceId = id });
                }
                if (player.PlayerId == State.ActivePlayer)
                {
                    foreach (int id in player.Turf.Equipped)
                    {
                        if (RollAbilityIds.Contains(EquippedDef(id).Id) &&
                            !State.UsedTurnAbilities.Contains(id) &&
                            RollAbilityUsable(EquippedDef(id).Id))
                        {
                            moves.Add(new UseTurnAbility { PlayerId = player.PlayerId, EquippedInstanceId = id });
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------ setup draft

        private void AddInitialBuyMoves(List<GameAction> moves, PlayerState player)
        {
            if (player.PlayerId != CurrentInitialBuyer)
            {
                return;
            }
            if (!State.InitialBuyStandDone)
            {
                foreach (var standType in Db.StandTypes)
                {
                    if (State.StandSupply[standType.Id].Count == 0 ||
                        StandPrice(player.PlayerId, standType.Id) > player.Money)
                    {
                        continue;
                    }
                    for (int pos = 0; pos <= player.Stands.Count; pos++)
                    {
                        moves.Add(new InitialBuyStand
                        {
                            PlayerId = player.PlayerId,
                            StandTypeId = standType.Id,
                            InsertIndex = pos,
                        });
                    }
                }
                return;
            }

            moves.Add(new InitialBuyEnd { PlayerId = player.PlayerId });
            // Optional draft purchase: capped so the later mandatory Stand stays affordable.
            AddBlackMarketBuys(moves, player, SetupSpendingRoom(player));
            if (!State.MarketRefreshUsedThisTurn &&
                Db.Config.BlackMarketRefreshCost <= SetupSpendingRoom(player))
            {
                moves.Add(new RefreshMarket { PlayerId = player.PlayerId });
            }
        }

        // ------------------------------------------------------- turn moves

        private void AddTurnMoves(List<GameAction> moves, PlayerState player)
        {
            int playerId = player.PlayerId;
            moves.Add(new EndTurn { PlayerId = playerId });

            if (!State.MarketRefreshUsedThisTurn && player.Money >= Db.Config.BlackMarketRefreshCost)
            {
                moves.Add(new RefreshMarket { PlayerId = playerId });
            }

            // Liquid Energy is an activated free ability outside of roll windows.
            foreach (int id in player.Turf.Equipped)
            {
                if (EquippedDef(id).Id == "liquid-energy" && !State.UsedTurnAbilities.Contains(id))
                {
                    moves.Add(new UseTurnAbility { PlayerId = playerId, EquippedInstanceId = id });
                }
            }

            if (State.ActionsRemaining > 0)
            {
                moves.Add(new DrawLemonCard { PlayerId = playerId });

                foreach (var standType in Db.StandTypes)
                {
                    if (State.StandSupply[standType.Id].Count > 0 &&
                        StandPrice(playerId, standType.Id) <= player.Money)
                    {
                        for (int pos = 0; pos <= player.Stands.Count; pos++)
                        {
                            moves.Add(new BuyStand
                            {
                                PlayerId = playerId,
                                StandTypeId = standType.Id,
                                InsertIndex = pos,
                            });
                        }
                    }
                }

                if (!State.BraggingRightsBoughtThisTurn &&
                    State.BraggingRightsSold < Db.Supporting.BraggingRightsPrices.Count &&
                    Db.Supporting.BraggingRightsPrices[State.BraggingRightsSold] <= player.Money)
                {
                    moves.Add(new BuyBraggingRights { PlayerId = playerId });
                }

                foreach (int cardId in player.Hand.ToList())
                {
                    var def = Db.Lemon(State.LemonInstances[cardId].DefId);
                    if (def.Type == LemonCardType.Plan || def.Type == LemonCardType.Attack)
                    {
                        moves.AddRange(PlayParamCombos(player, cardId, def));
                    }
                }
            }

            if (State.ActionsRemaining > 0 || State.BmOnlyActionsRemaining > 0)
            {
                AddBlackMarketBuys(moves, player);
            }
        }

        private void AddBlackMarketBuys(List<GameAction> moves, PlayerState player, int? budget = null)
        {
            int spendable = budget ?? player.Money;
            for (int i = 0; i < State.Market.Count; i++)
            {
                var def = EquippedDef(State.Market[i]);
                if (BlackMarketPrice(player.PlayerId, def) > spendable)
                {
                    continue;
                }
                foreach (var (standId, replaceId) in DestinationsFor(player, def))
                {
                    moves.Add(new BuyBlackMarket
                    {
                        PlayerId = player.PlayerId,
                        MarketIndex = i,
                        TargetStandInstanceId = standId,
                        ReplaceInstanceId = replaceId,
                    });
                }
            }
        }

        // ------------------------------------------------- play param combos

        /// <summary>Every valid parameterization for playing a plan/attack from hand.</summary>
        private IEnumerable<PlayLemonCard> PlayParamCombos(PlayerState player, int cardId, LemonCardDef def)
        {
            PlayLemonCard Base() => new PlayLemonCard { PlayerId = player.PlayerId, CardInstanceId = cardId };
            var opponents = State.Players.Where(p => p.PlayerId != player.PlayerId).ToList();

            switch (def.Id)
            {
                case "automation":
                case "market-forecasting":
                case "night-shifts":
                    yield return Base();
                    break;

                case "apologize":
                    foreach (var record in player.TantrumPile)
                    {
                        var play = Base();
                        play.TantrumInstanceId = record.InstanceId;
                        yield return play;
                    }
                    break;

                case "blame-changer":
                    foreach (var record in player.TantrumPile)
                    {
                        foreach (var other in opponents)
                        {
                            var play = Base();
                            play.TantrumInstanceId = record.InstanceId;
                            play.TargetPlayerId = other.PlayerId;
                            yield return play;
                        }
                    }
                    break;

                case "connections":
                    for (int i = 0; i < State.Market.Count; i++)
                    {
                        foreach (var (standId, replaceId) in DestinationsFor(player, EquippedDef(State.Market[i])))
                        {
                            var play = Base();
                            play.MarketIndex = i;
                            play.EquipStandInstanceId = standId;
                            play.EquipReplaceInstanceId = replaceId;
                            yield return play;
                        }
                    }
                    break;

                case "doorbuster-sale":
                    foreach (var stand in player.Stands)
                    {
                        var play = Base();
                        play.TargetStandInstanceId = stand.InstanceId;
                        yield return play;
                    }
                    break;

                case "finders-keepers":
                    foreach (var victim in opponents)
                    {
                        foreach (int eq in AllEquipped(victim))
                        {
                            foreach (var (standId, replaceId) in DestinationsFor(player, EquippedDef(eq)))
                            {
                                var play = Base();
                                play.TargetPlayerId = victim.PlayerId;
                                play.TargetEquippedInstanceId = eq;
                                play.EquipStandInstanceId = standId;
                                play.EquipReplaceInstanceId = replaceId;
                                yield return play;
                            }
                        }
                    }
                    break;

                case "hoa-violation":
                case "sharing-is-caring":
                case "smear-campaign":
                case "taxes":
                case "trash-pandas":
                    foreach (var other in opponents)
                    {
                        var play = Base();
                        play.TargetPlayerId = other.PlayerId;
                        yield return play;
                    }
                    break;

                case "rebrand":
                    foreach (var stand in player.Stands)
                    {
                        foreach (var newType in Db.StandTypes)
                        {
                            if (newType.Id == stand.StandTypeId || State.StandSupply[newType.Id].Count == 0)
                            {
                                continue;
                            }
                            int overflow = System.Math.Max(0, stand.Equipped.Count - newType.UpgradeSlots);
                            foreach (var combo in CardCombos(stand.Equipped, overflow))
                            {
                                var play = Base();
                                play.TargetStandInstanceId = stand.InstanceId;
                                play.NewStandTypeId = newType.Id;
                                play.SelectedInstanceIds = combo;
                                yield return play;
                            }
                        }
                    }
                    break;

                case "reduce-and-reuse":
                {
                    // One instance per distinct def is enough — copies are interchangeable.
                    var seen = new HashSet<string>();
                    foreach (int bmId in State.BlackMarketDiscard)
                    {
                        var bmDef = EquippedDef(bmId);
                        if (!seen.Add(bmDef.Id))
                        {
                            continue;
                        }
                        foreach (var (standId, replaceId) in DestinationsFor(player, bmDef))
                        {
                            var play = Base();
                            play.DiscardedBmInstanceId = bmId;
                            play.EquipStandInstanceId = standId;
                            play.EquipReplaceInstanceId = replaceId;
                            yield return play;
                        }
                    }
                    break;
                }

                case "reverse-engineer":
                {
                    var draw = Base();
                    draw.DrawInstead = true;
                    yield return draw;

                    var seenDefs = new HashSet<string>();
                    foreach (int lemonId in State.LemonDiscard)
                    {
                        var lemonDef = Db.Lemon(State.LemonInstances[lemonId].DefId);
                        if (lemonDef.Type != LemonCardType.Plan && lemonDef.Type != LemonCardType.Attack)
                        {
                            continue;
                        }
                        if (!seenDefs.Add(lemonDef.Id))
                        {
                            continue;
                        }
                        var play = Base();
                        play.DiscardedLemonInstanceId = lemonId;
                        yield return play;
                    }
                    break;
                }

                case "rummage-sale":
                {
                    // Subsets of size 1-3, capped to keep the move list bounded.
                    var equips = AllEquipped(player).ToList();
                    int emitted = 0;
                    for (int size = 1; size <= 3 && emitted < 40; size++)
                    {
                        foreach (var combo in CardCombos(equips, size))
                        {
                            emitted++;
                            var play = Base();
                            play.SelectedInstanceIds = combo;
                            yield return play;
                            if (emitted >= 40)
                            {
                                break;
                            }
                        }
                    }
                    break;
                }

                case "steal-the-cashbox":
                    foreach (var victim in opponents.Where(v => v.Turf.TrapInstanceId == null))
                    {
                        var play = Base();
                        play.TargetPlayerId = victim.PlayerId;
                        yield return play;
                    }
                    break;

                case "thats-not-fair":
                    foreach (var victim in opponents)
                    {
                        foreach (int eq in AllEquipped(victim))
                        {
                            var play = Base();
                            play.TargetEquippedInstanceId = eq;
                            yield return play;
                        }
                    }
                    break;
            }
        }

        // ----------------------------------------------------------- helpers

        private IEnumerable<int> AllEquipped(PlayerState player) =>
            player.Turf.Equipped.Concat(player.Stands.SelectMany(s => s.Equipped));

        /// <summary>
        /// Valid equip destinations for a Black Market def on this player's board:
        /// (standInstanceId, replaceInstanceId) pairs; (null, x) targets the turf.
        /// </summary>
        private IEnumerable<(int? StandId, int? ReplaceId)> DestinationsFor(PlayerState player, BlackMarketCardDef def)
        {
            if (def.Target == EquipTarget.Stand)
            {
                // Free slots first: the top option (and a tie-breaking greedy bot)
                // should never silently trash an existing upgrade.
                foreach (var stand in player.Stands)
                {
                    if (stand.Equipped.Count < Db.StandType(stand.StandTypeId).UpgradeSlots)
                    {
                        yield return (stand.InstanceId, null);
                    }
                }
                foreach (var stand in player.Stands)
                {
                    if (stand.Equipped.Count >= Db.StandType(stand.StandTypeId).UpgradeSlots)
                    {
                        foreach (int replace in stand.Equipped)
                        {
                            yield return (stand.InstanceId, replace);
                        }
                    }
                }
            }
            else
            {
                if (player.Turf.Equipped.Count < Db.Turf.UpgradeSlots)
                {
                    yield return (null, null);
                }
                else
                {
                    foreach (int replace in player.Turf.Equipped)
                    {
                        yield return (null, replace);
                    }
                }
            }
        }

        /// <summary>
        /// k-card combinations from a zone. Exhaustive when small; otherwise a deterministic
        /// sliding-window sample so the move list stays bounded.
        /// </summary>
        private static List<List<int>> CardCombos(IReadOnlyList<int> cards, int k)
        {
            var results = new List<List<int>>();
            if (k <= 0)
            {
                results.Add(new List<int>());
                return results;
            }
            if (k > cards.Count)
            {
                return results;
            }

            long total = 1;
            for (int i = 0; i < k; i++)
            {
                total = total * (cards.Count - i) / (i + 1);
            }
            if (total <= 30)
            {
                var combo = new int[k];
                void Recurse(int start, int depth)
                {
                    if (depth == k)
                    {
                        results.Add(combo.ToList());
                        return;
                    }
                    for (int i = start; i <= cards.Count - (k - depth); i++)
                    {
                        combo[depth] = cards[i];
                        Recurse(i + 1, depth + 1);
                    }
                }
                Recurse(0, 0);
            }
            else
            {
                // Sample: contiguous windows across the zone.
                for (int start = 0; start + k <= cards.Count && results.Count < 10; start += k)
                {
                    results.Add(cards.Skip(start).Take(k).ToList());
                }
            }
            return results;
        }
    }
}
