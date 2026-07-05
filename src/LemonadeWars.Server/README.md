# Lemonade Wars — Game Server

Authoritative multiplayer server: rooms with join codes, per-seat hidden-info views,
server-side bots, and action-log persistence with deterministic replay.

## Run locally

```sh
~/.dotnet/dotnet run --project src/LemonadeWars.Server
```

Listens on `http://0.0.0.0:5225` (`/ws` for the game socket, `/health` for checks).

## Environment

| Variable | Default | Purpose |
|---|---|---|
| `PORT` | `5225` | HTTP/WebSocket port (Railway injects this) |
| `GAME_DATA_DIR` | walk up to repo `game-data/` | Card definitions |
| `DATA_DIR` | `<binary dir>/rooms` | Room action logs (`<CODE>.jsonl`) |
| `BOT_DELAY_MS` | `600` | Pause between server-bot actions (0 = instant, used by tests) |

## Persistence model

When a game starts, the room writes a header line (seed, seat names, bot flags,
reconnect tokens) to `DATA_DIR/<CODE>.jsonl`, then appends every applied action.
On boot the server replays each unfinished log through the deterministic engine,
rebuilding the exact game state — so a crash or Railway redeploy costs nothing but
the connection; players click Resume and continue.

## Railway

- Service → Dockerfile path: `src/LemonadeWars.Server/Dockerfile`
- Mount a volume and set `DATA_DIR` to its mount point to persist games across deploys
- Client URL: `wss://<your-app>.up.railway.app/ws`
