using System;
using System.Collections.Generic;

namespace LemonadeWars.Engine.Data
{
    /// <summary>Lemon deck card categories (rulebook p10).</summary>
    public enum LemonCardType
    {
        Plan,
        Attack,
        Instant,
        /// <summary>The 2 Timeout cards shuffled into the Lemon deck (rulebook p12).</summary>
        Timeout,
    }

    /// <summary>What a Black Market card equips onto.</summary>
    public enum EquipTarget
    {
        Stand,
        Turf,
    }

    /// <summary>When a Black Market card's effect fires.</summary>
    public enum EffectTiming
    {
        Passive,
        OnSale,
        OnYourTurn,
        PowerPour,
    }

    /// <summary>The slot-icon shape printed on Stands and Black Market cards; counted by several First Dibs titles.</summary>
    public enum Shape
    {
        Diamond,
        Circle,
        Square,
    }

    /// <summary>Title card families (rulebook p13).</summary>
    public enum TitleKind
    {
        /// <summary>Claimed mid-game, first player to meet the condition.</summary>
        FirstDibs,
        /// <summary>Secret; evaluated at game end.</summary>
        LemonLord,
    }

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
