import express, { Request, Response } from 'express';
import cors from 'cors';
import path from 'path';
import { fileURLToPath } from 'url';
import fs from 'fs';
import { randomUUID } from 'crypto';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const app = express();
const PORT = 3000;

app.use(cors());
app.use(express.json());

// In-memory Job Engine State
export interface TimelineEvent {
  event: string;
  timestamp: string;
  durationMs?: number;
  details?: string;
}

interface Job {
  id: string;
  queueName: string;
  payload: string;
  status: 'Queued' | 'Processing' | 'Completed' | 'Failed' | 'DeadLettered' | 'Cancelled';
  jobType: string;
  retryCount: number;
  maxRetries: number;
  deadLetterReason?: string;
  createdAt: string;
  updatedAt: string;
  timeline: TimelineEvent[];
}

const initialJobs: Job[] = [
  {
    id: randomUUID(),
    queueName: 'emails',
    payload: JSON.stringify({ to: 'developer@example.com', template: 'welcome_v2', priority: 'high' }),
    status: 'Completed',
    jobType: 'Default',
    retryCount: 0,
    maxRetries: 3,
    createdAt: new Date(Date.now() - 1000 * 180).toISOString(),
    updatedAt: new Date(Date.now() - 1000 * 150).toISOString(),
    timeline: [
      { event: 'Enqueued', timestamp: new Date(Date.now() - 1000 * 180).toISOString(), details: 'Accepted into memory channel' },
      { event: 'Processing', timestamp: new Date(Date.now() - 1000 * 179).toISOString(), details: 'Assigned to embedded-worker-1' },
      { event: 'Completed', timestamp: new Date(Date.now() - 1000 * 150).toISOString(), durationMs: 29000, details: 'Email delivered via SMTP gateway' },
    ],
  },
  {
    id: randomUUID(),
    queueName: 'webhooks',
    payload: JSON.stringify({ targetUrl: 'https://api.github.com/events', method: 'POST', event: 'push' }),
    status: 'Completed',
    jobType: 'Webhook',
    retryCount: 0,
    maxRetries: 3,
    createdAt: new Date(Date.now() - 1000 * 120).toISOString(),
    updatedAt: new Date(Date.now() - 1000 * 90).toISOString(),
    timeline: [
      { event: 'Enqueued', timestamp: new Date(Date.now() - 1000 * 120).toISOString(), details: 'SSRF validation passed' },
      { event: 'Processing', timestamp: new Date(Date.now() - 1000 * 119).toISOString(), details: 'Assigned to embedded-worker-1' },
      { event: 'Completed', timestamp: new Date(Date.now() - 1000 * 90).toISOString(), durationMs: 29000, details: 'HTTP 200 OK received' },
    ],
  },
  {
    id: randomUUID(),
    queueName: 'reports',
    payload: JSON.stringify({ type: 'daily_audit', format: 'pdf', compress: true }),
    status: 'Completed',
    jobType: 'Default',
    retryCount: 0,
    maxRetries: 3,
    createdAt: new Date(Date.now() - 1000 * 60).toISOString(),
    updatedAt: new Date(Date.now() - 1000 * 35).toISOString(),
    timeline: [
      { event: 'Enqueued', timestamp: new Date(Date.now() - 1000 * 60).toISOString(), details: 'Accepted into memory channel' },
      { event: 'Processing', timestamp: new Date(Date.now() - 1000 * 59).toISOString(), details: 'Assigned to embedded-worker-1' },
      { event: 'Completed', timestamp: new Date(Date.now() - 1000 * 35).toISOString(), durationMs: 24000, details: 'PDF generated (1.4MB)' },
    ],
  },
  {
    id: randomUUID(),
    queueName: 'webhooks',
    payload: JSON.stringify({ targetUrl: 'https://hooks.slack.com/services/alert', method: 'POST', channel: '#ops' }),
    status: 'DeadLettered',
    jobType: 'Webhook',
    retryCount: 3,
    maxRetries: 3,
    deadLetterReason: 'HTTP 503 Service Unavailable (Max retries exhausted)',
    createdAt: new Date(Date.now() - 1000 * 45).toISOString(),
    updatedAt: new Date(Date.now() - 1000 * 20).toISOString(),
    timeline: [
      { event: 'Enqueued', timestamp: new Date(Date.now() - 1000 * 45).toISOString(), details: 'SSRF validation passed' },
      { event: 'Processing', timestamp: new Date(Date.now() - 1000 * 44).toISOString(), details: 'Attempt 1 failed (HTTP 503)' },
      { event: 'Processing', timestamp: new Date(Date.now() - 1000 * 35).toISOString(), details: 'Attempt 2 failed (HTTP 503)' },
      { event: 'DeadLettered', timestamp: new Date(Date.now() - 1000 * 20).toISOString(), details: 'Exhausted 3 retries. Moved to Dead Letter Queue.' },
    ],
  },
];

