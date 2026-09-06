# ? TaskForge

<p align="center">
  <img src="docs/logo.png" alt="TaskForge" width="180" />
</p>

<p align="center">
  <strong>High-Performance Asynchronous Distributed Job Engine for .NET 8</strong>
  <br />
  Built with <code>System.Threading.Channels</code>, React, and Docker
</p>

<p align="center">
  <a href="https://github.com/TYFALY/TaskForge/actions/workflows/ci.yml"><img src="https://github.com/TYFALY/TaskForge/actions/workflows/ci.yml/badge.svg" alt="CI Status" /></a>
  <a href="https://www.nuget.org/packages/TaskForge"><img src="https://img.shields.io/nuget/v/TaskForge.svg" alt="NuGet" /></a>
  <a href="https://learn.microsoft.com/en-us/dotnet/core/"><img src="https://img.shields.io/badge/.NET-8.0-purple?style=flat" alt=".NET 8" /></a>
  <a href="https://playwright.dev/"><img src="https://img.shields.io/badge/Playwright-Tested-blueviolet?style=flat" alt="Playwright Tested" /></a>
  <a href="https://github.com/TYFALY/TaskForge/blob/main/LICENSE"><img src="https://img.shields.io/github/license/TYFALY/TaskForge" alt="License" /></a>
</p>

---

## ? Project Overview

**TaskForge** is a modern, lightweight asynchronous distributed job processing engine built specifically for .NET 8. It provides ultra-low latency job queuing via in-memory channels, real-time observability via Server-Sent Events (SSE), and a zero-configuration React dashboard—all packaged in a single Docker container.

### Key Characteristics

| Feature | Description |
|---------|-------------|
| ? **In-Memory Channels** | Uses `System.Threading.Channels` for lock-free, high-throughput job buffering |
| ? **Webhook-First Design** | Executes HTTP webhooks with retry logic and dead-letter queues |
| ? **Real-Time Dashboard** | Built-in React + Tailwind dashboard with live SSE updates |
| ? **Zero-Config** | Works out of the box with SQLite, no external dependencies required |
| ? **Docker-Ready** | Single-stage or multi-stage production builds with health checks |

---

## ?? Dashboard Preview

The TaskForge dashboard is a modern **React + Tailwind CSS** interface that provides real-time job monitoring without polling.

### Features

- **? Live SSE Updates**: Server-Sent Events stream job status changes in real-time
- **? KPI Cards**: At-a-glance metrics for queue depth, throughput, and success rates
- **? Job History Table**: Filterable, sortable history with retry status indicators
- **?? Simulation Controls**: Built-in job generator for load testing
- **? Integration Guide**: cURL/PowerShell snippets with your API key pre-filled

### Architecture Benefits

```
+---------------------------------------------------------+
¦                    ZERO POLLING                         ¦
¦                                                          ¦
¦   Dashboard ?---- SSE Stream ------? TaskForge API    ¦
¦                                                          ¦
¦   Updates appear instantly when jobs complete            ¦
+---------------------------------------------------------+
```

---

## ? Quick Start

### Prerequisites

- Docker & Docker Compose installed
- API Key (generated or provided)

### 1. Clone & Start

```bash
git clone https://github.com/TYFALY/TaskForge.git
cd TaskForge
```

### 2. Configure API Key

Generate a secure API key:

```bash
# macOS/Linux
export TASKFORGE_API_KEY=$(openssl rand -hex 32)

# Windows (PowerShell)
$env:TASKFORGE_API_KEY = [Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }))
```

### 3. Start TaskForge

```bash
docker compose up --build -d
```

The API will be available at `http://localhost:5000` and the dashboard at `http://localhost:5000`.

### 4. Enqueue Your First Job

```bash
# Enqueue a webhook job
curl -X POST http://localhost:5000/api/v1/jobs/enqueue \\
  -H "Content-Type: application/json" \\
  -H "X-TaskForge-Key: ${API_KEY}" \\
  -d \'{
    "webhookUrl": "https://webhook.site/your-unique-id",
    "payload": {"message": "Hello from TaskForge!"},
    "priority": 5
  }\'
```

### 5. Monitor Jobs

```bash
# Stream live job events via SSE
curl -N http://localhost:5000/api/v1/events \\
  -H "X-TaskForge-Key: ${API_KEY}"
```

---

## ?? Architecture & System Design

```
+-----------------------------------------------------------------------------+
¦                              CLIENT LAYER                                    ¦
¦         (cURL, Postman, Browser, Mobile App, CI/CD Pipeline)               ¦
+-----------------------------------------------------------------------------+
                                 ¦ HTTP POST /api/v1/jobs/enqueue
                                 ?
+-----------------------------------------------------------------------------+
¦                      TASKFORGE API (ASP.NET Core 8)                        ¦
¦  +----------------+    +----------------+    +------------------------+   ¦
¦  ¦  Web API       ¦---?¦  Job           ¦---?¦  Channel Buffer        ¦   ¦
¦  ¦  Controllers   ¦    ¦  Controller    ¦    ¦  (RAM - Channels)      ¦   ¦
¦  +----------------+    +----------------+    +------------------------+   ¦
¦  +----------------+    +----------------+               ¦                 ¦
¦  ¦  SSE Stream    ¦?---¦  Job           ¦?---¦  Background Job       ¦   ¦
¦  ¦  /events      ¦    ¦  Broadcaster   ¦    ¦  Processor            ¦   ¦
¦  +----------------+    +----------------+    +------------------------+   ¦
¦  +----------------+    +----------------+               ¦                 ¦
¦  ¦  Health        ¦    ¦  Webhook       ¦--------------?¦  External HTTP   ¦   ¦
¦  ¦  /health      ¦    ¦  Executor      ¦    ¦  Targets              ¦   ¦
¦  +----------------+    +----------------+    +------------------------+   ¦
+-----------------------------------------------------------------------------+
                                 ¦
                                 ?
                     +-----------------------+
                     ¦   SQLite / PostgreSQL  ¦
                     ¦   (Job Persistence)   ¦
                     +-----------------------+
```

