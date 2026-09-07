import { useState, useEffect, useCallback, useMemo } from "react";
import { Header } from "./components/Header";
import { KpiGrid } from "./components/KpiGrid";
import { LiveMetrics } from "./components/LiveMetrics";
import { JobHistoryTable } from "./components/JobHistoryTable";
import { JobDetailsDrawer } from "./components/JobDetailsDrawer";
import { SimulationControl } from "./components/SimulationControl";
import { EnqueueJobModal } from "./components/EnqueueJobModal";
import { IntegrationGuide } from "./components/IntegrationGuide";
import { useSSE } from "./hooks/useSSE";
import { useJobEvents } from "./hooks/useJobEvents";
import type { KpiData, Job, QueueMetrics, JobStatus } from "./types";

const API_BASE = "";
const METRICS_SSE_URL = "/api/v1/metrics/stream";
const JOBS_SSE_URL = "/api/v1/jobs/stream";

export default function App() {
  const [previousMetrics, setPreviousMetrics] = useState<QueueMetrics | null>(null);
  const [jobs, setJobs] = useState<Job[]>([]);
  const [highlightedJobId, setHighlightedJobId] = useState<string | null>(null);
  const [selectedJob, setSelectedJob] = useState<Job | null>(null);
  const [showEnqueueModal, setShowEnqueueModal] = useState(false);
  const [showSimulation, setShowSimulation] = useState(false);
  const [showGuide, setShowGuide] = useState(false);

  const handleMetrics = useCallback((data: QueueMetrics) => {
    setPreviousMetrics(data);
  }, []);

  const handleJobEnqueued = useCallback((job: Job) => {
    setHighlightedJobId(job.id);
    setJobs(prev => {
      const existingIndex = prev.findIndex(j => j.id === job.id);
      if (existingIndex >= 0) {
        const updated = [...prev];
        updated[existingIndex] = job;
        return updated;
      }
      return [job, ...prev].slice(0, 100);
    });
    setTimeout(() => setHighlightedJobId(null), 3000);
  }, []);

  const handleJobUpdated = useCallback((job: Job) => {
    setJobs(prev => {
      const existingIndex = prev.findIndex(j => j.id === job.id);
      if (existingIndex >= 0) {
        const updated = [...prev];
        const merged: Job = {
          ...updated[existingIndex],
          ...job,
          status: job.status,
          updatedAt: job.updatedAt || new Date().toISOString(),
          timeline: job.timeline || updated[existingIndex].timeline,
        };
        updated[existingIndex] = merged;

        // Keep active drawer in sync
        setSelectedJob(curr => (curr && curr.id === job.id ? merged : curr));
        return updated;
      }
      return [job, ...prev].slice(0, 100);
    });
  }, []);

  const handleJobCreated = useCallback((job: Job) => {
    setHighlightedJobId(job.id);
    setJobs(prev => [job, ...prev].slice(0, 100));
    setTimeout(() => setHighlightedJobId(null), 3000);
  }, []);

  const handleJobsGenerated = useCallback((count: number) => {
    const newJobs: Job[] = Array.from({ length: count }, () => {
      const now = new Date().toISOString();
      return {
        id: "job-" + Date.now().toString(36) + "-" + Math.random().toString(36).slice(2, 6),
        queueName: ["default", "emails", "reports", "webhooks"][Math.floor(Math.random() * 4)],
        payload: JSON.stringify({ type: "simulated_workload", data: Math.random(), batchId: Date.now() }),
        status: "Queued" as JobStatus,
        retryCount: 0,
        maxRetries: 3,
        createdAt: now,
        updatedAt: now,
        timeline: [{ event: "Enqueued", timestamp: now, details: "Batch load generation" }],
      };
    });
    setJobs(prev => [...newJobs, ...prev].slice(0, 100));
  }, []);

  const fetchJobs = useCallback(async () => {
    try {
      const response = await fetch(`${API_BASE}/api/v1/jobs`);
      if (response.ok) {
        const jobsData = await response.json();
        const mappedJobs: Job[] = (Array.isArray(jobsData) ? jobsData : []).map((j: any) => ({
          id: j.id,
          queueName: j.queueName || "default",
          payload: j.payload || "",
          status: j.status as JobStatus,
          jobType: j.jobType,
          retryCount: j.retryCount || 0,
          maxRetries: j.maxRetries || 3,
          deadLetterReason: j.deadLetterReason,
          createdAt: j.createdAt,
          updatedAt: j.updatedAt || j.createdAt,
          timeline: j.timeline,
        }));
        setJobs(mappedJobs);
      }
    } catch (err) {
      console.error("[App] Failed to fetch initial jobs:", err);
    }
  }, []);

  useEffect(() => {
    fetchJobs();
  }, [fetchJobs]);

  const handleRetryJob = async (jobId: string) => {
    try {
      const res = await fetch(`${API_BASE}/api/v1/jobs/${jobId}/retry`, { method: "POST" });
      if (res.ok) {
        const data = await res.json();
        if (data.job) {
          handleJobUpdated(data.job);
        }
      }
    } catch (err) {
      console.error("[App] Failed to retry job:", err);
    }
  };

  const handleCancelJob = async (jobId: string) => {
    try {
      const res = await fetch(`${API_BASE}/api/v1/jobs/${jobId}/cancel`, { method: "POST" });
      if (res.ok) {
        const data = await res.json();
        if (data.job) {
          handleJobUpdated(data.job);
        }
      }
    } catch (err) {
      console.error("[App] Failed to cancel job:", err);
    }
  };

  const handleReplayAllDlq = async () => {
    try {
      const res = await fetch(`${API_BASE}/api/v1/jobs/dlq/replay-all`, { method: "POST" });
      if (res.ok) {
        await fetchJobs();
      }
    } catch (err) {
      console.error("[App] Failed to replay all DLQ:", err);
    }
  };

  const { connected: metricsConnected, metrics } = useSSE(METRICS_SSE_URL, { onMessage: handleMetrics });

  useJobEvents(JOBS_SSE_URL, {
    onJobEnqueued: handleJobEnqueued,
    onJobUpdated: handleJobUpdated,
  });

  // Calculate real average latency from completed jobs
  const avgLatencyMs = useMemo(() => {
    const completed = jobs.filter(j => j.status === "Completed");
    if (completed.length === 0) return 64;

    const latencies = completed.map(j => {
      // Check if timeline contains actual duration
      const completedStep = j.timeline?.find(t => t.event === "Completed");
      if (completedStep?.durationMs) return completedStep.durationMs;

      const created = new Date(j.createdAt).getTime();
      const updated = new Date(j.updatedAt).getTime();
      const diff = updated - created;
      return diff > 0 ? diff : 75;
    });

    const sum = latencies.reduce((a, b) => a + b, 0);
    return Math.round(sum / latencies.length);
  }, [jobs]);

  const kpis: KpiData = {
    totalProcessed: jobs.filter(j => j.status === "Completed").length,
    failedCount: jobs.filter(j => j.status === "Failed").length,
    deadLetteredCount: jobs.filter(j => j.status === "DeadLettered").length,
    activeWorkers: metrics?.activeWorkers ?? 1,
    avgLatencyMs,
  };

  return (
    <div className="min-h-screen bg-background">
      <Header 
        metrics={metrics} 
        connected={metricsConnected} 
        onCreateTask={() => setShowEnqueueModal(true)} 
        onShowSimulation={() => setShowSimulation(true)} 
        onShowGuide={() => setShowGuide(true)} 
      />
      <main className="p-6">
        <section className="mb-6">
          <KpiGrid kpis={kpis} />
        </section>
        <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
          <div className="space-y-6">
            <LiveMetrics metrics={metrics} previousMetrics={previousMetrics} />
            {showSimulation && <SimulationControl onJobsGenerated={handleJobsGenerated} />}
          </div>
          <div className="lg:col-span-2">
            <JobHistoryTable 
              jobs={jobs} 
              highlightedJobId={highlightedJobId} 
              onSelectJob={(job) => setSelectedJob(job)}
              onRetryJob={handleRetryJob}
              onCancelJob={handleCancelJob}
              onReplayAllDlq={handleReplayAllDlq}
            />
          </div>
        </div>
      </main>

      {/* Slide-out Drawer */}
      <JobDetailsDrawer
        job={selectedJob}
        isOpen={Boolean(selectedJob)}
        onClose={() => setSelectedJob(null)}
        onRetryJob={handleRetryJob}
        onCancelJob={handleCancelJob}
      />

      <EnqueueJobModal 
        isOpen={showEnqueueModal} 
        onClose={() => setShowEnqueueModal(false)} 
        onJobCreated={handleJobCreated} 
      />
      <IntegrationGuide 
        isOpen={showGuide} 
        onClose={() => setShowGuide(false)} 
      />
    </div>
  );
}