const jobs: Job[] = [...initialJobs];
let totalProcessed = jobs.filter(j => j.status === 'Completed').length;
let windowProcessedCount = 0;

// Active async timers per job so cancellation cooperatively stops execution
const inFlightTimers = new Map<string, NodeJS.Timeout[]>();

// Rolling throughput history (last 20 data points, updated every 2s)
interface ThroughputPoint {
  timestamp: string;
  throughput: number; // jobs / sec
  queueSize: number;
}
const throughputHistory: ThroughputPoint[] = [];

// Track rolling throughput
setInterval(() => {
  const throughput = Math.round((windowProcessedCount / 2) * 10) / 10;
  windowProcessedCount = 0;
  const currentQueueSize = jobs.filter(j => j.status === 'Queued' || j.status === 'Processing').length;

  throughputHistory.push({
    timestamp: new Date().toLocaleTimeString(),
    throughput,
    queueSize: currentQueueSize,
  });

  if (throughputHistory.length > 20) {
    throughputHistory.shift();
  }
}, 2000);

// Connected SSE clients
const jobEventClients = new Set<Response>();
const metricsClients = new Set<Response>();

function broadcastJobEnqueued(job: Job) {
  const data = JSON.stringify(job);
  jobEventClients.forEach(client => {
    client.write(`event: job_enqueued\ndata: ${data}\n\n`);
  });
}

function broadcastJobUpdated(job: Job) {
  const data = JSON.stringify(job);
  jobEventClients.forEach(client => {
    client.write(`event: job_updated\ndata: ${data}\n\n`);
  });
}

function cancelJobInFlight(jobId: string) {
  const timers = inFlightTimers.get(jobId);
  if (timers) {
    timers.forEach(t => clearTimeout(t));
    inFlightTimers.delete(jobId);
  }
}

function processJobAsync(job: Job) {
  cancelJobInFlight(job.id);
  const timers: NodeJS.Timeout[] = [];

  const startTimer = setTimeout(() => {
    if (job.status === 'Cancelled') return;

    job.status = 'Processing';
    job.updatedAt = new Date().toISOString();
    job.timeline.push({
      event: 'Processing',
      timestamp: job.updatedAt,
      details: `Picked up by worker (Attempt ${job.retryCount + 1} of ${job.maxRetries})`,
    });
    broadcastJobUpdated(job);

    const executionDuration = Math.floor(Math.random() * 800) + 600;
    const executionStartTime = Date.now();

    const finishTimer = setTimeout(() => {
      if (job.status === 'Cancelled') return;

      const elapsed = Date.now() - executionStartTime;
      const success = Math.random() > 0.15;

      if (success) {
        job.status = 'Completed';
        totalProcessed++;
        windowProcessedCount++;
        job.deadLetterReason = undefined;
        job.timeline.push({
          event: 'Completed',
          timestamp: new Date().toISOString(),
          durationMs: elapsed,
          details: 'Execution succeeded without error',
        });
      } else {
        job.retryCount++;
        if (job.retryCount >= job.maxRetries) {
          job.status = 'DeadLettered';
          job.deadLetterReason = 'Downstream endpoint failure (Exceeded max retries)';
          job.timeline.push({
            event: 'DeadLettered',
            timestamp: new Date().toISOString(),
            durationMs: elapsed,
            details: `Failed attempt ${job.retryCount}. Moved to Dead-Letter Queue.`,
          });
        } else {
          job.status = 'Failed';
          job.deadLetterReason = `Simulated transient error on attempt ${job.retryCount}`;
          job.timeline.push({
            event: 'Failed',
            timestamp: new Date().toISOString(),
            durationMs: elapsed,
            details: `Failed attempt ${job.retryCount}/${job.maxRetries}. Will retry.`,
          });
        }
      }

      job.updatedAt = new Date().toISOString();
      inFlightTimers.delete(job.id);
      broadcastJobUpdated(job);
    }, executionDuration);

    timers.push(finishTimer);
  }, 400);

  timers.push(startTimer);
  inFlightTimers.set(job.id, timers);
}

