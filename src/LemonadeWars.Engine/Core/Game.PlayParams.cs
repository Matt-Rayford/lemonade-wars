using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Per-card play parameterizations, shared by move enumeration, forced-play validation and
    /// attack retargeting. The order plays are yielded in is a contract: bots break ties by it
    /// and the client lists options in it. Combinatorial choices (multi-card discards, Rummage
    /// Sale subsets) are capped at a bounded sample.
    /// </summary>
    public sealed partial class Game
    {
        private const int RummageSaleMaxCards = 3;
        private const int RummageSaleMoveCap = 40;

        private const int ExhaustiveComboLimit = 30;
        private const int SampledComboLimit = 10;

        private IEnumerable<PlayLemonCard> PlayParamCombos(PlayerState player, int cardId, LemonCardDef def)
        {
            switch (def.Id)
            {
                case LemonCardIds.Automation:
                case LemonCardIds.MarketForecasting:
                case LemonCardIds.NightShifts:
                    return new[] { NewPlay(player, cardId) };

                case LemonCardIds.Apologize: return ApologizePlays(player, cardId);
                case LemonCardIds.BlameChanger: return BlameChangerPlays(player, cardId);
                case LemonCardIds.Connections: return ConnectionsPlays(player, cardId);
                case LemonCardIds.DoorbusterSale: return DoorbusterSalePlays(player, cardId);
                case LemonCardIds.FindersKeepers: return FindersKeepersPlays(player, cardId);

                case LemonCardIds.HoaViolation:
                case LemonCardIds.SharingIsCaring:
                case LemonCardIds.SmearCampaign:
                case LemonCardIds.Taxes:
                case LemonCardIds.TrashPandas:
                    return PlaysTargetingEach(OpponentsOf(player), player, cardId);

                case LemonCardIds.Rebrand: return RebrandPlays(player, cardId);
                case LemonCardIds.ReduceAndReuse: return ReduceAndReusePlays(player, cardId);
                case LemonCardIds.ReverseEngineer: return ReverseEngineerPlays(player, cardId);
                case LemonCardIds.RummageSale: return RummageSalePlays(player, cardId);

                case LemonCardIds.StealTheCashbox:
                    return PlaysTargetingEach(
                        OpponentsOf(player).Where(victim => victim.Turf.TrapInstanceId == null), player, cardId);

                case LemonCardIds.ThatsNotFair: return ThatsNotFairPlays(player, cardId);

                default:
                    return Enumerable.Empty<PlayLemonCard>();
            }
        }

        // ---------------------------------------------------- per-card plays

        private static PlayLemonCard NewPlay(PlayerState player, int cardId) =>
            new PlayLemonCard { PlayerId = player.PlayerId, CardInstanceId = cardId };

        private IEnumerable<PlayerState> OpponentsOf(PlayerState player) =>
            State.Players.Where(p => p.PlayerId != player.PlayerId);

        private static IEnumerable<PlayLemonCard> PlaysTargetingEach(
            IEnumerable<PlayerState> victims, PlayerState player, int cardId)
        {
            foreach (var victim in victims)
            {
                var play = NewPlay(player, cardId);
                play.TargetPlayerId = victim.PlayerId;
                yield return play;
            }
        }

        /// <summary>Tantrum copies are interchangeable — one move, engine picks.</summary>
        private static IEnumerable<PlayLemonCard> ApologizePlays(PlayerState player, int cardId)
        {
            if (player.TantrumPile.Count > 0)
            {
                yield return NewPlay(player, cardId);
            }
        }

        private IEnumerable<PlayLemonCard> BlameChangerPlays(PlayerState player, int cardId)
        {
            if (player.TantrumPile.Count == 0)
            {
                yield break;
            }
            foreach (var play in PlaysTargetingEach(OpponentsOf(player), player, cardId))
            {
                yield return play;
            }
        }

        private IEnumerable<PlayLemonCard> ConnectionsPlays(PlayerState player, int cardId)
        {
            for (int marketIndex = 0; marketIndex < State.Market.Count; marketIndex++)
            {
                var marketDef = EquippedDef(State.Market[marketIndex]);
                foreach (var (standId, replaceId) in DestinationsFor(player, marketDef))
                {
                    var play = NewPlay(player, cardId);
                    play.MarketIndex = marketIndex;
                    play.EquipStandInstanceId = standId;
                    play.EquipReplaceInstanceId = replaceId;
                    yield return play;
                }
            }
        }

        private static IEnumerable<PlayLemonCard> DoorbusterSalePlays(PlayerState player, int cardId)
        {
            foreach (var stand in player.Stands)
            {
                var play = NewPlay(player, cardId);
                play.TargetStandInstanceId = stand.InstanceId;
                yield return play;
            }
        }

        private IEnumerable<PlayLemonCard> FindersKeepersPlays(PlayerState player, int cardId)
        {
            foreach (var victim in OpponentsOf(player))
            {
                foreach (int equippedId in AllEquipped(victim))
                {
                    foreach (var (standId, replaceId) in DestinationsFor(player, EquippedDef(equippedId)))
                    {
                        var play = NewPlay(player, cardId);
                        play.TargetPlayerId = victim.PlayerId;
                        play.TargetEquippedInstanceId = equippedId;
                        play.EquipStandInstanceId = standId;
                        play.EquipReplaceInstanceId = replaceId;
                        yield return play;
                    }
                }
            }
        }

        private IEnumerable<PlayLemonCard> RebrandPlays(PlayerState player, int cardId)
        {
            foreach (var stand in player.Stands)
            {
                foreach (var newType in Db.StandTypes)
                {
                    if (newType.Id == stand.StandTypeId || State.StandSupply[newType.Id].Count == 0)
                    {
                        continue;
                    }
                    int overflow = System.Math.Max(0, stand.Equipped.Count - newType.UpgradeSlots);
                    foreach (var discards in CardCombos(stand.Equipped, overflow))
                    {
                        var play = NewPlay(player, cardId);
                        play.TargetStandInstanceId = stand.InstanceId;
                        play.NewStandTypeId = newType.Id;
                        play.SelectedInstanceIds = discards;
                        yield return play;
                    }
                }
            }
        }

        private IEnumerable<PlayLemonCard> ReduceAndReusePlays(PlayerState player, int cardId)
        {
            // Every physical copy in the discard is offered, not one per card name: the
            // picker shows the pile as it sits on the table, and players count copies.
            foreach (int bmId in State.BlackMarketDiscard)
            {
                foreach (var (standId, replaceId) in DestinationsFor(player, EquippedDef(bmId)))
                {
                    var play = NewPlay(player, cardId);
                    play.DiscardedBmInstanceId = bmId;
                    play.EquipStandInstanceId = standId;
                    play.EquipReplaceInstanceId = replaceId;
                    yield return play;
                }
            }
        }

        private IEnumerable<PlayLemonCard> ReverseEngineerPlays(PlayerState player, int cardId)
        {
            var drawTwo = NewPlay(player, cardId);
            drawTwo.DrawInstead = true;
            yield return drawTwo;

            // Every physical copy is offered, as for Reduce and Reuse.
            foreach (int lemonId in State.LemonDiscard)
            {
                if (!IsReverseEngineerable(Db.Lemon(State.LemonInstances[lemonId].DefId)))
                {
                    continue;
                }
                var play = NewPlay(player, cardId);
                play.DiscardedLemonInstanceId = lemonId;
                yield return play;
            }
        }

        private IEnumerable<PlayLemonCard> RummageSalePlays(PlayerState player, int cardId)
        {
            var equips = AllEquipped(player).ToList();
            int emitted = 0;
            for (int size = 1; size <= RummageSaleMaxCards && emitted < RummageSaleMoveCap; size++)
            {
                foreach (var soldIds in CardCombos(equips, size))
                {
                    emitted++;
                    var play = NewPlay(player, cardId);
                    play.SelectedInstanceIds = soldIds;
                    yield return play;
                    if (emitted >= RummageSaleMoveCap)
                    {
                        break;
                    }
                }
            }
        }

        private IEnumerable<PlayLemonCard> ThatsNotFairPlays(PlayerState player, int cardId)
        {
            foreach (var victim in OpponentsOf(player))
            {
                foreach (int equippedId in AllEquipped(victim))
                {
                    var play = NewPlay(player, cardId);
                    play.TargetEquippedInstanceId = equippedId;
                    yield return play;
                }
            }
        }

        // ----------------------------------------------------------- helpers

        private IEnumerable<int> AllEquipped(PlayerState player) =>
            player.Turf.Equipped.Concat(player.Stands.SelectMany(s => s.Equipped));

        /// <summary>A null StandId targets the turf.</summary>
        private IEnumerable<(int? StandId, int? ReplaceId)> DestinationsFor(PlayerState player, BlackMarketCardDef def)
        {
            if (def.Target != EquipTarget.Stand)
            {
                if (player.Turf.Equipped.Count < Db.Turf.UpgradeSlots)
                {
                    yield return (null, null);
                    yield break;
                }
                foreach (int replace in player.Turf.Equipped)
                {
                    yield return (null, replace);
                }
                yield break;
            }

            // Free slots first: the top option (and a tie-breaking greedy bot)
            // should never silently trash an existing upgrade.
            foreach (var stand in player.Stands)
            {
                if (!IsStandFull(stand))
                {
                    yield return (stand.InstanceId, null);
                }
            }
            foreach (var stand in player.Stands)
            {
                if (IsStandFull(stand))
                {
                    foreach (int replace in stand.Equipped)
                    {
                        yield return (stand.InstanceId, replace);
                    }
                }
            }
        }

        private bool IsStandFull(StandInstance stand) =>
            stand.Equipped.Count >= Db.StandType(stand.StandTypeId).UpgradeSlots;

        /// <summary>Exhaustive when small; otherwise a deterministic sliding-window sample, so the move list stays bounded.</summary>
        private static List<List<int>> CardCombos(IReadOnlyList<int> cards, int comboSize)
        {
            var combos = new List<List<int>>();
            if (comboSize <= 0)
            {
                combos.Add(new List<int>());
                return combos;
            }
            if (comboSize > cards.Count)
            {
                return combos;
            }

            if (CombinationCount(cards.Count, comboSize) > ExhaustiveComboLimit)
            {
                for (int start = 0;
                     start + comboSize <= cards.Count && combos.Count < SampledComboLimit;
                     start += comboSize)
                {
                    combos.Add(cards.Skip(start).Take(comboSize).ToList());
                }
                return combos;
            }

            var combo = new int[comboSize];
            void AddCombosFrom(int firstIndex, int chosenCount)
            {
                if (chosenCount == comboSize)
                {
                    combos.Add(combo.ToList());
                    return;
                }
                for (int i = firstIndex; i <= cards.Count - (comboSize - chosenCount); i++)
                {
                    combo[chosenCount] = cards[i];
                    AddCombosFrom(i + 1, chosenCount + 1);
                }
            }
            AddCombosFrom(0, 0);
            return combos;
        }

        private static long CombinationCount(int itemCount, int comboSize)
        {
            long count = 1;
            for (int i = 0; i < comboSize; i++)
            {
                count = count * (itemCount - i) / (i + 1);
            }
            return count;
        }

        /// <summary>
        /// Plans and attacks, except another Reverse Engineer: two copies recovering each other
        /// is a forced-play chain that never ends.
        /// </summary>
        private static bool IsReverseEngineerable(LemonCardDef def) =>
            (def.Type == LemonCardType.Plan || def.Type == LemonCardType.Attack) &&
            def.Id != LemonCardIds.ReverseEngineer;
    }
}
