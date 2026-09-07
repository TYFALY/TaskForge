import { useState, useEffect } from 'react';
import { TrendingUp, TrendingDown, Minus, Activity, Layers, type LucideIcon } from 'lucide-react';
import type { QueueMetrics, ThroughputPoint } from '../types';

interface LiveMetricsProps {
  metrics: QueueMetrics | null;
  previousMetrics: QueueMetrics | null;
}

function SparklineChart({ data, height = 64, color = '#10b981', gradientId }: { data: number[]; height?: number; color?: string; gradientId: string }) {
  if (data.length < 2) {
    return (
      <div className="flex h-16 items-center justify-center text-xs text-foreground-muted">
        Gathering telemetry...
      </div>
    );
  }

  const width = 280;
  const padding = 4;
  const minVal = Math.min(...data, 0);
  const maxVal = Math.max(...data, 1);
  const range = maxVal - minVal || 1;

  const points = data.map((val, idx) => {
    const x = padding + (idx / (data.length - 1)) * (width - padding * 2);
    const y = height - padding - ((val - minVal) / range) * (height - padding * 2);
    return { x, y };
  });

  const pathD = points.reduce((acc, pt, idx) => {
    if (idx === 0) return `M ${pt.x},${pt.y}`;
    return `${acc} L ${pt.x},${pt.y}`;
  }, '');

  const areaD = `${pathD} L ${points[points.length - 1].x},${height} L ${points[0].x},${height} Z`;

  return (
    <svg viewBox={`0 0 ${width} ${height}`} className="w-full overflow-visible" style={{ height }}>
      <defs>
        <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor={color} stopOpacity="0.3" />
          <stop offset="100%" stopColor={color} stopOpacity="0.0" />
        </linearGradient>
      </defs>
      <path d={areaD} fill={`url(#${gradientId})`} />
      <path d={pathD} fill="none" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
      {/* Pulse circle on the newest point */}
      {points.length > 0 && (
        <circle
          cx={points[points.length - 1].x}
          cy={points[points.length - 1].y}
          r="3"
          fill={color}
          className="animate-pulse"
        />
      )}
    </svg>
  );
}

