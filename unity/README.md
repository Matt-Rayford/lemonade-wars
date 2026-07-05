# Lemonade Wars — Unity Project

The Unity front end for the C# rules engine in `../src/LemonadeWars.Engine`.

## Opening the project

1. Open **Unity Hub** → **Add** → **Add project from disk** → select this `unity/` folder.
2. Open it with **Unity 6000.5.2f1** (already installed).
3. Press **Play**. No scene setup is needed — `LemonadeWarsApp` bootstraps itself
   into whatever scene is open (even an empty untitled one).

You play seat 0 against three GreedyBots.

| Key | Action |
|-----|--------|
| `B` | Toggle autopilot for your seat (watch 4 bots play) |
| `N` | New game |

## Architecture

- **`Assets/Plugins/LemonadeWars.Engine.dll`** — the rules engine, built from
  `../src`. The engine is pure C# (netstandard2.1) and never references Unity.
- **`Assets/StreamingAssets/`** — card data (`game-data/*.json`) and art
  (`images/`), synced from the repo root. Gitignored; regenerate with the sync
  script below.
- **`Assets/Scripts/`** — Unity-side code only:
  - `LemonadeWarsApp.cs` — bootstrap, game loop, bot stepping, debug HUD
  - `CardArt.cs` — images.json manifest + lazy texture cache
  - `MoveDescriber.cs` — human-readable labels for engine actions
  - `UiKit.cs` — code-built UGUI helpers

The UI is intentionally a **debug HUD**: every legal move (from
`Game.LegalMovesFor`) is a button, so the whole ruleset is playable while real
presentation work happens later.

## After changing the engine or card data

```sh
tools/sync_unity.sh
```

builds the engine and refreshes the DLL + StreamingAssets. Unity picks the
changes up on refocus.
