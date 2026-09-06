import { CheckCircle, XCircle, Users, Clock } from 'lucide-react';
import type { KpiData } from '../types';

interface KpiGridProps {
  kpis: KpiData;
}

export function KpiGrid({ kpis }: KpiGridProps) {
  const cards = [
    {
      label: 'Total Processed',
      value: kpis.totalProcessed.toLocaleString(),
      icon: CheckCircle,
      color: 'text-emerald-glow',
      bg: 'bg-emerald-500/10',
    },
    {
      label: 'Failed / DLQ',
      value: `${kpis.failedCount} / ${kpis.deadLetteredCount}`,
      icon: XCircle,
      color: 'text-red-400',
      bg: 'bg-red-500/10',
    },
    {
      label: 'Active Workers',
      value: kpis.activeWorkers.toString(),
      icon: Users,
      color: 'text-blue-400',
      bg: 'bg-blue-500/10',
    },
    {
      label: 'Avg Latency',
      value: `${kpis.avgLatencyMs.toFixed(0)}ms`,
      icon: Clock,
      color: 'text-amber-400',
      bg: 'bg-amber-500/10',
    },
  ];

  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
      {cards.map((card) => (
        <div
          key={card.label}
          className="rounded-xl border border-border bg-background-primary p-5 shadow-sm transition-all hover:border-emerald-glow/30"
        >
          <div className="flex items-center justify-between">
            <div>
              <p className="text-sm text-foreground-muted">{card.label}</p>
              <p className="mt-1 text-2xl font-bold text-foreground">{card.value}</p>
            </div>
            <div className={`rounded-lg p-3 ${card.bg}`}>
              <card.icon className={`h-6 w-6 ${card.color}`} />
            </div>
          </div>
        </div>
      ))}
    </div>
  );
}