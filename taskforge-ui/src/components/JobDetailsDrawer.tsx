import { useState } from 'react';
import { 
  X, 
  RotateCcw, 
  Ban, 
  Copy, 
  Check, 
  Clock, 
  AlertTriangle, 
  Layers, 
  Terminal, 
  Activity 
} from 'lucide-react';
import type { Job, JobStatus } from '../types';

interface JobDetailsDrawerProps {
  job: Job | null;
  isOpen: boolean;
  onClose: () => void;
  onRetryJob: (jobId: string) => Promise<void>;
  onCancelJob: (jobId: string) => Promise<void>;
}

const statusStyles: Record<JobStatus, { bg: string; text: string; dot: string }> = {
  Queued: { bg: 'bg-blue-500/20', text: 'text-blue-400', dot: 'bg-blue-400' },
  Processing: { bg: 'bg-amber-500/20', text: 'text-amber-400', dot: 'bg-amber-400 animate-pulse' },
  Completed: { bg: 'bg-emerald-500/20', text: 'text-emerald-glow', dot: 'bg-emerald-500' },
  Failed: { bg: 'bg-red-500/20', text: 'text-red-400', dot: 'bg-red-500' },
  DeadLettered: { bg: 'bg-purple-500/20', text: 'text-purple-400', dot: 'bg-purple-500' },
  Cancelled: { bg: 'bg-gray-500/20', text: 'text-gray-400', dot: 'bg-gray-400' },
};

