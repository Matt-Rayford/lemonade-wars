#!/usr/bin/env bash
# Sync the rules engine and game data into the Unity project.
#
#   tools/sync_unity.sh
#
# 1. Builds LemonadeWars.Engine (Release) and copies the DLL into Assets/Plugins.
#    (Newtonsoft.Json is NOT copied — Unity's com.unity.nuget.newtonsoft-json provides it.)
# 2. Copies game-data/*.json and game-assets/images/ into Assets/StreamingAssets.
#
# Run after any engine change or card-data regeneration.
set -euo pipefail
cd "$(dirname "$0")/.."

DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
UNITY_DIR="unity"
PLUGINS="$UNITY_DIR/Assets/Plugins"
STREAMING="$UNITY_DIR/Assets/StreamingAssets"

echo "== building engine + protocol =="
"$DOTNET" build src/LemonadeWars.Protocol -c Release --nologo -v quiet

echo "== syncing DLLs =="
mkdir -p "$PLUGINS"
cp src/LemonadeWars.Engine/bin/Release/netstandard2.1/LemonadeWars.Engine.dll "$PLUGINS/"
cp src/LemonadeWars.Protocol/bin/Release/netstandard2.1/LemonadeWars.Protocol.dll "$PLUGINS/"

echo "== syncing fonts =="
mkdir -p "$UNITY_DIR/Assets/Resources/fonts"
cp game-assets/fonts/*.ttf "$UNITY_DIR/Assets/Resources/fonts/" 2>/dev/null || true

echo "== syncing game data =="
rm -rf "$STREAMING/game-data" "$STREAMING/images"
mkdir -p "$STREAMING/game-data" "$STREAMING/images"
cp game-data/*.json "$STREAMING/game-data/"
cp -R game-assets/images/ "$STREAMING/images/"

echo "done:"
echo "  $(ls "$PLUGINS" | grep -c dll) dll(s) in Assets/Plugins"
echo "  $(ls "$STREAMING/game-data" | wc -l | tr -d ' ') json files, $(find "$STREAMING/images" -type f | wc -l | tr -d ' ') images in StreamingAssets"
