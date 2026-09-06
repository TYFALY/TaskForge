import { createColumnHelper, flexRender, getCoreRowModel, useReactTable, type SortingState } from '@tanstack/react-table';
import { ChevronDown, ChevronUp } from 'lucide-react';
import { useState, useEffect, useRef } from 'react';
import type { Job, JobStatus } from '../types';

const columnHelper = createColumnHelper<Job>();

const statusStyles: Record<JobStatus, { bg: string; text: string; dot: string }> = {
  Queued: { bg: 'bg-blue-500/20', text: 'text-blue-400', dot: 'bg-blue-400' },
  Processing: { bg: 'bg-amber-500/20', text: 'text-amber-400', dot: 'bg-amber-400 animate-pulse' },
  Completed: { bg: 'bg-emerald-500/20', text: 'text-emerald-glow', dot: 'bg-emerald-500' },
  Failed: { bg: 'bg-red-500/20', text: 'text-red-400', dot: 'bg-red-500' },
  DeadLettered: { bg: 'bg-purple-500/20', text: 'text-purple-400', dot: 'bg-purple-500' },
};

function StatusBadge({ status }: { status: JobStatus }) {
  const style = statusStyles[status];
  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-medium ${style.bg} ${style.text}`}>
      <span className={`h-1.5 w-1.5 rounded-full ${style.dot}`} />{status}
    </span>
  );
}

const columns = [
  columnHelper.accessor('id', {
    header: 'Job ID',
    cell: (info) => <span className="font-mono text-xs text-foreground-muted">{info.getValue().slice(0, 8)}...</span>,
  }),
  columnHelper.accessor('queueName', {
    header: 'Queue',
    cell: (info) => <span className="rounded bg-background-secondary px-2 py-0.5 text-xs">{info.getValue()}</span>,
  }),
  columnHelper.accessor('status', {
    header: 'Status',
    cell: (info) => <StatusBadge status={info.getValue()} />,
  }),
  columnHelper.accessor('retryCount', {
    header: 'Retries',
    cell: (info) => <span className="font-mono text-sm text-foreground-muted">{info.getValue()} / {info.row.original.maxRetries}</span>,
  }),
  columnHelper.accessor('createdAt', {
    header: 'Created',
    cell: (info) => <span className="text-sm text-foreground-muted">{new Date(info.getValue()).toLocaleTimeString()}</span>,
  }),
  columnHelper.accessor('updatedAt', {
    header: 'Updated',
    cell: (info) => <span className="text-sm text-foreground-muted">{new Date(info.getValue()).toLocaleTimeString()}</span>,
  }),
];

interface JobHistoryTableProps {
  jobs: Job[];
  highlightedJobId?: string | null;
}

export function JobHistoryTable({ jobs, highlightedJobId }: JobHistoryTableProps) {
  const [sorting, setSorting] = useState<SortingState>([{ id: 'updatedAt', desc: true }]);
  const highlightedRef = useRef<HTMLTableRowElement | null>(null);

  const table = useReactTable({ data: jobs, columns, state: { sorting }, onSortingChange: setSorting, getCoreRowModel: getCoreRowModel() });

  useEffect(() => {
    if (highlightedJobId && highlightedRef.current) {
      highlightedRef.current.scrollIntoView({ behavior: 'smooth', block: 'center' });
      setTimeout(() => { if (highlightedRef.current) highlightedRef.current.classList.add('highlight-fade'); }, 2000);
    }
  }, [highlightedJobId]);

  return (
    <div className="rounded-xl border border-border bg-background-primary overflow-hidden">
      <div className="border-b border-border px-6 py-4">
        <h2 className="text-lg font-semibold">Job History <span className="text-sm font-normal text-foreground-muted">({jobs.length})</span></h2>
      </div>
      <div className="overflow-x-auto max-h-[500px] overflow-y-auto">
        <table className="w-full">
          <thead className="sticky top-0 z-10 bg-background-primary">
            {table.getHeaderGroups().map((hg) => (
              <tr key={hg.id} className="border-b border-border">
                {hg.headers.map((h) => (
                  <th key={h.id} className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wider text-foreground-muted">
                    <button onClick={h.column.getToggleSortingHandler()} className="inline-flex items-center gap-1 hover:text-foreground">
                      {flexRender(h.column.columnDef.header, h.getContext())}
                      {h.column.getIsSorted() === 'asc' && <ChevronUp className="h-3 w-3" />}
                      {h.column.getIsSorted() === 'desc' && <ChevronDown className="h-3 w-3" />}
                    </button>
                  </th>
                ))}
              </tr>
            ))}
          </thead>
          <tbody>
            {table.getRowModel().rows.map((row) => (
              <tr key={row.id} ref={row.original.id === highlightedJobId ? highlightedRef : null}
                className={`border-b border-border/50 transition-colors hover:bg-background-secondary/50 ${row.original.id === highlightedJobId ? 'bg-emerald-500/20 ring-2 ring-emerald-500/50' : ''}`}>
                {row.getVisibleCells().map((cell) => (
                  <td key={cell.id} className="px-4 py-3">{flexRender(cell.column.columnDef.cell, cell.getContext())}</td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
        {jobs.length === 0 && <div className="py-12 text-center"><p className="text-foreground-muted">No jobs found</p></div>}
      </div>
    </div>
  );
}