// SSRF Safety Filter matching Core/Security/SsrfProtectionFilter.cs
function isSafeUrl(rawUrl: string): { safe: boolean; reason?: string } {
  try {
    const url = new URL(rawUrl);
    if (url.protocol !== 'http:' && url.protocol !== 'https:') {
      return { safe: false, reason: 'Only http and https schemes are permitted' };
    }
    const host = url.hostname.toLowerCase();
    if (
      host === 'localhost' ||
      host === '127.0.0.1' ||
      host === '::1' ||
      host === '169.254.169.254' ||
      host.endsWith('.local') ||
      host.endsWith('.internal') ||
      host.startsWith('10.') ||
      host.startsWith('192.168.') ||
      (host.startsWith('172.') && parseInt(host.split('.')[1] || '0', 10) >= 16 && parseInt(host.split('.')[1] || '0', 10) <= 31)
    ) {
      return { safe: false, reason: `Host '${host}' is blocked by SSRF protection filter` };
    }
    return { safe: true };
  } catch (err: any) {
    return { safe: false, reason: 'Invalid URL format' };
  }
}

// ----------------- API Endpoints -----------------

// Health check
app.get('/health', (_req: Request, res: Response) => {
  res.json({
    status: 'healthy',
    timestamp: new Date().toISOString(),
    mode: 'embedded',
    engine: 'TaskForge v2.0 (High-Performance Async Channel Engine)',
  });
});

// Prometheus metrics
app.get('/metrics', (_req: Request, res: Response) => {
  const queuedCount = jobs.filter(j => j.status === 'Queued').length;
  const processingCount = jobs.filter(j => j.status === 'Processing').length;
  const completedCount = jobs.filter(j => j.status === 'Completed').length;
  const failedCount = jobs.filter(j => j.status === 'Failed').length;
  const deadLetterCount = jobs.filter(j => j.status === 'DeadLettered').length;

  res.setHeader('Content-Type', 'text/plain');
  res.send(
    `# HELP taskforge_jobs_total Total number of jobs enqueued\n` +
    `# TYPE taskforge_jobs_total counter\n` +
    `taskforge_jobs_total ${jobs.length}\n` +
    `# HELP taskforge_jobs_completed_total Total completed jobs\n` +
    `# TYPE taskforge_jobs_completed_total counter\n` +
    `taskforge_jobs_completed_total ${completedCount}\n` +
    `# HELP taskforge_queue_depth Current queue depth\n` +
    `# TYPE taskforge_queue_depth gauge\n` +
    `taskforge_queue_depth{status="queued"} ${queuedCount}\n` +
    `taskforge_queue_depth{status="processing"} ${processingCount}\n` +
    `taskforge_queue_depth{status="failed"} ${failedCount}\n` +
    `taskforge_queue_depth{status="dead_letter"} ${deadLetterCount}\n`
  );
});

// Get all jobs
app.get('/api/v1/jobs', (_req: Request, res: Response) => {
  res.json(jobs.slice(0, 100));
});

// Enqueue standard job
app.post('/api/v1/jobs/enqueue', (req: Request, res: Response) => {
  const { queueName = 'default', payload = '{}', maxRetries = 3, namespace = 'default', tenantId } = req.body || {};

  const job: Job = {
    id: randomUUID(),
    queueName: queueName || 'default',
    payload: typeof payload === 'object' ? JSON.stringify(payload) : payload,
    status: 'Queued',
    jobType: 'Default',
    retryCount: 0,
    maxRetries: Number(maxRetries) || 3,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    timeline: [
      { event: 'Enqueued', timestamp: new Date().toISOString(), details: 'Accepted into memory channel' },
    ],
  };

  jobs.unshift(job);
  broadcastJobEnqueued(job);
  processJobAsync(job);

  res.status(202).json({
    jobId: job.id,
    queueName: job.queueName,
    status: job.status,
    enqueuedAt: job.createdAt,
    namespace,
    tenantId,
    jobType: job.jobType,
  });
});

