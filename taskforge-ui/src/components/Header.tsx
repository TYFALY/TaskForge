import { Activity, Server, Wifi, WifiOff, Plus, Zap, BookOpen } from 'lucide-react';
import type { QueueMetrics } from '../types';

interface HeaderProps {
  metrics: QueueMetrics | null;
  connected: boolean;
  onCreateTask: () => void;
  onShowSimulation: () => void;
  onShowGuide: () => void;
}

export function Header({ metrics, connected, onCreateTask, onShowSimulation, onShowGuide }: HeaderProps) {
  return (
    <header className="border-b border-border bg-background-primary px-6 py-4">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-4">
          <div className="flex items-center gap-3">
            <img src="/logo.png" alt="TaskForge Lightning Fox" className="h-10 w-10 object-contain" />
            <h1 className="text-2xl font-bold text-foreground">TaskForge</h1>
          </div>
          <div className="h-6 w-px bg-border" />
          <span className="text-sm text-foreground-muted">Distributed Job Engine</span>
        </div>
        <div className="flex items-center gap-3">
          <button onClick={onCreateTask} className="inline-flex items-center gap-2 rounded-lg bg-gradient-to-r from-emerald-600 to-blue-600 px-4 py-2 text-sm font-medium text-white transition-all hover:from-emerald-500 hover:to-blue-500">
            <Plus className="h-4 w-4" />Create Task
          </button>
          <button onClick={onShowSimulation} className="inline-flex items-center gap-2 rounded-lg bg-amber-500/20 px-4 py-2 text-sm font-medium text-amber-400 border border-amber-500/50 transition-all hover:bg-amber-500/30">
            <Zap className="h-4 w-4" />Simulate
          </button>
          <button onClick={onShowGuide} className="inline-flex items-center gap-2 rounded-lg bg-background-secondary px-4 py-2 text-sm font-medium text-foreground-muted transition-all hover:text-foreground">
            <BookOpen className="h-4 w-4" />Integrate
          </button>
          <div className="h-8 w-px bg-border mx-2" />
          <div className="flex items-center gap-2">
            <div className={`h-2.5 w-2.5 rounded-full ${connected ? 'bg-emerald-500 animate-pulse' : 'bg-red-500'}`} />
            <span className="text-sm font-medium">{connected ? 'Connected' : 'Disconnected'}</span>
            {connected ? <Wifi className="h-4 w-4 text-emerald-glow" /> : <WifiOff className="h-4 w-4 text-red-500" />}
          </div>
          <div className="flex items-center gap-2 rounded-lg bg-background-secondary px-3 py-1.5">
            <Server className="h-4 w-4 text-foreground-muted" /><span className="text-sm text-foreground-muted">Workers:</span><span className="font-mono font-semibold text-emerald-glow">{metrics?.activeWorkers ?? '-'}</span>
          </div>
          <div className="flex items-center gap-2 rounded-lg bg-background-secondary px-3 py-1.5">
            <Activity className="h-4 w-4 text-foreground-muted" /><span className="text-sm text-foreground-muted">Queue:</span><span className="font-mono font-semibold text-amber-400">{metrics?.queueSize ?? '-'}</span>
          </div>
        </div>
      </div>
    </header>
  );
}
