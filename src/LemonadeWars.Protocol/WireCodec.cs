using System;
using System.Collections.Generic;
using System.Linq;
using LemonadeWars.Engine.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace LemonadeWars.Protocol
{
    /// <summary>
    /// JSON wire encoding for engine actions and events, using an explicit reflection-built
    /// type registry with a "$type" discriminator (never TypeNameHandling — clients cannot
    /// instantiate arbitrary types). Also redacts hidden information from events per seat.
    /// </summary>
    public static class WireCodec
    {
        private static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);

        public static JsonSerializerSettings Settings { get; } = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            Converters =
            {
                // Enums travel as strings ("finished", "diamond") — readable and robust
                // against enum reordering between client and server versions.
                new Newtonsoft.Json.Converters.StringEnumConverter
                {
                    NamingStrategy = new CamelCaseNamingStrategy(),
                },
            },
        };

        private static readonly Dictionary<string, Type> ActionTypes = BuildRegistry<GameAction>();
        private static readonly Dictionary<string, Type> EventTypes = BuildRegistry<GameEvent>();
        private static readonly Dictionary<Type, string> ActionNames =
            ActionTypes.ToDictionary(kv => kv.Value, kv => kv.Key);
        private static readonly Dictionary<Type, string> EventNames =
            EventTypes.ToDictionary(kv => kv.Value, kv => kv.Key);

        private static Dictionary<string, Type> BuildRegistry<TBase>()
        {
            return typeof(TBase).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(TBase).IsAssignableFrom(t))
                .ToDictionary(t => t.Name, t => t);
        }

        // ----------------------------------------------------------- actions

        public static JObject EncodeAction(GameAction action)
        {
            var json = JObject.FromObject(action, Serializer);
            json["$type"] = ActionNames[action.GetType()];
            return json;
        }

        public static GameAction DecodeAction(JObject json)
        {
            string name = (string?)json["$type"]
                ?? throw new JsonSerializationException("Action is missing $type.");
            if (!ActionTypes.TryGetValue(name, out var type))
            {
                throw new JsonSerializationException($"Unknown action type '{name}'.");
            }
            return (GameAction?)json.ToObject(type, Serializer)
                ?? throw new JsonSerializationException($"Could not decode '{name}'.");
        }

        // ------------------------------------------------------------ events

        /// <summary>
        /// Encode an event as one seat is allowed to see it. Today's only redaction:
        /// another player's draw must not reveal which card they drew.
        /// </summary>
        public static JObject EncodeEventFor(GameEvent gameEvent, int seat)
        {
            var visible = gameEvent;
            if (gameEvent is CardDrawn drawn && drawn.PlayerId != seat)
            {
                visible = new CardDrawn { PlayerId = drawn.PlayerId, InstanceId = 0, DefId = "" };
            }

            var json = JObject.FromObject(visible, Serializer);
            json["$type"] = EventNames[gameEvent.GetType()];
            json["label"] = visible.ToString();
            return json;
        }

        public static string EventTypeName(JObject json) => (string?)json["$type"] ?? "";

        /// <summary>
        /// Decode a wire event back into its typed form. Returns null for unknown types
        /// so an older client shrugs at events a newer server invents.
        /// </summary>
        public static GameEvent? DecodeEvent(JObject json)
        {
            string name = (string?)json["$type"] ?? "";
            return EventTypes.TryGetValue(name, out var type)
                ? (GameEvent?)json.ToObject(type, Serializer)
                : null;
        }
    }
}
