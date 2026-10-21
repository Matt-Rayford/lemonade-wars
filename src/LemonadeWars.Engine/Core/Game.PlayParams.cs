using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Engine.Core
{
    /// <summary>
    /// Per-card play parameterizations and the board-query/combination helpers behind
    /// them. Shared by move enumeration (Game.LegalMoves.cs), forced-play validation,
    /// and attack retargeting. Combinatorial choices (multi-card discards, Rummage Sale
    /// subsets) are capped at a bounded sample; the cap trades exhaustive coverage for
    /// move-list size and is noted inline wherever it applies.
    /// </summary>
    public sealed partial class Game
    {
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
                    // Tantrum copies are interchangeable — one move, engine picks.
                    if (player.TantrumPile.Count > 0)
                    {
                        yield return Base();
                    }
                    break;

                case "blame-changer":
                    if (player.TantrumPile.Count > 0)
                    {
                        foreach (var other in opponents)
                        {
                            var play = Base();
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
                        foreach (int equippedId in AllEquipped(victim))
                        {
                            foreach (var (standId, replaceId) in DestinationsFor(player, EquippedDef(equippedId)))
                            {
                                var play = Base();
                                play.TargetPlayerId = victim.PlayerId;
                                play.TargetEquippedInstanceId = equippedId;
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
                    // EVERY physical copy in the discard is offered: the picker shows the
                    // pile as it sits on the table, and players count the copies.
                    foreach (int bmId in State.BlackMarketDiscard)
                    {
                        var bmDef = EquippedDef(bmId);
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

                    // EVERY physical copy in the discard is offered: the picker shows the
                    // pile as it sits on the table, and players count the copies.
                    foreach (int lemonId in State.LemonDiscard)
                    {
                        var lemonDef = Db.Lemon(State.LemonInstances[lemonId].DefId);
                        if (!IsReverseEngineerable(lemonDef))
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
                        foreach (int equippedId in AllEquipped(victim))
                        {
                            var play = Base();
                            play.TargetEquippedInstanceId = equippedId;
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

        /// <summary>
        /// Plans and attacks can be recovered and played at once — except another
        /// Reverse Engineer: with two copies in the game, each one recovering the other
        /// is a forced-play chain that never ends (an all-bot table looped on it).
        /// </summary>
        private static bool IsReverseEngineerable(LemonCardDef def) =>
            (def.Type == LemonCardType.Plan || def.Type == LemonCardType.Attack) &&
            def.Id != "reverse-engineer";
    }
}
