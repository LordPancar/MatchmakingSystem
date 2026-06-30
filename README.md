# MatchmakingSystem

Distributed, horizontally scalable matchmaking system with an **active ranked ladder**, built with .NET 10, RabbitMQ, Redis, SignalR and Docker.

Players queue with a score; the system pairs any two players whose scores are close (≤ 100 apart). Matching state is shared in Redis and mutated atomically with a Lua script, so **multiple worker instances can run concurrently** without races, double-matches, or lost players. Matched players "play" a game, ratings move with Elo, and a background simulator keeps online players competing so the leaderboard is always alive. The dashboard updates in real time via SignalR.

## Architecture

```
                 ┌─────────────┐
Client ──HTTP──► │     API     │ ──publish(MatchRequest)──► RabbitMQ
   ▲             └─────────────┘                               │
   │ SignalR push       │  ▲ consume(MatchCompletedEvent)      │ distributes
   │ (live updates)     │  │  → broadcast to browsers          │ (competing consumers)
   │                    ▼  │                       ┌───────────┼───────────┐
   │             ┌─────────────┐                   ▼           ▼           ▼
   └──────────── │    Redis    │ ◄──────────  ┌─────────┐ ┌─────────┐ ┌─────────┐
                 │  (shared)   │  atomic Lua  │ Worker 1│ │ Worker 2│ │ Worker N│
                 └─────────────┘  match/Elo   └─────────┘ └─────────┘ └─────────┘
                                                    ▲
                                        RankedSimulator re-queues online players
```

- **API** — REST endpoints, serves the web UI, reads state from Redis, and pushes live updates to browsers via SignalR
- **Worker** — consumes `MatchRequest`, matches atomically in Redis, resolves the game (coin flip + Elo), records history, publishes `MatchCompletedEvent`; also runs the ranked simulator. **Scales to N instances.**
- **RabbitMQ** — async message broker; distributes messages across workers (competing consumers)
- **Redis** — all shared state (queue, leaderboard, join times, online set, simulator flag, match history)

> Workers never talk to each other directly — they coordinate **only through shared Redis state**, which is why the matching step must be atomic.

## How it works (end to end)

1. Client sends `POST /queue` with `userId` + `score`; the API marks the player **online**, publishes a `MatchRequest`, and returns `202 Accepted`
2. RabbitMQ delivers the message to one worker
3. The worker runs an **atomic Lua script** that either:
   - finds the closest waiting player within ±100, removes them from the queue, returns the match, **or**
   - adds the incoming player to the waiting set (and records the join time)
4. On a match, the worker **plays the game**: a coin flip picks the winner, **Elo** adjusts both ratings, the leaderboard is updated, the match is appended to history, and `MatchCompletedEvent` is published
5. The API consumes that event and **pushes** a signal to all browsers over SignalR; the dashboard refreshes
6. The **RankedSimulator** periodically re-queues online leaderboard players, so matches keep happening on their own

Because find-and-remove happens inside a single Lua script, Redis executes it atomically (single-threaded) — no distributed lock is needed and concurrent workers can never grab the same waiting player.

## Ranked system & simulator

- Each player's **score is their rating**. The leaderboard is just those ratings, sorted — not a separate thing that gets polled.
- When two close-rated players match, a **coin flip** decides the winner; [`EloCalculator`](Matchmaking.Shared/EloCalculator.cs) moves the ratings (beating a stronger opponent gains more).
- The **simulator** ([`RankedSimulator`](Matchmaking.Worker/RankedSimulator.cs)) re-queues **online** players every few seconds so the ladder is continuously active. It can be toggled live from the UI (state stored in the `ranked:simulator` Redis flag) and configured via `Ranked:SimulatorEnabled` / `Ranked:IntervalSeconds`.
- **Online / offline:** only online players (`players:online` set) are re-queued. An offline player keeps their rating but stops playing.

## Tech Stack

- .NET 10 (ASP.NET Core Web API + Worker Service)
- MassTransit 8.x + RabbitMQ (with message retry)
- StackExchange.Redis (Sorted Sets, Hash, Set, List + Lua scripting)
- SignalR (real-time push to the browser)
- xUnit (unit tests)
- k6 / bombardier (load testing)
- Docker + Docker Compose

## Getting Started

**Prerequisites:** Docker Desktop

```bash
docker compose up --build
```

| Container | Description | Port |
|-----------|-------------|------|
| `rabbitmq` | Message broker | `5672`, `15672` (management UI) |
| `redis` | Shared state | `6379` |
| `matchmaking-api` | REST API + web UI | `8080` |
| `matchmaking-worker` | Background consumer + simulator (scalable) | — |

Then open the **web UI** at **http://localhost:8080/**.

### Scaling workers

The worker service has no fixed `container_name`, so it scales freely:

```bash
docker compose up -d --scale matchmaking-worker=3   # run 3 workers
docker compose up -d --scale matchmaking-worker=1   # back to 1
```

## Web UI

A lightweight dashboard served by the API at `http://localhost:8080/`:

