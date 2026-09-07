import { 
  createColumnHelper, 
  flexRender, 
  getCoreRowModel, 
  useReactTable, 
  type SortingState 
} from '@tanstack/react-table';
import { 
  ChevronDown, 
  ChevronUp, 
  RotateCcw, 
  Ban, 
  Eye, 
  ArchiveRestore 
} from 'lucide-react';
import { useState, useEffect, useRef, useMemo } from 'react';
import type { Job, JobStatus } from '../types';

const columnHelper = createColumnHelper<Job>();

const statusStyles: Record<JobStatus, { bg: string; text: string; dot: string }> = {
  Queued: { bg: 'bg-blue-500/20', text: 'text-blue-400', dot: 'bg-blue-400' },
  Processing: { bg: 'bg-amber-500/20', text: 'text-amber-400', dot: 'bg-amber-400 animate-pulse' },
  Completed: { bg: 'bg-emerald-500/20', text: 'text-emerald-glow', dot: 'bg-emerald-500' },
  Failed: { bg: 'bg-red-500/20', text: 'text-red-400', dot: 'bg-red-500' },
  DeadLettered: { bg: 'bg-purple-500/20', text: 'text-purple-400', dot: 'bg-purple-500' },
  Cancelled: { bg: 'bg-gray-500/20', text: 'text-gray-400', dot: 'bg-gray-400' },
};

