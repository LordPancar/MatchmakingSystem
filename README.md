# MatchmakingSystem

Distributed, horizontally scalable matchmaking system built with .NET 10, RabbitMQ, Redis, and Docker.

Players queue with a score; the system pairs any two players whose scores are close (≤ 100 apart). Matching state is shared in Redis and mutated atomically with a Lua script, so **multiple worker instances can run concurrently** without races, double-matches, or lost players.

## Architecture

```
                 ┌─────────────┐
Client ──HTTP──► │     API     │ ──publish(MatchRequest)──► RabbitMQ
   ▲             └─────────────┘                               │
   │                    │                       distributes (competing consumers)
   │ web UI / REST      │ read                  ┌──────────────┼──────────────┐
   │                    ▼                        ▼              ▼              ▼
   │             ┌─────────────┐           ┌─────────┐    ┌─────────┐    ┌─────────┐
   └──────────── │    Redis    │ ◄──────── │ Worker 1│    │ Worker 2│    │ Worker N│
                 │             │  atomic   └─────────┘    └─────────┘    └─────────┘
                 │ matchmaking │  Lua match        │             │             │
                 │ :queue      │                   └─────────────┴─────────────┘
                 │ leaderboard │ ◄────── write matched players + publish MatchCompletedEvent
                 └─────────────┘
```

- **API** — receives match requests via REST, publishes `MatchRequest` to RabbitMQ; also serves the web UI and reads leaderboard/queue from Redis
- **Worker** — consumes `MatchRequest`, performs atomic matching in Redis, writes results, publishes `MatchCompletedEvent`. **Scales to N instances.**
- **RabbitMQ** — async message broker; distributes messages across workers (competing consumers)
- **Redis** — shared state: the waiting queue (`matchmaking:queue`) and the `leaderboard`, both Sorted Sets

> Workers never talk to each other directly — they coordinate **only through shared Redis state**, which is why the matching step must be atomic.

## How Matchmaking Works

1. Client sends `POST /queue` with a `userId` and `score`
2. API publishes a `MatchRequest` to RabbitMQ and returns immediately (`202 Accepted`)
3. RabbitMQ delivers the message to one of the workers
4. The worker runs an **atomic Lua script** against Redis that either:
   - finds the closest waiting player within ±100 score, removes them from the queue, and returns the match, **or**
   - adds the incoming player to the waiting Sorted Set (no opponent yet)
5. On a match, the worker writes both players to the `leaderboard` and publishes `MatchCompletedEvent`

Because the find-and-remove happens inside a single Lua script, Redis executes it atomically (single-threaded) — no distributed lock is needed and concurrent workers can never grab the same waiting player.

## Tech Stack

- .NET 10 (ASP.NET Core Web API + Worker Service)
- MassTransit 8.x + RabbitMQ (with message retry)
- StackExchange.Redis (Sorted Sets + Lua scripting)
- xUnit (unit tests)
- k6 / bombardier (load testing)
- Docker + Docker Compose

## Getting Started

**Prerequisites:** Docker Desktop

```bash
docker compose up --build
```

This starts the stack:

| Container | Description | Port |
|-----------|-------------|------|
| `rabbitmq` | Message broker | `5672`, `15672` (management UI) |
| `redis` | Shared queue + leaderboard | `6379` |
| `matchmaking-api` | REST API + web UI | `8080` |
| `matchmaking-worker` | Background consumer (scalable) | — |

Then open the **web UI** at **http://localhost:8080/**.

### Scaling workers

The worker service has no fixed `container_name`, so it can be scaled freely:

```bash
# Run 3 workers
docker compose up -d --scale matchmaking-worker=3

# Change the count live (adds/removes only workers)
docker compose up -d --scale matchmaking-worker=5
```

## Web UI

A lightweight dashboard is served by the API at `http://localhost:8080/`:

- **Add player** form (with a "random player" button for quick testing)
- **Leaderboard** — matched players, sorted by score
- **Waiting queue** — players still looking for an opponent
- Auto-refreshes every 2 seconds

It's a static page under `Matchmaking.Api/wwwroot/`, served from the same origin as the API (no CORS setup needed).

## API Endpoints

### Queue a match request
```
POST http://localhost:8080/api/matchmaking/queue
Content-Type: application/json

{ "userId": "player1", "score": 1500 }
```
Response:
```json
{ "message": "Kuyruğa alındı.", "requestId": "3fa85f64-5717-4562-b3fc-2c963f66afa6" }
```

### Get leaderboard (matched players)
```
GET http://localhost:8080/api/matchmaking/leaderboard
```
```json
[
  { "userId": "player1", "score": 1500 },
  { "userId": "player2", "score": 1420 }
]
```

### Get waiting queue (unmatched players)
```
GET http://localhost:8080/api/matchmaking/waiting
```
```json
[
  { "userId": "player3", "score": 4000 }
]
```

## Resilience

To survive transient disconnects (e.g. during deploys, restarts, or scaling), the system is hardened:

- **Redis** connections use `AbortOnConnectFail = false`, so the client keeps retrying in the background instead of throwing on a momentary outage (applied in both API and Worker).
- **MassTransit** retries a failing message (5 attempts, 500 ms apart) before faulting it, absorbing brief hiccups.

## Events

| Event | Publisher | Description |
|-------|-----------|-------------|
| `MatchRequest` | API | Player queues for a match |
| `MatchCompletedEvent` | Worker | Two players have been matched |

### MatchCompletedEvent
```json
{
  "matchId": "guid",
  "player1Id": "player1",
  "player2Id": "player2",
  "completedAtUtc": "2026-01-01T00:00:00Z"
}
```

## Load Testing

Both tools target `POST /queue` and write output to `loadtest-results/` (git-ignored).

**k6** (`Matchmaking.Test/loadtest.js`, 50 VUs × 1000 iterations, unique players):
```bash
Matchmaking.Test\k6-test.bat
# or: k6 run Matchmaking.Test/loadtest.js
```
Results are written via `handleSummary` to `k6-result.txt` and `k6-summary.json`.

**bombardier** (queue + leaderboard endpoints):
```bash
Matchmaking.Test\bombardier-test.bat
```
Edit the variables at the top of the `.bat` files to change connection/request counts or the bombardier path.

## Tests

```bash
dotnet test
```
Unit tests (`Matchmaking.Test`) cover the matching rule in `MatchmakingEngine`.

## Project Structure

```
MatchmakingSystem/
├── Matchmaking.Api/             # REST API + web UI (producer)
│   ├── Controllers/
│   │   └── MatchmakingController.cs
│   └── wwwroot/
│       └── index.html           # dashboard
├── Matchmaking.Worker/          # Background worker (consumer, scalable)
│   ├── MatchRequestConsumer.cs  # thin consumer
│   └── RedisMatchmaker.cs       # atomic Redis + Lua matching
├── Matchmaking.Shared/          # Shared models & events
│   ├── MatchRequest.cs
│   ├── MatchResult.cs
│   ├── MatchCompletedEvent.cs
│   └── MatchmakingEngine.cs     # in-memory matching rule (unit-tested reference)
├── Matchmaking.Test/            # Unit tests + load testing scripts
│   ├── MatchmakingEngineTest.cs
│   ├── loadtest.js
│   ├── k6-test.bat
│   └── bombardier-test.bat
└── docker-compose.yml
```

## RabbitMQ Management UI

After `docker compose up`, visit `http://localhost:15672` to monitor queues and events.
Default credentials: `guest` / `guest`
```
