import { TrendingUp, TrendingDown, Minus, type LucideIcon } from 'lucide-react';
import type { QueueMetrics } from '../types';

interface LiveMetricsProps {
  metrics: QueueMetrics | null;
  previousMetrics: QueueMetrics | null;
}

export function LiveMetrics({ metrics, previousMetrics }: LiveMetricsProps) {
  const queueDelta =
    metrics && previousMetrics ? metrics.queueSize - previousMetrics.queueSize : 0;
  const workerDelta =
    metrics && previousMetrics ? metrics.activeWorkers - previousMetrics.activeWorkers : 0;

  const QueueTrendIcon: LucideIcon = queueDelta > 0 ? TrendingUp : queueDelta < 0 ? TrendingDown : Minus;
  const WorkerTrendIcon: LucideIcon = workerDelta > 0 ? TrendingUp : workerDelta < 0 ? TrendingDown : Minus;

  return (
    <div className="rounded-xl border border-border bg-background-primary p-6">
      <h2 className="mb-4 text-lg font-semibold text-foreground">Live Metrics</h2>

      <div className="space-y-4">
        {/* Queue Size */}
        <div className="flex items-center justify-between rounded-lg bg-background-secondary p-4">
          <div>
            <p className="text-sm text-foreground-muted">Queue Size</p>
            <p className="mt-1 text-3xl font-bold text-foreground">
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
            <div className="h-12 w-px bg-border" />
            <div className="text-right">
              <p className="text-xs text-foreground-muted">per update</p>
            </div>
          </div>
        </div>

        {/* Active Workers */}
        <div className="flex items-center justify-between rounded-lg bg-background-secondary p-4">
          <div>
            <p className="text-sm text-foreground-muted">Active Workers</p>
            <p className="mt-1 text-3xl font-bold text-foreground">
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
            <div className="h-12 w-px bg-border" />
            <div className="text-right">
              <p className="text-xs text-foreground-muted">online</p>
            </div>
          </div>
        </div>

        {/* Timestamp */}
        {metrics?.timestamp && (
          <div className="text-center text-xs text-foreground-muted">
            Last updated: {new Date(metrics.timestamp).toLocaleTimeString()}
          </div>
        )}
      </div>
    </div>
  );
}