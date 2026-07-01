# MatchmakingSystem

Distributed, horizontally scalable matchmaking system with **user accounts** and an **active ranked ladder**, built with .NET 10, RabbitMQ, Redis, PostgreSQL, SignalR and Docker.

Users register/log in (JWT auth), then queue for matches. The system pairs any two players whose ratings are close (≤ 100 apart). Matching state is shared in Redis and mutated atomically with a Lua script, so **multiple worker instances can run concurrently** without races, double-matches, or lost players. Matched players "play" a game, ratings move with Elo, and a background simulator keeps online players competing so the leaderboard is always alive. The dashboard updates in real time via SignalR.

## Architecture

```
                 ┌─────────────┐
Client ──HTTP──► │     API     │ ──publish(MatchRequest)──► RabbitMQ
   ▲             └─────────────┘                               │
   │ SignalR push    │   │  ▲ consume(MatchCompletedEvent)     │ distributes
   │ (live updates)  │   │  │  → broadcast to browsers         │ (competing consumers)
   │                 ▼   ▼  │                       ┌──────────┼──────────┐
   │        ┌──────────┐ ┌────────┐                 ▼          ▼          ▼
   └─────── │ Postgres │ │ Redis  │ ◄──────────  ┌──────┐  ┌──────┐  ┌──────┐
            │ accounts │ │(shared)│  atomic Lua  │Worker│  │Worker│  │Worker│
            └──────────┘ └────────┘  match/Elo   └──────┘  └──────┘  └──────┘
                                                     ▲
                                    RankedSimulator re-queues online players
```

- **API** — auth + REST endpoints, serves the web UI, reads/writes game state in Redis, and pushes live updates to browsers via SignalR
- **Worker** — consumes `MatchRequest`, matches atomically in Redis, resolves the game (coin flip + Elo), records history, publishes `MatchCompletedEvent`; also runs the ranked simulator. **Scales to N instances.**
- **RabbitMQ** — async message broker; distributes messages across workers (competing consumers)
- **Redis** — all game state (queue, leaderboard, join times, online set, simulator flag, match history)
- **PostgreSQL** — user accounts only (username + password hash). Ratings stay in Redis.

> Workers never talk to each other directly — they coordinate **only through shared Redis state**, which is why the matching step must be atomic.

## Accounts & authentication

- **Register** (`POST /api/auth/register`) creates a user in PostgreSQL (password hashed with `PasswordHasher`, PBKDF2), seeds a **1000 starting rating** in the Redis leaderboard, marks the user **online**, and returns a **JWT**.
- **Login** (`POST /api/auth/login`) verifies the password, marks the user online, and returns a JWT.
- The token carries the username; subsequent requests send it as `Authorization: Bearer <token>`.
- **Queue is identity-based:** `POST /api/matchmaking/queue` is `[Authorize]` — there is no free-text userId. The identity comes from the token and the rating is read from the leaderboard, so nobody can queue as someone else.
- **Logout** (`POST /api/auth/logout`) marks the user offline.

> Data split: **PostgreSQL** = who you are (account), **Redis** = your live rating/standing. Accounts are relational and persistent; ratings are hot game state.

## How it works (end to end)

1. A logged-in client calls `POST /queue` with its token; the API resolves the identity, marks it **online**, publishes a `MatchRequest` (userId + current rating), and returns `202 Accepted`
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
- PostgreSQL + Entity Framework Core (user accounts)
- JWT bearer authentication (`Microsoft.AspNetCore.Authentication.JwtBearer`)
- SignalR (real-time push to the browser)
- xUnit (unit tests) · k6 / bombardier (load testing)
- Docker + Docker Compose

## Getting Started

**Prerequisites:** Docker Desktop

```bash
docker compose up --build
```

| Container | Description | Port |
|-----------|-------------|------|
| `rabbitmq` | Message broker | `5672`, `15672` (management UI) |
| `redis` | Game state | `6379` |
| `postgres` | User accounts | `5432` |
| `matchmaking-api` | REST API + auth + web UI | `8080` |
| `matchmaking-worker` | Background consumer + simulator (scalable) | — |

Then open the **web UI** at **http://localhost:8080/**.

Config passed via env in `docker-compose.yml`: `ConnectionStrings__Postgres`, `Jwt__Key`, `Jwt__Issuer` (the `__` maps to nested config, e.g. `Jwt:Key`). The API creates the DB schema on startup with `EnsureCreated()` (retrying until Postgres is ready).

### Scaling workers

The worker service has no fixed `container_name`, so it scales freely:

