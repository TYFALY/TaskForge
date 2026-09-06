import { useState, useEffect, useRef, useCallback } from 'react';
import { Play, Pause, Zap, Trash2, Activity } from 'lucide-react';

interface SimulationControlProps {
  onJobsGenerated: (count: number) => void;
}

const RATES = [
  { label: '1 job/s', value: 1000 },
  { label: '5 jobs/s', value: 200 },
  { label: '10 jobs/s', value: 100 },
  { label: '20 jobs/s', value: 50 },
];

const QUEUES = ['default', 'emails', 'reports', 'notifications'];

export function SimulationControl({ onJobsGenerated }: SimulationControlProps) {
  const [isRunning, setIsRunning] = useState(false);
  const [rate, setRate] = useState(200);
  const [totalGenerated, setTotalGenerated] = useState(0);
  const intervalRef = useRef<number | null>(null);

  const generateJob = useCallback(async () => {
    try {
      const payload = JSON.stringify({
        type: QUEUES[Math.floor(Math.random() * QUEUES.length)],
        data: Math.random().toString(36).slice(2),
        timestamp: Date.now(),
      });
      await fetch('/api/v1/jobs/enqueue', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ queueName: QUEUES[Math.floor(Math.random() * QUEUES.length)], payload, maxRetries: 3 }),
      });
      setTotalGenerated(prev => prev + 1);
      onJobsGenerated(1);
    } catch { /* Silent fail during simulation */ }
  }, [onJobsGenerated]);

  const toggleSimulation = useCallback(() => {
    if (isRunning) {
      if (intervalRef.current) clearInterval(intervalRef.current);
      setIsRunning(false);
    } else {
      setIsRunning(true);
      intervalRef.current = window.setInterval(generateJob, rate);
    }
  }, [isRunning, rate, generateJob]);

  useEffect(() => {
    if (isRunning && intervalRef.current) {
      clearInterval(intervalRef.current);
      intervalRef.current = window.setInterval(generateJob, rate);
    }
    return () => { if (intervalRef.current) clearInterval(intervalRef.current); };
  }, [rate, isRunning, generateJob]);

  useEffect(() => { return () => { if (intervalRef.current) clearInterval(intervalRef.current); }; }, []);

  const stopAndReset = () => {
    if (intervalRef.current) clearInterval(intervalRef.current);
    setIsRunning(false);
    setTotalGenerated(0);
  };

  return (
    <div className="rounded-xl border border-border bg-background-primary overflow-hidden">
      <div className="border-b border-border px-6 py-4">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-amber-500/20">
              <Zap className="h-5 w-5 text-amber-400" />
            </div>
            <div>
              <h2 className="text-lg font-semibold">Traffic Simulation</h2>
              <p className="text-sm text-foreground-muted">Generate load for testing</p>
            </div>
          </div>
          <div className="flex items-center gap-2 rounded-lg bg-background-secondary px-3 py-1.5">
            <Activity className="h-4 w-4 text-foreground-muted" />
            <span className="font-mono text-sm text-foreground-muted">Generated:</span>
            <span className="font-mono font-semibold text-emerald-glow">{totalGenerated}</span>
          </div>
        </div>
      </div>
      <div className="p-6">
        <div className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-foreground-muted mb-3">Rate</label>
            <div className="flex gap-2">
              {RATES.map(r => (
                <button key={r.value} type="button" onClick={() => setRate(r.value)}
                  className={`flex-1 rounded-lg px-3 py-2 text-sm font-medium transition-all ${rate === r.value ? 'bg-emerald-500/20 text-emerald-glow border border-emerald-500/50' : 'bg-background-secondary text-foreground-muted hover:text-foreground border border-transparent'}`}>
                  {r.label}
                </button>
              ))}
            </div>
          </div>
          <div className="flex gap-3">
            <button type="button" onClick={toggleSimulation}
              className={`flex items-center gap-2 flex-1 justify-center rounded-lg px-4 py-3 text-sm font-medium transition-all ${isRunning ? 'bg-red-500/20 text-red-400 border border-red-500/50 hover:bg-red-500/30' : 'bg-emerald-500/20 text-emerald-glow border border-emerald-500/50 hover:bg-emerald-500/30'}`}>
              {isRunning ? <><Pause className="h-4 w-4" />Pause</> : <><Play className="h-4 w-4" />Start</>}
            </button>
            <button type="button" onClick={stopAndReset} disabled={totalGenerated === 0}
              className="flex items-center gap-2 rounded-lg bg-background-secondary px-4 py-3 text-sm font-medium text-foreground-muted hover:text-foreground transition-colors disabled:opacity-50">
              <Trash2 className="h-4 w-4" />Reset
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
