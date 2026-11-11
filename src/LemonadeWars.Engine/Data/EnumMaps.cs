using System;
using System.Collections.Generic;

namespace LemonadeWars.Engine.Data
{
    /// <summary>String forms used by the game-data JSON files.</summary>
    public static class EnumMaps
    {
        public static readonly IReadOnlyDictionary<string, LemonCardType> LemonType =
            new Dictionary<string, LemonCardType>(StringComparer.OrdinalIgnoreCase)
            {
                ["plan"] = LemonCardType.Plan,
                ["attack"] = LemonCardType.Attack,
                ["instant"] = LemonCardType.Instant,
                ["timeout"] = LemonCardType.Timeout,
            };

        public static readonly IReadOnlyDictionary<string, EquipTarget> Target =
            new Dictionary<string, EquipTarget>(StringComparer.OrdinalIgnoreCase)
            {
                ["stand"] = EquipTarget.Stand,
                ["turf"] = EquipTarget.Turf,
            };

        public static readonly IReadOnlyDictionary<string, EffectTiming> Timing =
            new Dictionary<string, EffectTiming>(StringComparer.OrdinalIgnoreCase)
            {
                ["Passive"] = EffectTiming.Passive,
                ["On Sale"] = EffectTiming.OnSale,
                ["On Your Turn"] = EffectTiming.OnYourTurn,
                ["Power Pour"] = EffectTiming.PowerPour,
            };

        public static readonly IReadOnlyDictionary<string, Shape> ShapeName =
            new Dictionary<string, Shape>(StringComparer.OrdinalIgnoreCase)
            {
                ["diamond"] = Shape.Diamond,
                ["circle"] = Shape.Circle,
                ["square"] = Shape.Square,
            };

        public static TValue Parse<TValue>(IReadOnlyDictionary<string, TValue> map, string key, string context)
        {
            if (map.TryGetValue(key, out var value))
            {
                return value;
            }
            throw new InvalidOperationException($"Unknown {context} value '{key}' in game data.");
        }
    }
}
