# TaskForge: Background Job Processing Engine Design Document

## 📜 Overview

TaskForge is designed to be a lightweight, distributed, and highly available background job processing engine built on modern .NET 8 practices. It decouples job ingestion, queue management, and worker processing using a combination of PostgreSQL for persistent state and Redis for high-speed, ephemeral queue communication.

---

## 🧱 1. System Component Boundaries

The system is decomposed into four primary services/components, adhering to clean architecture principles for maximum separation of concerns.

### 1.1. `TaskForge.Api` (.NET 8 Web API)
*   **Purpose:** The primary ingress point for the system. Responsible for accepting new job requests, interacting with the persistent storage (PostgreSQL), and streaming real-time status updates.
*   **Key Responsibilities:**
    *   **Job Ingestion:** Provides REST endpoints (`POST /api/jobs`) to accept job payloads and initiate the job record in PostgreSQL.
    *   **SSE Streaming:** Implements Server-Sent Events (SSE) endpoint (`GET /api/jobs/stream/{jobId}`) to stream real-time status updates (e.g., `QUEUED`, `PROCESSING`, `COMPLETED`, `FAILED`) for a specific job ID.
    *   **Health Checks:** Provides standard API health check endpoints.
*   **Technology Focus:** Minimal business logic; primarily orchestration and data mapping.

### 1.2. `TaskForge.Worker` (.NET 8 IHostedService)
*   **Purpose:** The dedicated background processing unit responsible for consuming jobs from the queues.
*   **Key Responsibilities:**
    *   **Queue Polling:** Periodically polls Redis queues (`taskforge:queue:{queue_name}`) for available job IDs/payloads.
    *   **Atomic Processing:** On retrieval, it must atomically attempt to claim the job, updating its status in PostgreSQL to `PROCESSING` and marking it as currently owned by the worker instance ID.
    *   **Execution:** Deserializes the payload and executes the registered job logic.
    *   **State Management:** Updates the job status in PostgreSQL upon completion or failure.
    *   **Heartbeat:** Periodically updates its own worker heartbeat in Redis to signal liveness.
*   **Fault Tolerance:** Implements retries and manages the transition to the Dead Letter Queue (DLQ) upon exceeding maximum retry limits.

### 1.3. `TaskForge.Core` (Shared Library/Domain)
*   **Purpose:** A shared library containing immutable, strongly-typed domain models, interfaces, and enumerations used across all other components.
*   **Key Components:**
    *   **Models:** `JobPayload`, `JobStatus`, `JobRecord`.
    *   **Enums:** `JobStatus` (e.g., `QUEUED`, `PROCESSING`, `COMPLETED`, `FAILED`, `DEAD_LETTER`), `JobPriority`.
    *   **Interfaces:** Abstraction layer for job execution (`IJobProcessor<T>`) to allow component isolation and easy extension.

### 1.4. `taskforge-ui` (Single Page Application - Vite + React + Tailwind CSS)
*   **Purpose:** The user-facing dashboard for monitoring, triggering, and managing tasks.
*   **Key Responsibilities:**
    *   **Dashboard View:** Displaying a list of recent jobs, filtered by status.
    *   **Job Submission:** Providing a UI form to submit new jobs to the `TaskForge.Api`.
    *   **Live Monitoring:** Utilizing WebSockets or SSE (via the API) to subscribe to and display real-time status changes for specific jobs or queues.

---

## 💾 2. Database Schema (PostgreSQL)

The primary state management will reside in PostgreSQL to ensure transactional integrity and historical tracking.

### `jobs` Table

| Column Name | Data Type | Constraints / Description |
| :--- | :--- | :--- |
| **id** | `UUID` | **Primary Key**. Unique identifier for the job. |
| **queue_name** | `VARCHAR(100)` | The logical name of the queue (e.g., `image-processing`, `email-sending`). |
| **payload_json** | `JSONB` | The serialized payload data required for job execution. |
| **status** | `VARCHAR(50)` | The current job status (e.g., `QUEUED`, `PROCESSING`, `COMPLETED`). *Should reference `JobStatus` enum.* |
| **retry_count** | `INTEGER` | The number of times this job has been attempted. Default: 0. |
| **dead_letter_reason** | `TEXT` | Stores the reason if the job moved to the DLQ. Null if processing succeeded. |
| **created_at** | `TIMESTAMP WITH TIME ZONE` | Record creation timestamp. |
| **updated_at** | `TIMESTAMP WITH TIME ZONE` | Last status update timestamp. |
| **locked_by** | `UUID` | The ID of the worker currently holding this job, ensuring exclusive claim. |

**Indexes:**
*   Index on `status` (for fast querying of job types).
*   Index on `queue_name` (for grouping and list views).
*   Index on `updated_at` (for time-series queries).

---

## 🚀 3. Redis Key Strategy

Redis will be used as a fast, ephemeral message broker layer, separate from persistent state.

| Key Type | Key Format | Value Type / Structure | Purpose | Management |
| :--- | :--- | :--- | :--- | :--- |
| **Job Queues** | `taskforge:queue:{queue_name}` | **Redis List (LPUSH/BRPOP)**. The list stores `job_id`s (UUID strings). | Holds identifiers for jobs ready to be processed. Workers consume from the tail (`BRPOP`). | Jobs are removed from this list *after* successful consumption and *before* or *during* state update in PostgreSQL. |
| **Worker Heartbeats**| `taskforge:workers:{worker_id}` | **Redis String/Hash**. Stores `{worker_id: true}`. | Tracks the liveness and location of active workers. | Set with a **TTL of 10 seconds**. If the key expires, the worker is considered offline. |
| **Dead-Letter Queue** | `taskforge:dlq` | **Redis List (LPUSH/BRPOP)**. Stores `job_id`s (UUID strings). | Catches jobs that have exhausted their retries or failed fatally. | Requires manual intervention or a separate remediation worker process. |