export function JobDetailsDrawer({ job, isOpen, onClose, onRetryJob, onCancelJob }: JobDetailsDrawerProps) {
  const [copied, setCopied] = useState(false);
  const [isActing, setIsActing] = useState(false);

  if (!isOpen || !job) return null;

  const handleCopyPayload = () => {
    let textToCopy = job.payload;
    try {
      const parsed = JSON.parse(job.payload);
      textToCopy = JSON.stringify(parsed, null, 2);
    } catch {
      // Use raw payload if not json
    }
    navigator.clipboard.writeText(textToCopy);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const handleRetry = async () => {
    setIsActing(true);
    try {
      await onRetryJob(job.id);
    } finally {
      setIsActing(false);
    }
  };

  const handleCancel = async () => {
    setIsActing(true);
    try {
      await onCancelJob(job.id);
    } finally {
      setIsActing(false);
    }
  };

  let formattedPayload = job.payload;
  try {
    const parsed = JSON.parse(job.payload);
    formattedPayload = JSON.stringify(parsed, null, 2);
  } catch {
    // Keep raw
  }

  const isTerminal = job.status === 'Completed' || job.status === 'Failed' || job.status === 'DeadLettered' || job.status === 'Cancelled';
  const canCancel = job.status === 'Queued' || job.status === 'Processing';
  const canRetry = job.status === 'Failed' || job.status === 'DeadLettered' || job.status === 'Cancelled';

  // Build timeline fallback if timeline array not present
  const timelineEvents = (job.timeline && job.timeline.length > 0) ? job.timeline : [
    {
      event: 'Enqueued',
      timestamp: job.createdAt,
      details: 'Job registered into queue buffer',
    },
    ...(job.status !== 'Queued' ? [{
      event: 'Processing',
      timestamp: job.updatedAt,
      details: `Worker picked up job (Attempt ${job.retryCount + 1})`,
    }] : []),
    ...(isTerminal && job.status !== 'Processing' ? [{
      event: job.status,
      timestamp: job.updatedAt,
      details: job.deadLetterReason || (job.status === 'Completed' ? 'Successfully processed' : 'Job halted'),
    }] : []),
  ];

  return (
    <div className="fixed inset-0 z-50 flex justify-end">
      {/* Backdrop */}
      <div 
        className="fixed inset-0 bg-black/60 backdrop-blur-xs transition-opacity" 
        onClick={onClose}
      />

      {/* Slide-out Drawer */}
      <div className="relative z-10 flex h-full w-full max-w-xl flex-col border-l border-border bg-background-primary shadow-2xl transition-transform duration-300 ease-in-out">
        {/* Header */}
        <div className="flex items-center justify-between border-b border-border px-6 py-4">
          <div className="flex items-center gap-3">
            <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-emerald-500/10 text-emerald-glow">
              <Activity className="h-5 w-5" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h3 className="font-semibold text-foreground">Job Details</h3>
                <span className={`inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-medium ${statusStyles[job.status]?.bg || 'bg-gray-500/20'} ${statusStyles[job.status]?.text || 'text-gray-400'}`}>
                  <span className={`h-1.5 w-1.5 rounded-full ${statusStyles[job.status]?.dot || 'bg-gray-400'}`} />
                  {job.status}
                </span>
              </div>
              <p className="font-mono text-xs text-foreground-muted">{job.id}</p>
            </div>
          </div>
          <button 
            onClick={onClose}
            className="rounded-lg p-2 text-foreground-muted hover:bg-background-secondary hover:text-foreground transition-colors"
            title="Close Drawer"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Action Toolbar */}
        <div className="flex items-center justify-between border-b border-border bg-background-secondary/40 px-6 py-3">
          <div className="flex items-center gap-2 text-xs text-foreground-muted">
            <Layers className="h-4 w-4" />
            <span>Queue: <strong className="text-foreground font-mono">{job.queueName}</strong></span>
          </div>

          <div className="flex items-center gap-2">
            {canCancel && (
              <button
                onClick={handleCancel}
                disabled={isActing}
                className="inline-flex items-center gap-1.5 rounded-lg border border-red-500/30 bg-red-500/10 px-3 py-1.5 text-xs font-medium text-red-400 hover:bg-red-500/20 transition-colors disabled:opacity-50"
              >
                <Ban className="h-3.5 w-3.5" />
                Cancel Job
              </button>
            )}

            {canRetry && (
              <button
                onClick={handleRetry}
                disabled={isActing}
                className="inline-flex items-center gap-1.5 rounded-lg border border-emerald-500/30 bg-emerald-500/10 px-3 py-1.5 text-xs font-medium text-emerald-glow hover:bg-emerald-500/20 transition-colors disabled:opacity-50"
              >
                <RotateCcw className={`h-3.5 w-3.5 ${isActing ? 'animate-spin' : ''}`} />
                Replay Job
              </button>
            )}
          </div>
        </div>

        {/* Body Content */}
        <div className="flex-1 overflow-y-auto p-6 space-y-6">
          {/* DLQ / Error Banner if present */}
          {job.deadLetterReason && (
            <div className="rounded-lg border border-purple-500/30 bg-purple-500/10 p-4">
              <div className="flex items-start gap-3">
                <AlertTriangle className="h-5 w-5 text-purple-400 shrink-0 mt-0.5" />
                <div>
                  <h4 className="text-xs font-semibold uppercase tracking-wider text-purple-300">
                    Dead Letter Diagnostic
                  </h4>
                  <p className="mt-1 text-sm text-purple-200 font-mono">
                    {job.deadLetterReason}
                  </p>
                  <p className="mt-2 text-xs text-purple-300/80">
                    Max retries ({job.maxRetries}) exhausted. You can re-enqueue this job directly with the Replay button above.
                  </p>
                </div>
              </div>
            </div>
          )}

          {/* Quick Metrics Grid */}
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <div className="rounded-lg border border-border bg-background-secondary p-3">
              <span className="text-xs text-foreground-muted">Type</span>
              <p className="mt-1 font-medium text-foreground text-sm">{job.jobType || 'Default'}</p>
            </div>
            <div className="rounded-lg border border-border bg-background-secondary p-3">
              <span className="text-xs text-foreground-muted">Retries</span>
              <p className="mt-1 font-medium text-foreground text-sm font-mono">{job.retryCount} / {job.maxRetries}</p>
            </div>
            <div className="rounded-lg border border-border bg-background-secondary p-3">
              <span className="text-xs text-foreground-muted">Created</span>
              <p className="mt-1 font-medium text-foreground text-xs">{new Date(job.createdAt).toLocaleTimeString()}</p>
            </div>
            <div className="rounded-lg border border-border bg-background-secondary p-3">
              <span className="text-xs text-foreground-muted">Last Active</span>
              <p className="mt-1 font-medium text-foreground text-xs">{new Date(job.updatedAt).toLocaleTimeString()}</p>
            </div>
          </div>

          {/* Execution Timeline */}
          <div>
            <h4 className="mb-3 flex items-center gap-2 text-sm font-semibold text-foreground">
              <Clock className="h-4 w-4 text-emerald-glow" />
              Execution Lifecycle Timeline
            </h4>
            <div className="relative pl-6 space-y-6 before:absolute before:bottom-2 before:left-[11px] before:top-2 before:w-0.5 before:bg-border">
              {timelineEvents.map((evt, idx) => {
                const isEvtComplete = evt.event === 'Completed';
                const isEvtFailed = evt.event === 'Failed' || evt.event === 'DeadLettered';
                const isEvtCancelled = evt.event === 'Cancelled';
                
                let dotColor = 'bg-blue-500 border-blue-400';
                if (isEvtComplete) dotColor = 'bg-emerald-500 border-emerald-400';
                if (isEvtFailed) dotColor = 'bg-red-500 border-red-400';
                if (isEvtCancelled) dotColor = 'bg-gray-500 border-gray-400';
                if (evt.event === 'Replayed') dotColor = 'bg-purple-500 border-purple-400';

                return (
                  <div key={idx} className="relative group">
                    {/* Node Dot */}
                    <span className={`absolute -left-[19px] top-1 h-3 w-3 rounded-full border-2 bg-background ${dotColor}`} />
                    
                    <div className="flex items-baseline justify-between gap-2">
                      <span className="text-sm font-medium text-foreground">{evt.event}</span>
                      <span className="text-xs text-foreground-muted font-mono">
                        {new Date(evt.timestamp).toLocaleTimeString()}
                      </span>
                    </div>

                    {evt.details && (
                      <p className="mt-0.5 text-xs text-foreground-muted">
                        {evt.details}
                      </p>
                    )}

                    {evt.durationMs !== undefined && (
                      <span className="mt-1 inline-block rounded bg-background-secondary px-1.5 py-0.5 font-mono text-[11px] text-emerald-glow">
                        duration: {evt.durationMs}ms
                      </span>
                    )}
                  </div>
                );
              })}
            </div>
          </div>

          {/* Payload Inspector */}
          <div>
            <div className="mb-2 flex items-center justify-between">
              <h4 className="flex items-center gap-2 text-sm font-semibold text-foreground">
                <Terminal className="h-4 w-4 text-foreground-muted" />
                Payload Data
              </h4>
              <button
                onClick={handleCopyPayload}
                className="inline-flex items-center gap-1 text-xs text-foreground-muted hover:text-foreground transition-colors"
              >
                {copied ? <Check className="h-3.5 w-3.5 text-emerald-glow" /> : <Copy className="h-3.5 w-3.5" />}
                {copied ? 'Copied' : 'Copy JSON'}
              </button>
            </div>
            <div className="relative rounded-lg border border-border bg-[#0d1117] p-4 font-mono text-xs text-slate-200 overflow-x-auto max-h-56">
              <pre>{formattedPayload || '{}'}</pre>
            </div>
          </div>
        </div>

        {/* Footer */}
        <div className="border-t border-border bg-background-secondary/30 px-6 py-3 text-right">
          <button
            onClick={onClose}
            className="rounded-lg border border-border bg-background-primary px-4 py-2 text-xs font-medium text-foreground hover:bg-background-secondary transition-colors"
          >
            Close Details
          </button>
        </div>
      </div>
    </div>
  );
}
