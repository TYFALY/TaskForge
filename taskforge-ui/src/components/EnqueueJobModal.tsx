import { useState } from 'react';
import { X, Send, Loader2, AlertCircle, CheckCircle } from 'lucide-react';
import type { Job, JobStatus } from '../types';

interface EnqueueJobModalProps {
  isOpen: boolean;
  onClose: () => void;
  onJobCreated: (job: Job) => void;
}

const PRESETS = ['default', 'emails', 'reports', 'notifications'];

const createLocalJob = (queueName: string, payload: string, maxRetries: number): Job => ({
  id: 'job-' + Date.now().toString(36) + '-' + Math.random().toString(36).slice(2, 6),
  queueName,
  payload,
  status: 'Queued' as JobStatus,
  retryCount: 0,
  maxRetries,
  createdAt: new Date().toISOString(),
  updatedAt: new Date().toISOString(),
});

export function EnqueueJobModal({ isOpen, onClose, onJobCreated }: EnqueueJobModalProps) {
  const [queue, setQueue] = useState('default');
  const [custom, setCustom] = useState('');
  const [isCustom, setIsCustom] = useState(false);
  const [payload, setPayload] = useState('{"task":"my-task"}');
  const [retries, setRetries] = useState(3);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  if (!isOpen) return null;

  const qName = isCustom ? custom.trim() : queue;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    if (!qName) { setError('Select a queue'); return; }
    try { JSON.parse(payload); } catch { setError('Invalid JSON'); return; }
    setLoading(true);
    try {
      const res = await fetch('/api/v1/jobs/enqueue', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ queueName: qName, payload, maxRetries: retries }),
      });
      if (!res.ok) throw new Error('HTTP ' + res.status);
      const data = await res.json();
      // Create local job representation
      const localJob = createLocalJob(qName, payload, retries);
      localJob.id = data.jobId || localJob.id;
      setSuccess('Job ' + (data.jobId || '').slice(0, 8) + ' queued!');
      onJobCreated(localJob);
      setTimeout(() => { onClose(); setPayload('{"task":"my-task"}'); setRetries(3); setSuccess(null); }, 1500);
    } catch (err) { setError(err instanceof Error ? err.message : 'Failed'); }
    finally { setLoading(false); }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="w-full max-w-xl rounded-2xl border border-border bg-background-primary shadow-2xl">
        <div className="flex items-center justify-between border-b border-border px-6 py-4">
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-emerald-500/20"><Send className="h-5 w-5 text-emerald-glow" /></div>
            <div><h2 className="text-lg font-semibold">Create New Task</h2><p className="text-sm text-foreground-muted">Enqueue a job</p></div>
          </div>
          <button onClick={onClose} className="rounded-lg p-2 hover:bg-background-secondary"><X className="h-5 w-5" /></button>
        </div>
        <form onSubmit={handleSubmit} className="p-6 space-y-5">
          <div>
            <label className="block text-sm font-medium mb-2">Queue</label>
            <div className="flex flex-wrap gap-2">
              {PRESETS.map(q => (
                <button key={q} type="button" onClick={() => { setQueue(q); setIsCustom(false); }}
                  className={'rounded-lg px-3 py-1.5 text-sm ' + (!isCustom && queue === q ? 'bg-emerald-500/20 text-emerald-glow border border-emerald-500/50' : 'bg-background-secondary text-foreground-muted')}>{q}</button>
              ))}
              <button type="button" onClick={() => setIsCustom(true)}
                className={'rounded-lg px-3 py-1.5 text-sm ' + (isCustom ? 'bg-emerald-500/20 text-emerald-glow border border-emerald-500/50' : 'bg-background-secondary text-foreground-muted')}>+ Custom</button>
            </div>
            {isCustom && <input className="mt-2 w-full rounded-lg border border-border bg-background-secondary px-4 py-2 text-sm focus:border-emerald-500/50 focus:outline-none" placeholder="Queue name" value={custom} onChange={e => setCustom(e.target.value)} autoFocus />}
          </div>
          <div>
            <label className="block text-sm font-medium mb-2">Payload (JSON)</label>
            <textarea className="w-full rounded-lg border border-border bg-background-secondary px-4 py-3 font-mono text-sm resize-none focus:border-emerald-500/50 focus:outline-none" rows={5} value={payload} onChange={e => setPayload(e.target.value)} />
          </div>
          <div>
            <label className="block text-sm font-medium mb-2">Max Retries: <span className="text-emerald-glow">{retries}</span></label>
            <input type="range" min="0" max="10" value={retries} onChange={e => setRetries(parseInt(e.target.value))} className="w-full accent-emerald-500" />
          </div>
          {error && <div className="flex items-center gap-2 rounded-lg bg-red-500/10 border border-red-500/30 px-4 py-3 text-sm text-red-400"><AlertCircle className="h-4 w-4" />{error}</div>}
          {success && <div className="flex items-center gap-2 rounded-lg bg-emerald-500/10 border border-emerald-500/30 px-4 py-3 text-sm text-emerald-glow"><CheckCircle className="h-4 w-4" />{success}</div>}
          <div className="flex justify-end gap-3">
            <button type="button" onClick={onClose} className="rounded-lg border border-border px-5 py-2.5 text-sm hover:bg-background-secondary">Cancel</button>
            <button type="submit" disabled={loading} className="inline-flex items-center gap-2 rounded-lg bg-gradient-to-r from-emerald-600 to-blue-600 px-5 py-2.5 text-sm font-medium text-white disabled:opacity-50">
              {loading ? <><Loader2 className="h-4 w-4 animate-spin" />Enqueuing...</> : <><Send className="h-4 w-4" />Enqueue</>}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
