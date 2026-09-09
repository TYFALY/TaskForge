<p align="center">
  <img src="docs/logo.png" alt="TaskForge" width="180" />
</p>

<p align="center">
  <strong>High-Performance Asynchronous Job Engine for .NET 8</strong><br />
  Dual-mode design: Embedded (Channels + SQLite) or Distributed (Redis + PostgreSQL)
</p>

<p align="center">
  <a href="https://hub.docker.com/"><img src="https://img.shields.io/badge/Docker-Ready-2496ED?style=flat&logo=docker&logoColor=white" alt="Docker Ready" /></a>
  <a href="https://learn.microsoft.com/en-us/dotnet/core/"><img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat&logo=.net&logoColor=white" alt=".NET 8" /></a>
  <a href="https://playwright.dev/"><img src="https://img.shields.io/badge/Playwright-Tested-2EAD33?style=flat&logo=playwright&logoColor=white" alt="Playwright Tested" /></a>
  <a href="https://github.com/TYFALY/TaskForge/blob/main/LICENSE"><img src="https://img.shields.io/github/license/TYFALY/TaskForge?style=flat&color=blue" alt="License" /></a>
</p>

---

## Project Overview

**TaskForge** is a modern asynchronous job processing engine built for .NET 8.  
It supports two operating modes that share the same API and dashboard:

| Mode | Queue | Persistence | Best for |
|------|-------|-------------|----------|
| **Embedded** (default) | `System.Threading.Channels` (in-memory) | SQLite | Local development, demos, single-node deployments |
| **Distributed** | Redis | PostgreSQL | Horizontal scaling, higher durability |

Both modes feature webhook-first job execution, automatic retries, dead-letter handling, and a real-time React dashboard powered by Server-Sent Events (SSE).

### Key Characteristics

| Feature | Description |
|---------|-------------|
| Dual Mode | Switch between Embedded and Distributed with one configuration value |
| In-Memory Channels | Lock-free, high-throughput buffering in Embedded mode |
| Webhook-First | Execute HTTP webhooks with retries and dead-letter queues |
| Real-Time Dashboard | React + Tailwind UI with live SSE updates (no polling) |
| Zero-Config Default | Embedded mode works out of the box with SQLite |
| Docker-Ready | Single container (Embedded) or multi-service stack (Distributed) |

---

## Dashboard Preview

The TaskForge dashboard is a modern React + Tailwind CSS interface that provides real-time job monitoring without polling.

**Features**
- Live SSE updates for job status changes
- KPI cards (queue depth, throughput, success rate)
- Filterable and sortable job history
- Built-in job simulation controls for load testing
- Integration guide with pre-filled cURL / PowerShell snippets

```
Dashboard  ──SSE Stream──▶  TaskForge API
     ▲                           │
     └──── Instant updates ──────┘
```

---

## Quick Start (Embedded Mode – Recommended)

### Prerequisites
- Docker & Docker Compose

### 1. Clone the repository
```bash
git clone https://github.com/TYFALY/TaskForge.git
cd TaskForge
```

### 2. Generate an API key
```bash
# macOS / Linux
export TASKFORGE_API_KEY=$(openssl rand -hex 32)

# Windows (PowerShell)
$env:TASKFORGE_API_KEY = [Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }))
```

### 3. Start TaskForge
```bash
docker compose up --build -d
```

The API and dashboard will be available at `http://localhost:5000`.

### 4. Enqueue your first job
```bash
curl -X POST http://localhost:5000/api/v1/jobs/enqueue \
  -H "Content-Type: application/json" \
  -H "X-TaskForge-Key: $TASKFORGE_API_KEY" \
  -d '{
    "webhookUrl": "https://webhook.site/your-unique-id",
    "payload": {"message": "Hello from TaskForge!"},
    "priority": 5
  }'
```

### 5. Stream live events
```bash
curl -N http://localhost:5000/api/v1/events \
  -H "X-TaskForge-Key: $TASKFORGE_API_KEY"
```

---

## Distributed Mode (Optional)

For horizontal scaling with Redis and PostgreSQL:

```bash
export TASKFORGE_API_KEY=$(openssl rand -hex 32)
docker compose -f docker-compose.distributed.yml up --build -d
```

This starts:
- PostgreSQL
- Redis
- TaskForge API
- Two Worker replicas
---

## Architecture

### Embedded Mode (Default)
```
Client
  │
  ▼
TaskForge.Api
  ├── System.Threading.Channels (in-memory buffer)
  ├── Embedded Worker (background processor)
  └── SQLite (persistence)
  │
  ▼
External Webhook Targets
```

### Distributed Mode
```
Client
  │
  ▼
TaskForge.Api  ──▶  Redis Queue  ──▶  Worker(s)
  │                      │                │
  └──── PostgreSQL ◄─────┴────────────────┘
```

Both modes expose the same REST API and SSE endpoint. Mode is controlled by the `ExecutionMode` environment variable (`Embedded` or `Distributed`).

---

## API Reference

**Base URL:** `http://localhost:5000/api/v1`

**Authentication:** All endpoints (except `/health`) require the header:
```
X-TaskForge-Key: your-api-key
```

### Health Check
```bash
curl http://localhost:5000/health
```

### Enqueue Job
```bash
curl -X POST http://localhost:5000/api/v1/jobs/enqueue \
  -H "Content-Type: application/json" \
  -H "X-TaskForge-Key: $TASKFORGE_API_KEY" \
  -d '{
    "webhookUrl": "https://example.com/webhook",
    "payload": {"key": "value"},
    "priority": 5,
    "maxRetries": 3,
    "timeoutSeconds": 30
  }'
```

**Response**
```json
{
  "jobId": "tf_job_abc123xyz",
  "status": "enqueued",
  "enqueuedAt": "2024-01-15T10:30:00Z"
}
```

### Get Job Status
```bash
curl http://localhost:5000/api/v1/jobs/{jobId} \
  -H "X-TaskForge-Key: $TASKFORGE_API_KEY"
```

### List Jobs
```bash
curl "http://localhost:5000/api/v1/jobs?limit=50" \
  -H "X-TaskForge-Key: $TASKFORGE_API_KEY"

curl "http://localhost:5000/api/v1/jobs?status=completed&limit=100" \
  -H "X-TaskForge-Key: $TASKFORGE_API_KEY"
```

### Stream Live Events (SSE)
```bash
curl -N http://localhost:5000/api/v1/events \
  -H "X-TaskForge-Key: $TASKFORGE_API_KEY"
```

---

## Configuration

| Variable | Default | Description |
|----------|---------|-------------|
| `ExecutionMode` | `Embedded` | `Embedded` or `Distributed` |
| `TaskForge__ApiKey` | *(required)* | API authentication key |
| `TaskForge__Server__Port` | `8080` | Internal listening port |
| `Embedded:DatabasePath` | `taskforge.db` | SQLite file path (Embedded only) |
| `Redis__ConnectionString` | — | Required in Distributed mode |
| `Postgres__ConnectionString` | — | Required in Distributed mode |

---

## Testing

```bash
# .NET tests
dotnet test

# UI E2E tests (requires running server)
cd taskforge-ui
npm install
npx playwright install
npm test
```

---

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

---

## License

This project is licensed under the **MIT License** – see the [LICENSE](LICENSE) file for details.

---

<p align="center">
  Built with ❤️ for .NET developers
</p>
