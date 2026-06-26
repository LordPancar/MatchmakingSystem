# MatchmakingSystem

Distributed matchmaking system built with .NET 10, RabbitMQ, Redis, and Docker.

## Architecture

```
Client
  │
  ▼
Matchmaking.Api  ──(MatchRequest)──►  Matchmaking.Worker
  │                                          │
  │                                          ├──► Redis (leaderboard)
  │                                          │
  │                                          └──► RabbitMQ (MatchCompletedEvent)
  │
  └────────────────(Redis)────────── GET /leaderboard
```

- **API** — receives match requests via REST, publishes `MatchRequest` messages to RabbitMQ
- **Worker** — consumes `MatchRequest` messages, runs matchmaking algorithm, writes results to Redis, publishes `MatchCompletedEvent`
- **RabbitMQ** — async message broker (loose coupling between API and Worker)
- **Redis** — stores leaderboard data as a sorted set

## Tech Stack

- .NET 10 (ASP.NET Core Web API + Worker Service)
- MassTransit 8.x + RabbitMQ
- StackExchange.Redis
- Docker + Docker Compose

## Getting Started

**Prerequisites:** Docker Desktop

```bash
docker-compose up --build
```

This starts 4 containers:

| Container | Description | Port |
|-----------|-------------|------|
| `rabbitmq` | Message broker | `15672` (management UI) |
| `redis` | Leaderboard store | `6379` |
| `matchmaking-api` | REST API | `8080` |
| `matchmaking-worker` | Background consumer | — |

## API Endpoints

### Queue a match request
```
POST http://localhost:8080/api/matchmaking/queue
Content-Type: application/json

{
  "userId": "player1",
  "score": 1500
}
```

Response:
```json
{
  "message": "Kuyruğa alındı.",
  "requestId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

### Get leaderboard
```
GET http://localhost:8080/api/matchmaking/leaderboard
```

Response:
```json
[
  { "userId": "player1", "score": 1500 },
  { "userId": "player2", "score": 1420 }
]
```

## How Matchmaking Works

1. Client sends `POST /queue` with a `userId` and `score`
2. API publishes a `MatchRequest` message to RabbitMQ and immediately returns
3. Worker picks up the message and adds the player to an in-memory queue
4. Worker sorts players by score and pairs any two players whose score difference is ≤ 100
5. Matched players are written to the Redis leaderboard sorted set
6. Worker publishes a `MatchCompletedEvent` to RabbitMQ for each successful match

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

## Project Structure

```
MatchmakingSystem/
├── Matchmaking.Api/          # REST API (producer)
│   └── Controllers/
│       └── MatchmakingController.cs
├── Matchmaking.Worker/       # Background worker (consumer)
│   ├── MatchRequestConsumer.cs
│   └── Worker.cs
├── Matchmaking.Shared/       # Shared models & events
│   ├── MatchRequest.cs
│   ├── MatchResult.cs
│   ├── MatchCompletedEvent.cs
│   └── MatchmakingEngine.cs
└── docker-compose.yml
```

## RabbitMQ Management UI

After running `docker-compose up`, visit `http://localhost:15672` to monitor queues and events.

Default credentials: `guest` / `guest`