export function LiveMetrics({ metrics, previousMetrics }: LiveMetricsProps) {
  const [localHistory, setLocalHistory] = useState<ThroughputPoint[]>([]);

  // Collect rolling points if not provided by server or to ensure smooth updates
  useEffect(() => {
    if (!metrics) return;

    setLocalHistory(prev => {
      const serverHistory = metrics.throughputHistory;
      if (serverHistory && serverHistory.length > 0) {
        return serverHistory;
      }

      // Fallback local calculation
      const lastPoint = prev[prev.length - 1];
      const deltaProcessed = (metrics.processedCount ?? 0) - (lastPoint?.throughput ? 0 : 0);
      const newPoint: ThroughputPoint = {
        timestamp: new Date().toLocaleTimeString(),
        throughput: Math.max(0, deltaProcessed),
        queueSize: metrics.queueSize,
      };

      const updated = [...prev, newPoint];
      return updated.slice(-15);
    });
  }, [metrics]);

  const queueDelta =
    metrics && previousMetrics ? metrics.queueSize - previousMetrics.queueSize : 0;
  const workerDelta =
    metrics && previousMetrics ? metrics.activeWorkers - previousMetrics.activeWorkers : 0;

  const QueueTrendIcon: LucideIcon = queueDelta > 0 ? TrendingUp : queueDelta < 0 ? TrendingDown : Minus;
  const WorkerTrendIcon: LucideIcon = workerDelta > 0 ? TrendingUp : workerDelta < 0 ? TrendingDown : Minus;

  const throughputSeries = localHistory.map(p => p.throughput);
  const queueDepthSeries = localHistory.map(p => p.queueSize);
  const currentThroughput = localHistory.length > 0 ? localHistory[localHistory.length - 1].throughput : 0;

  return (
    <div className="rounded-xl border border-border bg-background-primary p-6 space-y-5">
      <div className="flex items-center justify-between">
        <h2 className="text-lg font-semibold text-foreground">Live Telemetry</h2>
        <span className="inline-flex items-center gap-1.5 rounded-full bg-emerald-500/10 px-2.5 py-0.5 text-xs font-medium text-emerald-glow">
          <span className="h-1.5 w-1.5 rounded-full bg-emerald-500 animate-ping" />
          Realtime SSE
        </span>
      </div>

      {/* Primary KPI Cards */}
      <div className="space-y-3">
        {/* Queue Size */}
        <div className="flex items-center justify-between rounded-lg bg-background-secondary p-4">
          <div>
            <p className="text-xs text-foreground-muted uppercase tracking-wider font-medium">Queue Depth</p>
            <p className="mt-1 text-3xl font-bold text-foreground font-mono">
              {metrics?.queueSize ?? '-'}
            </p>
          </div>
          <div className="flex items-center gap-2">
            {queueDelta !== 0 && (
              <>
                <span
                  className={`text-sm font-medium ${
                    queueDelta > 0 ? 'text-amber-400' : 'text-emerald-glow'
                  }`}
                >
                  {queueDelta > 0 ? '+' : ''}
                  {queueDelta}
                </span>
                <QueueTrendIcon
                  className={`h-5 w-5 ${
                    queueDelta > 0 ? 'text-amber-400' : 'text-emerald-glow'
                  }`}
                />
              </>
            )}
            <div className="h-10 w-px bg-border" />
            <div className="text-right">
              <p className="text-[11px] text-foreground-muted">in-flight</p>
            </div>
          </div>
        </div>

        {/* Active Workers */}
        <div className="flex items-center justify-between rounded-lg bg-background-secondary p-4">
          <div>
            <p className="text-xs text-foreground-muted uppercase tracking-wider font-medium">Active Workers</p>
            <p className="mt-1 text-3xl font-bold text-foreground font-mono">
              {metrics?.activeWorkers ?? '-'}
            </p>
          </div>
          <div className="flex items-center gap-2">
            {workerDelta !== 0 && (
              <>
                <span
                  className={`text-sm font-medium ${
                    workerDelta > 0 ? 'text-emerald-glow' : 'text-red-400'
                  }`}
                >
                  {workerDelta > 0 ? '+' : ''}
                  {workerDelta}
                </span>
                <WorkerTrendIcon
                  className={`h-5 w-5 ${
                    workerDelta > 0 ? 'text-emerald-glow' : 'text-red-400'
                  }`}
                />
              </>
            )}
            <div className="h-10 w-px bg-border" />
            <div className="text-right">
              <p className="text-[11px] text-foreground-muted">embedded</p>
            </div>
          </div>
        </div>
      </div>

      {/* Rolling Throughput Sparkline */}
      <div className="rounded-lg border border-border bg-background-secondary/50 p-4">
        <div className="mb-2 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Activity className="h-4 w-4 text-emerald-glow" />
            <span className="text-xs font-semibold uppercase tracking-wider text-foreground-muted">Throughput</span>
          </div>
          <span className="font-mono text-xs font-bold text-emerald-glow">
            {currentThroughput} <span className="font-normal text-foreground-muted">jobs/s</span>
          </span>
        </div>
        <SparklineChart data={throughputSeries} color="#10b981" gradientId="throughputGrad" />
      </div>

      {/* Rolling Queue Depth Sparkline */}
      <div className="rounded-lg border border-border bg-background-secondary/50 p-4">
        <div className="mb-2 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Layers className="h-4 w-4 text-blue-400" />
            <span className="text-xs font-semibold uppercase tracking-wider text-foreground-muted">Queue Trend</span>
          </div>
          <span className="font-mono text-xs font-bold text-blue-400">
            {metrics?.queueSize ?? 0} <span className="font-normal text-foreground-muted">depth</span>
          </span>
        </div>
        <SparklineChart data={queueDepthSeries} color="#60a5fa" gradientId="queueGrad" />
      </div>

      {/* Timestamp */}
      {metrics?.timestamp && (
        <div className="text-center text-[11px] text-foreground-muted">
          Last heartbeat: {new Date(metrics.timestamp).toLocaleTimeString()}
        </div>
      )}
    </div>
  );
}