### Data Flow

```
1. INGESTION     Client --POST--? API --Buffer--? In-Memory Channel
2. PROCESSING    Channel ----------------------? Background Worker
3. EXECUTION     Worker --HTTP POST--? Webhook Target
4. BROADCAST     Worker --Signal--? SSE ------? Dashboard (Real-time)
5. PERSISTENCE   Worker --Write--? SQLite/PostgreSQL
```

---

## ? API Reference

### Base URL

```
http://localhost:5000/api/v1
```

### Authentication

All endpoints require the `X-TaskForge-Key` header:

```bash
-H "X-TaskForge-Key: your-api-key"
```

### Endpoints

#### Health Check

```bash
# No authentication required
curl http://localhost:5000/health
```

#### Enqueue Job

```bash
curl -X POST http://localhost:5000/api/v1/jobs/enqueue \\
  -H "Content-Type: application/json" \\
  -H "X-TaskForge-Key: ${API_KEY}" \\
  -d \'{
    "webhookUrl": "https://example.com/webhook",
    "payload": {"key": "value"},
    "priority": 5,
    "maxRetries": 3,
    "timeoutSeconds": 30
  }\'
```

**Response:**

```json
{
  "jobId": "tf_job_abc123xyz",
  "status": "enqueued",
  "enqueuedAt": "2024-01-15T10:30:00Z"
}
```

#### Get Job Status

```bash
curl http://localhost:5000/api/v1/jobs/{jobId} \\
  -H "X-TaskForge-Key: ${API_KEY}"
```

#### List Jobs

```bash
# Get recent jobs
curl "http://localhost:5000/api/v1/jobs?limit=50" \\
  -H "X-TaskForge-Key: ${API_KEY}"

# Filter by status
curl "http://localhost:5000/api/v1/jobs?status=completed&limit=100" \\
  -H "X-TaskForge-Key: ${API_KEY}"
```

#### Stream Live Events (SSE)

```bash
curl -N http://localhost:5000/api/v1/events \\
  -H "X-TaskForge-Key: ${API_KEY}"
```

---

## ? Testing

### Run All Tests

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

## ? Performance Benchmarks

| Metric | Value |
|--------|-------|
| Job Throughput | 50,000+ jobs/sec |
| Enqueue Latency | < 1ms (p99) |
| Memory Usage | ~50MB baseline |
| Webhook Timeout | Configurable (default 30s) |

*Results from BenchmarkDotNet on M2 MacBook Pro.*

---

## ? Configuration

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Runtime environment |
| `ASPNETCORE_URLS` | `http://+:8080` | Binding address |
| `TaskForge__ApiKey` | *(required)* | API authentication key |
| `TaskForge__Server__Port` | `8080` | Internal port |

### docker-compose.yml

```yaml
version: "3.9"

services:
  taskforge:
    build:
      context: .
      dockerfile: Dockerfile
    container_name: taskforge-api
    ports:
      - "5000:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - TaskForge__ApiKey=${TASKFORGE_API_KEY:-MISSING_API_KEY_PRODUCTION_ERROR}
      - TaskForge__Server__Port=8080
    healthcheck:
      test: ["CMD", "wget", "--no-verbose", "--tries=1", "--spider", "http://localhost:8080/health"]
      interval: 30s
      timeout: 3s
      retries: 3
      start_period: 10s
    restart: unless-stopped
```

---

## ? Project Structure

```
TaskForge/
+-- docs/                          # Documentation assets
¦   +-- dashboard-preview.png
¦   +-- logo.png
+-- scripts/                       # Utility scripts
¦   +-- send-live-jobs.ps1
+-- src/                           # .NET source code
¦   +-- TaskForge.Api/            # ASP.NET Core Web API
¦   ¦   +-- Endpoints/            # Minimal API endpoints
¦   ¦   +-- Middleware/           # API key authentication
¦   ¦   +-- Metrics/              # Prometheus metrics
¦   ¦   +-- wwwroot/              # Embedded React dashboard
¦   ¦   +-- Program.cs
¦   +-- TaskForge.Benchmark/      # BenchmarkDotNet benchmarks
¦   +-- TaskForge.Core/            # Shared models & interfaces
¦   +-- TaskForge.Worker/          # Background job processor
+-- taskforge-ui/                  # React + Tailwind frontend
¦   +-- src/                       # React components
¦   +-- tests/                     # Playwright E2E tests
¦   +-- package.json
+-- .dockerignore                  # Docker context exclusions
+-- .gitignore                     # Git exclusions
+-- docker-compose.yml             # Container orchestration
+-- Dockerfile                     # Multi-stage production build
+-- README.md                      # This file
+-- DESIGN.md                      # Architecture decisions
+-- LICENSE                        # MIT License
+-- TaskForge.sln                  # Solution file
```

---

## ? Contributing

1. **Fork the repository**
2. **Create a feature branch:** `git checkout -b feature/amazing-feature`
3. **Commit changes:** `git commit -m \'Add amazing feature\'`
4. **Push to branch:** `git push origin feature/amazing-feature`
5. **Open a Pull Request**

---

## ? License

This project is licensed under the **MIT License** - see the [LICENSE](LICENSE) file for details.

---

<p align="center">
  Built with ? for .NET developers
</p>