function StatusBadge({ status }: { status: JobStatus }) {
  const style = statusStyles[status] || { bg: 'bg-gray-500/20', text: 'text-gray-400', dot: 'bg-gray-400' };
  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-medium ${style.bg} ${style.text}`}>
      <span className={`h-1.5 w-1.5 rounded-full ${style.dot}`} />
      {status}
    </span>
  );
}

interface JobHistoryTableProps {
  jobs: Job[];
  highlightedJobId?: string | null;
  onSelectJob: (job: Job) => void;
  onRetryJob: (jobId: string) => Promise<void>;
  onCancelJob: (jobId: string) => Promise<void>;
  onReplayAllDlq: () => Promise<void>;
}

export function JobHistoryTable({ 
  jobs, 
  highlightedJobId, 
  onSelectJob,
  onRetryJob,
  onCancelJob,
  onReplayAllDlq 
}: JobHistoryTableProps) {
  const [sorting, setSorting] = useState<SortingState>([{ id: 'updatedAt', desc: true }]);
  const [statusFilter, setStatusFilter] = useState<'All' | JobStatus>('All');
  const [isReplayingAll, setIsReplayingAll] = useState(false);
  const highlightedRef = useRef<HTMLTableRowElement | null>(null);

  const dlqCount = useMemo(() => 
    jobs.filter(j => j.status === 'DeadLettered' || j.status === 'Failed').length, 
    [jobs]
  );

  const filteredJobs = useMemo(() => {
    if (statusFilter === 'All') return jobs;
    return jobs.filter(j => j.status === statusFilter);
  }, [jobs, statusFilter]);

  const handleBulkReplay = async () => {
    setIsReplayingAll(true);
    try {
      await onReplayAllDlq();
    } finally {
      setIsReplayingAll(false);
    }
  };

  const columns = useMemo(() => [
    columnHelper.accessor('id', {
      header: 'Job ID',
      cell: (info) => (
        <span className="font-mono text-xs font-medium text-foreground hover:text-emerald-glow">
          {info.getValue().slice(0, 8)}...
        </span>
      ),
    }),
    columnHelper.accessor('queueName', {
      header: 'Queue',
      cell: (info) => (
        <span className="rounded bg-background-secondary px-2 py-0.5 text-xs font-mono text-foreground-muted">
          {info.getValue()}
        </span>
      ),
    }),
    columnHelper.accessor('status', {
      header: 'Status',
      cell: (info) => <StatusBadge status={info.getValue()} />,
    }),
    columnHelper.accessor('retryCount', {
      header: 'Retries',
      cell: (info) => (
        <span className="font-mono text-xs text-foreground-muted">
          {info.getValue()} / {info.row.original.maxRetries}
        </span>
      ),
    }),
    columnHelper.accessor('createdAt', {
      header: 'Created',
      cell: (info) => (
        <span className="text-xs text-foreground-muted">
          {new Date(info.getValue()).toLocaleTimeString()}
        </span>
      ),
    }),
    columnHelper.accessor('updatedAt', {
      header: 'Updated',
      cell: (info) => (
        <span className="text-xs text-foreground-muted">
          {new Date(info.getValue()).toLocaleTimeString()}
        </span>
      ),
    }),
    columnHelper.display({
      id: 'actions',
      header: () => <div className="text-right">Actions</div>,
      cell: ({ row }) => {
        const job = row.original;
        const canCancel = job.status === 'Queued' || job.status === 'Processing';
        const canRetry = job.status === 'Failed' || job.status === 'DeadLettered' || job.status === 'Cancelled';

        return (
          <div className="flex items-center justify-end gap-1.5" onClick={(e) => e.stopPropagation()}>
            {canRetry && (
              <button
                onClick={() => onRetryJob(job.id)}
                className="inline-flex items-center gap-1 rounded bg-emerald-500/10 px-2 py-1 text-[11px] font-medium text-emerald-glow hover:bg-emerald-500/20 transition-colors"
                title="Replay Job"
              >
                <RotateCcw className="h-3 w-3" />
                Replay
              </button>
            )}

            {canCancel && (
              <button
                onClick={() => onCancelJob(job.id)}
                className="inline-flex items-center gap-1 rounded bg-red-500/10 px-2 py-1 text-[11px] font-medium text-red-400 hover:bg-red-500/20 transition-colors"
                title="Cancel Execution"
              >
                <Ban className="h-3 w-3" />
                Cancel
              </button>
            )}

            <button
              onClick={() => onSelectJob(job)}
              className="rounded p-1 text-foreground-muted hover:bg-background-secondary hover:text-foreground transition-colors"
              title="Inspect Timeline & Payload"
            >
              <Eye className="h-3.5 w-3.5" />
            </button>
          </div>
        );
      },
    }),
  ], [onRetryJob, onCancelJob, onSelectJob]);

  const table = useReactTable({
    data: filteredJobs,
    columns,
    state: { sorting },
    onSortingChange: setSorting,
    getCoreRowModel: getCoreRowModel(),
  });

  useEffect(() => {
    if (highlightedJobId && highlightedRef.current) {
      highlightedRef.current.scrollIntoView({ behavior: 'smooth', block: 'center' });
      setTimeout(() => {
        if (highlightedRef.current) highlightedRef.current.classList.add('highlight-fade');
      }, 2000);
    }
  }, [highlightedJobId]);

  return (
    <div className="rounded-xl border border-border bg-background-primary overflow-hidden">
      {/* Header & Controls */}
      <div className="flex flex-col gap-4 border-b border-border px-6 py-4 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-center gap-3">
          <h2 className="text-lg font-semibold text-foreground">
            Job History <span className="text-sm font-normal text-foreground-muted">({filteredJobs.length})</span>
          </h2>

          {dlqCount > 0 && (
            <span className="inline-flex items-center gap-1 rounded-full bg-purple-500/20 px-2 py-0.5 text-xs font-semibold text-purple-300">
              {dlqCount} In DLQ
            </span>
          )}
        </div>

        <div className="flex flex-wrap items-center gap-2">
          {/* Status Filter Tabs */}
          <div className="flex items-center rounded-lg border border-border bg-background-secondary/60 p-0.5 text-xs">
            {(['All', 'Queued', 'Processing', 'Completed', 'Failed', 'DeadLettered', 'Cancelled'] as const).map((tab) => (
              <button
                key={tab}
                onClick={() => setStatusFilter(tab)}
                className={`rounded-md px-2.5 py-1 font-medium transition-colors ${
                  statusFilter === tab
                    ? 'bg-background-primary text-foreground shadow-xs'
                    : 'text-foreground-muted hover:text-foreground'
                }`}
              >
                {tab === 'DeadLettered' ? 'DLQ' : tab}
              </button>
            ))}
          </div>

          {/* Bulk Replay DLQ Button */}
          {dlqCount > 0 && (
            <button
              onClick={handleBulkReplay}
              disabled={isReplayingAll}
              className="inline-flex items-center gap-1.5 rounded-lg border border-purple-500/40 bg-purple-500/10 px-3 py-1.5 text-xs font-semibold text-purple-300 hover:bg-purple-500/20 transition-colors disabled:opacity-50"
            >
              <ArchiveRestore className={`h-3.5 w-3.5 ${isReplayingAll ? 'animate-spin' : ''}`} />
              Replay All DLQ ({dlqCount})
            </button>
          )}
        </div>
      </div>

      {/* Table Content */}
      <div className="overflow-x-auto max-h-[520px] overflow-y-auto">
        <table className="w-full">
          <thead className="sticky top-0 z-10 bg-background-primary border-b border-border">
            {table.getHeaderGroups().map((hg) => (
              <tr key={hg.id}>
                {hg.headers.map((h) => (
                  <th
                    key={h.id}
                    className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wider text-foreground-muted"
                  >
                    {h.isPlaceholder ? null : (
                      <button
                        onClick={h.column.getToggleSortingHandler()}
                        className="inline-flex items-center gap-1 hover:text-foreground transition-colors"
                      >
                        {flexRender(h.column.columnDef.header, h.getContext())}
                        {h.column.getIsSorted() === 'asc' && <ChevronUp className="h-3 w-3 text-emerald-glow" />}
                        {h.column.getIsSorted() === 'desc' && <ChevronDown className="h-3 w-3 text-emerald-glow" />}
                      </button>
                    )}
                  </th>
                ))}
              </tr>
            ))}
          </thead>
          <tbody>
            {table.getRowModel().rows.length === 0 ? (
              <tr>
                <td colSpan={columns.length} className="px-4 py-12 text-center text-sm text-foreground-muted">
                  No jobs found matching filter &ldquo;{statusFilter}&rdquo;.
                </td>
              </tr>
            ) : (
              table.getRowModel().rows.map((row) => (
                <tr
                  key={row.id}
                  ref={row.original.id === highlightedJobId ? highlightedRef : null}
                  onClick={() => onSelectJob(row.original)}
                  className={`cursor-pointer border-b border-border/40 transition-colors hover:bg-background-secondary/60 ${
                    row.original.id === highlightedJobId
                      ? 'bg-emerald-500/20 ring-1 ring-emerald-500/50'
                      : ''
                  }`}
                >
                  {row.getVisibleCells().map((cell) => (
                    <td key={cell.id} className="px-4 py-3">
                      {flexRender(cell.column.columnDef.cell, cell.getContext())}
                    </td>
                  ))}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
