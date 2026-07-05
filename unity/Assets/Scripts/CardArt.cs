using System.Collections.Generic;
using System.IO;
using LemonadeWars.Engine.Data;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Card artwork lookup backed by game-data/images.json and the StreamingAssets image
    /// tree. Textures load lazily from disk and are cached for the session.
    /// </summary>
    public sealed class CardArt
    {
        private readonly string _imagesRoot;
        private readonly JObject _manifest;
        private readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();

        public CardArt(string streamingAssetsPath)
        {
            _imagesRoot = Path.Combine(streamingAssetsPath, "images");
            string manifestPath = Path.Combine(streamingAssetsPath, "game-data", "images.json");
            _manifest = JObject.Parse(File.ReadAllText(manifestPath));
        }

        public Texture2D Lemon(string defId) =>
            Load((string)_manifest["lemon"]?[defId]);

        public Texture2D BlackMarket(string defId, Shape shape) =>
            Load((string)_manifest["blackMarket"]?[defId]?[shape.ToString().ToLowerInvariant()]);

        public Texture2D Title(string defId) =>
            Load((string)_manifest["titles"]?[defId]);

        public Texture2D Turf(int powerPourNumber) =>
            Load((string)_manifest["turf"]?[powerPourNumber.ToString()]);

        public Texture2D Stand(string standTypeId) =>
            Load((string)(_manifest["stands"]?[standTypeId] as JArray)?[0]);

        public Texture2D BraggingRights(int index)
        {
            var list = _manifest["supporting"]?["braggingRights"] as JArray;
            if (list == null || index < 0 || index >= list.Count)
            {
                return null;
            }
            return Load((string)list[index]);
        }

        public Texture2D Back(string key) =>
            Load((string)_manifest["backs"]?[key]);

        private Texture2D Load(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return null;
            }
            if (_cache.TryGetValue(relativePath, out var cached))
            {
                return cached;
            }

            string fullPath = Path.Combine(_imagesRoot, relativePath);
            Texture2D texture = null;
            if (File.Exists(fullPath))
            {
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.LoadImage(File.ReadAllBytes(fullPath));
                texture.name = relativePath;
            }
            else
            {
                Debug.LogWarning($"CardArt: missing image {fullPath}");
            }
            _cache[relativePath] = texture;
            return texture;
        }
    }
}