// Enqueue webhook job with SSRF protection
app.post('/api/v1/jobs/webhook', (req: Request, res: Response) => {
  const {
    queueName = 'webhooks',
    targetUrl,
    method = 'POST',
    headers = {},
    body = '',
    maxRetries = 3,
    namespace = 'default',
    tenantId,
  } = req.body || {};

  if (!targetUrl) {
    res.status(400).json({ error: 'targetUrl is required' });
    return;
  }

  const ssrfCheck = isSafeUrl(targetUrl);
  if (!ssrfCheck.safe) {
    res.status(400).json({ error: `SSRF Blocked: ${ssrfCheck.reason}` });
    return;
  }

  const job: Job = {
    id: randomUUID(),
    queueName: queueName || 'webhooks',
    payload: JSON.stringify({ targetUrl, method, headers, body }),
    status: 'Queued',
    jobType: 'Webhook',
    retryCount: 0,
    maxRetries: Number(maxRetries) || 3,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    timeline: [
      { event: 'Enqueued', timestamp: new Date().toISOString(), details: 'SSRF validation passed. Enqueued to webhook worker.' },
    ],
  };

  jobs.unshift(job);
  broadcastJobEnqueued(job);
  processJobAsync(job);

  res.status(202).json({
    jobId: job.id,
    queueName: job.queueName,
    status: job.status,
    enqueuedAt: job.createdAt,
    namespace,
    tenantId,
    jobType: job.jobType,
  });
});

// Enqueue scheduled job
app.post('/api/v1/jobs/scheduled', (req: Request, res: Response) => {
  const { queueName = 'default', payload = '{}', cronExpression, maxRetries = 3 } = req.body || {};

  if (!cronExpression) {
    res.status(400).json({ error: 'cronExpression is required' });
    return;
  }

  const job: Job = {
    id: randomUUID(),
    queueName: queueName || 'default',
    payload: typeof payload === 'object' ? JSON.stringify(payload) : payload,
    status: 'Queued',
    jobType: 'Scheduled',
    retryCount: 0,
    maxRetries: Number(maxRetries) || 3,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    timeline: [
      { event: 'Enqueued', timestamp: new Date().toISOString(), details: `Scheduled with cron '${cronExpression}'` },
    ],
  };

  jobs.unshift(job);
  broadcastJobEnqueued(job);
  processJobAsync(job);

  res.status(202).json({
    jobId: job.id,
    queueName: job.queueName,
    status: job.status,
    cronExpression,
    enqueuedAt: job.createdAt,
  });
});

// Retry / Replay a specific job (Failed or DeadLettered)
app.post('/api/v1/jobs/:jobId/retry', (req: Request, res: Response) => {
  const job = jobs.find(j => j.id === req.params.jobId);
  if (!job) {
    res.status(404).json({ error: 'Job not found' });
    return;
  }

  job.status = 'Queued';
  job.retryCount = 0;
  job.deadLetterReason = undefined;
  job.updatedAt = new Date().toISOString();
  job.timeline.push({
    event: 'Replayed',
    timestamp: job.updatedAt,
    details: 'Manual retry triggered from dashboard/API. Re-enqueued to active channel.',
  });

  broadcastJobUpdated(job);
  processJobAsync(job);

  res.json({
    success: true,
    message: `Job ${job.id} replayed successfully`,
    job,
  });
});

// Cancel an active or queued job
app.post('/api/v1/jobs/:jobId/cancel', (req: Request, res: Response) => {
  const job = jobs.find(j => j.id === req.params.jobId);
  if (!job) {
    res.status(404).json({ error: 'Job not found' });
    return;
  }

  if (job.status === 'Completed') {
    res.status(400).json({ error: 'Cannot cancel an already completed job' });
    return;
  }

  cancelJobInFlight(job.id);
  job.status = 'Cancelled';
  job.updatedAt = new Date().toISOString();
  job.timeline.push({
    event: 'Cancelled',
    timestamp: job.updatedAt,
    details: 'Cooperative cancellation requested. Worker execution halted.',
  });

  broadcastJobUpdated(job);

  res.json({
    success: true,
    message: `Job ${job.id} cancelled successfully`,
    job,
  });
});

// Bulk replay all Dead-Lettered jobs
app.post('/api/v1/jobs/dlq/replay-all', (_req: Request, res: Response) => {
  const deadLetterJobs = jobs.filter(j => j.status === 'DeadLettered' || j.status === 'Failed');
  const count = deadLetterJobs.length;

  for (const job of deadLetterJobs) {
    job.status = 'Queued';
    job.retryCount = 0;
    job.deadLetterReason = undefined;
    job.updatedAt = new Date().toISOString();
    job.timeline.push({
      event: 'Replayed',
      timestamp: job.updatedAt,
      details: 'Bulk DLQ recovery replayed job.',
    });
    broadcastJobUpdated(job);
    processJobAsync(job);
  }

  res.json({
    success: true,
    replayedCount: count,
    message: `Replayed ${count} dead-lettered job(s)`,
  });
});

// Get job by ID
app.get('/api/v1/jobs/:jobId', (req: Request, res: Response) => {
  const job = jobs.find(j => j.id === req.params.jobId);
  if (!job) {
    res.status(404).json({ error: 'Job not found' });
    return;
  }
  res.json(job);
});

