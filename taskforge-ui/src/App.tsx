import { useState, useEffect, useCallback } from "react";
import { Header } from "./components/Header";
import { KpiGrid } from "./components/KpiGrid";
import { LiveMetrics } from "./components/LiveMetrics";
import { JobHistoryTable } from "./components/JobHistoryTable";
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
        updated[existingIndex] = {
          ...updated[existingIndex],
          status: job.status,
          updatedAt: job.updatedAt || new Date().toISOString(),
        };
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
    const newJobs: Job[] = Array.from({ length: count }, () => ({
      id: "job-" + Date.now().toString(36) + "-" + Math.random().toString(36).slice(2, 6),
      queueName: ["default", "emails", "reports"][Math.floor(Math.random() * 3)],
      payload: JSON.stringify({ type: "simulated", data: Math.random() }),
      status: "Queued" as JobStatus,
      retryCount: 0,
      maxRetries: 3,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    }));
    setJobs(prev => [...newJobs, ...prev].slice(0, 100));
  }, []);

  const { connected: metricsConnected, metrics } = useSSE(METRICS_SSE_URL, { onMessage: handleMetrics });

  useJobEvents(JOBS_SSE_URL, {
    onJobEnqueued: handleJobEnqueued,
    onJobUpdated: handleJobUpdated,
  });

  useEffect(() => {
    const fetchInitialJobs = async () => {
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
            createdAt: j.createdAt,
            updatedAt: j.updatedAt || j.createdAt,
          }));
          setJobs(prev => {
            const existingIds = new Set(prev.map(j => j.id));
            const newJobs = mappedJobs.filter(j => !existingIds.has(j.id));
            return [...prev, ...newJobs].sort((a, b) => 
              new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
            ).slice(0, 100);
          });
        }
      } catch (err) {
        console.error("[App] Failed to fetch initial jobs:", err);
      }
    };

    fetchInitialJobs();
  }, []);

  const kpis: KpiData = {
    totalProcessed: jobs.filter(j => j.status === "Completed").length,
    failedCount: jobs.filter(j => j.status === "Failed").length,
    deadLetteredCount: jobs.filter(j => j.status === "DeadLettered").length,
    activeWorkers: metrics?.activeWorkers ?? 1,
    avgLatencyMs: Math.floor(Math.random() * 100) + 50,
  };

  return (
    <div className="min-h-screen bg-background">
      <Header metrics={metrics} connected={metricsConnected} onCreateTask={() => setShowEnqueueModal(true)} onShowSimulation={() => setShowSimulation(true)} onShowGuide={() => setShowGuide(true)} />
      <main className="p-6">
        <section className="mb-6"><KpiGrid kpis={kpis} /></section>
        <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
          <div className="space-y-6">
            <LiveMetrics metrics={metrics} previousMetrics={previousMetrics} />
            {showSimulation && <SimulationControl onJobsGenerated={handleJobsGenerated} />}
          </div>
          <div className="lg:col-span-2">
            <JobHistoryTable jobs={jobs} highlightedJobId={highlightedJobId} />
          </div>
        </div>
      </main>
      <EnqueueJobModal isOpen={showEnqueueModal} onClose={() => setShowEnqueueModal(false)} onJobCreated={handleJobCreated} />
      <IntegrationGuide isOpen={showGuide} onClose={() => setShowGuide(false)} />
    </div>
  );
}