- **Add player** form (+ a "random player" button)
- **Ranked simulator** on/off switch
- **Leaderboard** — matched players by rating, each with online/offline toggle and delete
- **Waiting queue** — players still seeking an opponent, with live waiting duration
- **Recent matches** — last 50 results (winner, loser, new scores, time)
- Updates in real time via **SignalR** (no constant polling)

It's a static page under `Matchmaking.Api/wwwroot/`, served from the same origin as the API (no CORS setup needed).

## API Endpoints

| Method | Route | Description |
|--------|-------|-------------|
| `POST` | `/api/matchmaking/queue` | Queue a player `{ userId, score }` (marks them online) |
| `GET`  | `/api/matchmaking/leaderboard` | Matched players + online status |
| `GET`  | `/api/matchmaking/waiting` | Waiting players + join time |
| `GET`  | `/api/matchmaking/history` | Last 50 matches |
| `PUT`  | `/api/matchmaking/player/{userId}` | Update a player's rating `{ score }` |
| `DELETE` | `/api/matchmaking/player/{userId}` | Remove a player from all structures |
| `GET` / `POST` | `/api/matchmaking/simulator` | Read / set the simulator flag `{ enabled }` |
| `POST` | `/api/matchmaking/player/{userId}/online` | Set online/offline `{ enabled }` |

Reads and simple CRUD go straight to Redis (fast); only the match request goes through the queue (command → queue, query → direct).

## Real-time updates (SignalR)

The browser connects to the `/hub/matchmaking` hub. When the worker finishes a match it publishes `MatchCompletedEvent`; the API's [`MatchCompletedConsumer`](Matchmaking.Api/Consumers/MatchCompletedConsumer.cs) consumes it and broadcasts to all clients, which then refresh. This replaces polling — the page fetches only when something actually changed (a debounce coalesces bursts, and a local timer ticks the waiting duration without network calls).

> Scaling the API to multiple instances would additionally need a SignalR Redis backplane and a fan-out (per-instance) queue for the event.

## Resilience

- **Redis** connections use `AbortOnConnectFail = false` — the client keeps retrying in the background instead of throwing on a momentary outage (API + Worker).
- **MassTransit** retries a failing message (5 attempts, 500 ms apart) before faulting it.

## Redis keys

| Key | Type | Holds |
|-----|------|-------|
| `leaderboard` | Sorted Set | userId → rating (matched players) |
| `matchmaking:queue` | Sorted Set | userId → score (waiting players) |
| `matchmaking:joined` | Hash | userId → join time (epoch ms) |
| `players:online` | Set | online userIds |
| `ranked:simulator` | String | `"1"`/`"0"` simulator flag |
| `matchmaking:history` | List | last 50 matches (JSON) |

## Events

| Event | Publisher | Consumer | Description |
|-------|-----------|----------|-------------|
| `MatchRequest` | API / Simulator | Worker | Player queues for a match |
| `MatchCompletedEvent` | Worker | API (→ SignalR) | A match finished (incl. winner/loser) |

## Load Testing

Both tools target `POST /queue` and write output to `loadtest-results/` (git-ignored).

```bash
Matchmaking.Test\k6-test.bat          # k6 (handleSummary → k6-result.txt + k6-summary.json)
Matchmaking.Test\bombardier-test.bat  # bombardier (queue + leaderboard)
```
Edit the variables at the top of the `.bat` files to change connection/request counts or the bombardier path.

## Tests

```bash
dotnet test
```
Unit tests (`Matchmaking.Test`) cover the matching rule in `MatchmakingEngine`. `EloCalculator` is a pure function and is a natural next target for unit tests.

## Project Structure

```
MatchmakingSystem/
├── Matchmaking.Api/             # REST API + web UI + SignalR (producer & event consumer)
│   ├── Controllers/MatchmakingController.cs
│   ├── Consumers/MatchCompletedConsumer.cs   # event → SignalR push
│   ├── Hubs/MatchmakingHub.cs
│   └── wwwroot/index.html                     # dashboard
├── Matchmaking.Worker/          # Background worker (consumer, scalable)
│   ├── MatchRequestConsumer.cs  # match → coin flip + Elo → leaderboard + history
│   ├── RedisMatchmaker.cs       # atomic Redis + Lua matching + join times
│   └── RankedSimulator.cs       # re-queues online players (active ladder)
├── Matchmaking.Shared/          # Shared models & pure logic
│   ├── MatchRequest.cs
│   ├── MatchResult.cs
│   ├── MatchCompletedEvent.cs
│   ├── MatchRecord.cs           # history entry
│   ├── EloCalculator.cs         # pure Elo math
│   └── MatchmakingEngine.cs     # in-memory matching rule (unit-tested reference)
├── Matchmaking.Test/            # Unit tests + load testing scripts
│   ├── MatchmakingEngineTest.cs
│   ├── loadtest.js, k6-test.bat, bombardier-test.bat
└── docker-compose.yml
```

## RabbitMQ Management UI

After `docker compose up`, visit `http://localhost:15672` to monitor queues and events.
Default credentials: `guest` / `guest`