```bash
docker compose up -d --scale matchmaking-worker=3   # run 3 workers
docker compose up -d --scale matchmaking-worker=1   # back to 1
```

## Web UI

A lightweight dashboard served by the API at `http://localhost:8080/`:

- **Login / register** panel; once logged in, a **"Join queue"** button (queues you by identity) and **logout**
- **Quick add (bot)** — seed test players without an account (dev helper)
- **Ranked simulator** on/off switch
- **Leaderboard** — matched players by rating, each with online/offline toggle and delete
- **Waiting queue** — players still seeking an opponent, with live waiting duration
- **Recent matches** — last 50 results (winner, loser, new scores, time)
- Updates in real time via **SignalR** (no constant polling)

It's a static page under `Matchmaking.Api/wwwroot/`, served from the same origin as the API (no CORS setup needed).

## API Endpoints

### Auth
| Method | Route | Description |
|--------|-------|-------------|
| `POST` | `/api/auth/register` | Create account `{ username, password }` → returns JWT |
| `POST` | `/api/auth/login` | Log in `{ username, password }` → returns JWT |
| `POST` | `/api/auth/logout` | Mark offline (auth required) |
| `GET`  | `/api/auth/me` | Current username (auth required) |

### Matchmaking
| Method | Route | Description |
|--------|-------|-------------|
| `POST` | `/api/matchmaking/queue` | **Auth required** — queue yourself (identity from token, rating from leaderboard) |
| `GET`  | `/api/matchmaking/leaderboard` | Matched players + online status |
| `GET`  | `/api/matchmaking/waiting` | Waiting players + join time |
| `GET`  | `/api/matchmaking/history` | Last 50 matches |
| `PUT`  | `/api/matchmaking/player/{userId}` | Update a player's rating `{ score }` |
| `DELETE` | `/api/matchmaking/player/{userId}` | Remove a player from all structures |
| `GET` / `POST` | `/api/matchmaking/simulator` | Read / set the simulator flag `{ enabled }` |
| `POST` | `/api/matchmaking/player/{userId}/online` | Set online/offline `{ enabled }` |
| `POST` | `/api/matchmaking/seed` | Dev: add a bot player `{ userId?, score? }` |
| `GET`  | `/api/matchmaking/seed/random` | Dev: add a random bot (address-bar convenience) |

Reads and simple CRUD go straight to Redis (fast); only the match request goes through the queue (command → queue, query → direct).

## Real-time updates (SignalR)

The browser connects to the `/hub/matchmaking` hub. When the worker finishes a match it publishes `MatchCompletedEvent`; the API's [`MatchCompletedConsumer`](Matchmaking.Api/Consumers/MatchCompletedConsumer.cs) consumes it and broadcasts to all clients, which then refresh. This replaces polling — the page fetches only when something actually changed (a debounce coalesces bursts, and a local timer ticks the waiting duration without network calls).

> Scaling the API to multiple instances would additionally need a SignalR Redis backplane and a fan-out (per-instance) queue for the event.

## Resilience

- **Redis** connections use `AbortOnConnectFail = false` — the client keeps retrying in the background instead of throwing on a momentary outage (API + Worker).
- **MassTransit** retries a failing message (5 attempts, 500 ms apart) before faulting it.

## Data stores

**PostgreSQL** — `Users` table (via EF Core):

| Column | Notes |
|--------|-------|
| `Id` | primary key |
| `Username` | unique index |
| `PasswordHash` | PBKDF2 hash (never plaintext) |
| `CreatedAtUtc` | |

**Redis keys:**

| Key | Type | Holds |
|-----|------|-------|
| `leaderboard` | Sorted Set | userId → rating |
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
> Note: `POST /queue` now requires a JWT, so load scripts must send an `Authorization` header (or target the `seed` endpoint) to exercise the full path.

## Tests

```bash
dotnet test
```
Unit tests (`Matchmaking.Test`) cover the matching rule in `MatchmakingEngine`. `EloCalculator` is a pure function and is a natural next target for unit tests.

## Project Structure

```
MatchmakingSystem/
├── Matchmaking.Api/             # REST API + auth + web UI + SignalR
│   ├── Controllers/
│   │   ├── MatchmakingController.cs
│   │   └── AuthController.cs           # register / login / logout / me
│   ├── Consumers/MatchCompletedConsumer.cs   # event → SignalR push
│   ├── Hubs/MatchmakingHub.cs
│   ├── Data/AppDbContext.cs           # EF Core DbContext (Users)
│   ├── Models/User.cs                 # account entity
│   ├── Services/TokenService.cs       # JWT generation
│   └── wwwroot/index.html             # dashboard
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
