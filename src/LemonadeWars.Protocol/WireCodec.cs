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
    /// JSON wire encoding for engine actions and events. Types travel as a "$type" name
    /// looked up in an explicit registry, never TypeNameHandling, so a client cannot make
    /// the server instantiate arbitrary types. Events are redacted per seat.
    /// </summary>
    public static class WireCodec
    {
        // Actions and events travel with Newtonsoft defaults (PascalCase keys, numeric enums):
        // that is the wire and room-log format. Settings below shapes envelopes and views only.
        private static readonly JsonSerializer Serializer = JsonSerializer.Create();

        public static JsonSerializerSettings Settings { get; } = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            Converters =
            {
                // Enums travel as strings, so reordering one cannot desync client and server.
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
            string typeName = (string?)json["$type"]
                ?? throw new JsonSerializationException("Action is missing $type.");
            if (!ActionTypes.TryGetValue(typeName, out var type))
            {
                throw new JsonSerializationException($"Unknown action type '{typeName}'.");
            }
            return (GameAction?)json.ToObject(type, Serializer)
                ?? throw new JsonSerializationException($"Could not decode '{typeName}'.");
        }

        // ------------------------------------------------------------ events

        /// <summary>
        /// Encodes an event as one seat may see it. The only redaction so far: another
        /// player's draw stays public, with its reason, but hides which card was drawn.
        /// </summary>
        public static JObject EncodeEventFor(GameEvent gameEvent, int seat)
        {
            var visible = gameEvent;
            if (gameEvent is CardDrawn drawn && drawn.PlayerId != seat)
            {
                visible = new CardDrawn
                {
                    PlayerId = drawn.PlayerId,
                    InstanceId = 0,
                    DefId = "",
                    Reason = drawn.Reason,
                };
            }

            var json = JObject.FromObject(visible, Serializer);
            json["$type"] = EventNames[gameEvent.GetType()];
            json["label"] = visible.ToString();
            return json;
        }

        public static string EventTypeName(JObject json) => (string?)json["$type"] ?? "";

        /// <summary>Null for unknown types, so an older client ignores events a newer server invents.</summary>
        public static GameEvent? DecodeEvent(JObject json)
        {
            string typeName = (string?)json["$type"] ?? "";
            return EventTypes.TryGetValue(typeName, out var type)
                ? (GameEvent?)json.ToObject(type, Serializer)
                : null;
        }
    }
}
