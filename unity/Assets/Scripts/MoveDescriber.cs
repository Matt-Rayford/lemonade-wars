using System.Linq;
using LemonadeWars.Engine.Core;
using LemonadeWars.Engine.Data;

namespace LemonadeWars.Unity
{
    /// <summary>Human-readable labels for engine actions, used by the move buttons.</summary>
    public static class MoveDescriber
    {
        public static string Describe(Game game, GameAction action)
        {
            var s = game.State;
            string PlayerName(int? id) => id is int p ? s.Players[p].Name : "?";
            string LemonName(int instanceId) => game.Db.Lemon(s.LemonInstances[instanceId].DefId).Name;
            string BmName(int instanceId) => game.Db.BlackMarket(s.BlackMarketInstances[instanceId].DefId).Name;

            switch (action)
            {
                case ChooseLemonLords c:
                    return "Keep: " + string.Join(" + ", c.KeepTitleIds.Select(id => game.Db.Title(id).Name));
                case InitialBuyStand b:
                    return $"Buy {game.Db.StandType(b.StandTypeId).Name} (slot {b.InsertIndex})";
                case InitialBuyEnd _:
                    return "Finish this draft visit";
                case DrawLemonCard _:
                    return "Draw a Lemon card";
                case BuyStand b:
                    return $"Buy {game.Db.StandType(b.StandTypeId).Name} " +
                           $"(${game.StandPrice(action.PlayerId, b.StandTypeId)}, slot {b.InsertIndex})";
                case BuyBlackMarket b:
                {
                    var def = game.Db.BlackMarket(s.BlackMarketInstances[s.Market[b.MarketIndex]].DefId);
                    string target = def.Target == EquipTarget.Turf
                        ? "Turf"
                        : $"stand #{s.Players[action.PlayerId].Stands.FindIndex(st => st.InstanceId == b.TargetStandInstanceId) + 1}";
                    string replace = b.ReplaceInstanceId is int r ? $", replacing {BmName(r)}" : "";
                    return $"Buy {def.Name} (${game.BlackMarketPrice(action.PlayerId, def)}) onto {target}{replace}";
                }
                case BuyBraggingRights _:
                    return $"Buy Bragging Rights (${game.Db.Supporting.BraggingRightsPrices[s.BraggingRightsSold]})";
                case RefreshMarket _:
                    return "Refresh the Black Market ($1)";
                case EndTurn _:
                    return "End turn (roll the sale die)";
                case PlayLemonCard p:
                {
                    string name = LemonName(p.CardInstanceId);
                    string detail = "";
                    if (p.TargetPlayerId is int victim)
                    {
                        detail = $" -> {PlayerName(victim)}";
                    }
                    else if (p.TargetEquippedInstanceId is int eq)
                    {
                        detail = $" -> {BmName(eq)}";
                    }
                    else if (p.DrawInstead)
                    {
                        detail = " (draw 2 instead)";
                    }
                    else if (p.DiscardedLemonInstanceId is int dl)
                    {
                        detail = $" -> recover {LemonName(dl)}";
                    }
                    else if (p.DiscardedBmInstanceId is int db)
                    {
                        detail = $" -> {BmName(db)}";
                    }
                    else if (p.MarketIndex is int mi && mi < s.Market.Count)
                    {
                        detail = $" -> take {BmName(s.Market[mi])}";
                    }
                    else if (!string.IsNullOrEmpty(p.NewStandTypeId))
                    {
                        detail = $" -> {game.Db.StandType(p.NewStandTypeId).Name}";
                    }
                    else if (p.TargetStandInstanceId is int st)
                    {
                        int idx = s.Players[action.PlayerId].Stands.FindIndex(x => x.InstanceId == st);
                        detail = $" -> stand #{idx + 1}";
                    }
                    else if (p.SelectedInstanceIds.Count > 0)
                    {
                        detail = " -> " + string.Join(", ", p.SelectedInstanceIds.Select(BmName));
                    }
                    return $"Play {name}{detail}";
                }
                case RespondToWindow r:
                {
                    if (r.EquippedInstanceId is int decoy)
                    {
                        return $"Use {BmName(decoy)}";
                    }
                    string name = LemonName(r.CardInstanceId);
                    return r.RedirectTargetId is int t ? $"Play {name} -> {PlayerName(t)}" : $"Play {name}";
                }
                case PassWindow _:
                    return "Pass";
                case UseTurnAbility a:
                    return $"Use {BmName(a.EquippedInstanceId)}";
                case SubmitDiscard d:
                    return "Discard " + string.Join(", ", d.InstanceIds.Select(LemonName));
                case SubmitTimeoutPayment t:
                    return t.SellStandInstanceIds.Count + t.SellBmInstanceIds.Count == 0
                        ? "Pay the Timeout fine"
                        : "Pay the fine (selling " +
                          string.Join(", ", t.SellBmInstanceIds.Select(BmName)
                              .Concat(t.SellStandInstanceIds.Select(id =>
                                  game.Db.StandType(s.Players[action.PlayerId].Stands
                                      .First(st => st.InstanceId == id).StandTypeId).Name))) + ")";
                case SubmitRetarget r:
                    return $"Retarget -> {BmName(r.TargetEquippedInstanceId ?? 0)}";
                case SkipFreePlay _:
                    return "Skip";
                case SubmitAbilityChoice c:
                    if (c.TargetPlayerId is int v2)
                    {
                        return $"Steal from {PlayerName(v2)}";
                    }
                    if (c.CardInstanceIds.Count > 0)
                    {
                        return "Choose " + string.Join(", ", c.CardInstanceIds.Select(LemonName));
                    }
                    if (c.EquippedInstanceId is int copy)
                    {
                        return $"Copy {BmName(copy)}";
                    }
                    if (c.StandInstanceId is int stand)
                    {
                        int idx = s.Players[action.PlayerId].Stands.FindIndex(x => x.InstanceId == stand);
                        return $"Sell with stand #{idx + 1}";
                    }
                    return "Choose";
                default:
                    return action.GetType().Name;
            }
        }
    }
}