// Workflows endpoints
const workflows = [
  {
    id: randomUUID(),
    name: 'Order Processing Pipeline',
    status: 'Active',
    stepsCount: 4,
    createdAt: new Date().toISOString(),
  },
  {
    id: randomUUID(),
    name: 'Nightly Data Reconciliation',
    status: 'Active',
    stepsCount: 3,
    createdAt: new Date().toISOString(),
  },
];

app.get('/api/v1/workflows', (_req: Request, res: Response) => {
  res.json(workflows);
});

app.post('/api/v1/workflows', (req: Request, res: Response) => {
  const workflow = {
    id: randomUUID(),
    name: req.body?.name || 'Untitled Workflow',
    status: 'Draft',
    stepsCount: req.body?.steps?.length || 1,
    createdAt: new Date().toISOString(),
  };
  workflows.push(workflow);
  res.status(201).json(workflow);
});

// ----------------- Server-Sent Events (SSE) -----------------

// Real-time Queue & Worker Metrics SSE Stream
app.get('/api/v1/metrics/stream', (req: Request, res: Response) => {
  res.setHeader('Content-Type', 'text/event-stream');
  res.setHeader('Cache-Control', 'no-cache');
  res.setHeader('Connection', 'keep-alive');
  res.setHeader('X-Accel-Buffering', 'no');
  res.flushHeaders();

  metricsClients.add(res);

  const sendMetrics = () => {
    const queuedCount = jobs.filter(j => j.status === 'Queued' || j.status === 'Processing').length;
    const metricsPayload = {
      queueSize: queuedCount,
      activeWorkers: 1,
      timestamp: new Date().toISOString(),
      mode: 'embedded',
      processedCount: totalProcessed,
      throughputHistory: [...throughputHistory],
    };
    res.write(`data: ${JSON.stringify(metricsPayload)}\n\n`);
  };

  sendMetrics();
  const intervalId = setInterval(sendMetrics, 2000);

  req.on('close', () => {
    clearInterval(intervalId);
    metricsClients.delete(res);
  });
});

// Real-time Job Events SSE Stream
app.get('/api/v1/jobs/stream', (req: Request, res: Response) => {
  res.setHeader('Content-Type', 'text/event-stream');
  res.setHeader('Cache-Control', 'no-cache');
  res.setHeader('Connection', 'keep-alive');
  res.setHeader('X-Accel-Buffering', 'no');
  res.flushHeaders();

  jobEventClients.add(res);

  // Send keepalive comment ping every 15 seconds
  const pingInterval = setInterval(() => {
    res.write(': ping\n\n');
  }, 15000);

  req.on('close', () => {
    clearInterval(pingInterval);
    jobEventClients.delete(res);
  });
});

// ----------------- Static UI Hosting & SPA Fallback -----------------

const staticDirs = [
  path.join(__dirname, 'taskforge-ui', 'dist'),
  path.join(__dirname, 'dist'),
  path.join(__dirname, 'src', 'TaskForge.Api', 'wwwroot'),
];

let primaryStaticDir = staticDirs.find(d => fs.existsSync(d) && fs.existsSync(path.join(d, 'index.html')));

if (!primaryStaticDir) {
  primaryStaticDir = path.join(__dirname, 'src', 'TaskForge.Api', 'wwwroot');
}

console.log(`[TaskForge] Serving UI static assets from: ${primaryStaticDir}`);
app.use(express.static(primaryStaticDir));

app.get('*', (req: Request, res: Response, next) => {
  if (req.path.startsWith('/api') || req.path.startsWith('/health') || req.path.startsWith('/metrics')) {
    return next();
  }
  const indexPath = path.join(primaryStaticDir!, 'index.html');
  if (fs.existsSync(indexPath)) {
    res.sendFile(indexPath);
  } else {
    res.status(404).send('TaskForge Dashboard assets not found. Run npm run build first.');
  }
});

app.listen(PORT, '0.0.0.0', () => {
  console.log(`=============================================================`);
  console.log(` TaskForge v2.0 Live Engine running on http://0.0.0.0:${PORT}`);
  console.log(` Live Dashboard: http://localhost:${PORT}`);
  console.log(` Health Status:  http://localhost:${PORT}/health`);
  console.log(` SSE Metrics:    http://localhost:${PORT}/api/v1/metrics/stream`);
  console.log(` SSE Jobs:       http://localhost:${PORT}/api/v1/jobs/stream`);
  console.log(`=============================================================`);
});
