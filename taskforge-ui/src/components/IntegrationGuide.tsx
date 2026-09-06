import { useState } from 'react';
import { X, Code, Copy, Check, BookOpen } from 'lucide-react';

interface IntegrationGuideProps {
  isOpen: boolean;
  onClose: () => void;
}

type Language = 'curl' | 'csharp' | 'javascript' | 'python';

const SNIPPETS: Record<Language, { label: string; code: string }> = {
  curl: {
    label: 'cURL',
    code: `curl -X POST http://localhost:5000/api/v1/jobs/enqueue \\
  -H "Content-Type: application/json" \\
  -d '{\n    "queueName": "default",\n    "payload": "{\\"task\\":\\"my-job\\"}",\n    "maxRetries": 3\n  }'`,
  },
  csharp: {
    label: 'C#',
    code: `using var client = new HttpClient();\n\nvar payload = new {\n    queueName = "default",\n    payload = "{\\"task\\":\\"my-job\\"}",\n    maxRetries = 3\n};\n\nvar response = await client.PostAsJsonAsync(\n    "http://localhost:5000/api/v1/jobs/enqueue",\n    payload);\n\nvar result = await response.Content.ReadAsJsonAsync<EnqueueResponse>();\nConsole.WriteLine($"Job {result.JobId} queued!");`,
  },
  javascript: {
    label: 'JavaScript',
    code: `const response = await fetch('http://localhost:5000/api/v1/jobs/enqueue', {\n  method: 'POST',\n  headers: { 'Content-Type': 'application/json' },\n  body: JSON.stringify({\n    queueName: 'default',\n    payload: JSON.stringify({ task: 'my-job' }),\n    maxRetries: 3\n  })\n});\n\nconst result = await response.json();\nconsole.log('Job ' + result.jobId + ' queued!');`,
  },
  python: {
    label: 'Python',
    code: `import requests\n\nresponse = requests.post(\n    'http://localhost:5000/api/v1/jobs/enqueue',\n    json={\n        'queueName': 'default',\n        'payload': '{\\"task\\":\\"my-job\\"}',\n        'maxRetries': 3\n    }\n)\n\nresult = response.json()\nprint(f"Job {result['jobId']} queued!")`,
  },
};

export function IntegrationGuide({ isOpen, onClose }: IntegrationGuideProps) {
  const [activeTab, setActiveTab] = useState<Language>('curl');
  const [copied, setCopied] = useState(false);

  if (!isOpen) return null;

  const handleCopy = async () => {
    await navigator.clipboard.writeText(SNIPPETS[activeTab].code);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="w-full max-w-3xl max-h-[90vh] overflow-y-auto rounded-2xl border border-border bg-background-primary shadow-2xl">
        <div className="sticky top-0 z-10 flex items-center justify-between border-b border-border bg-background-primary px-6 py-4">
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-blue-500/20">
              <BookOpen className="h-5 w-5 text-blue-400" />
            </div>
            <div>
              <h2 className="text-lg font-semibold">Integration Guide</h2>
              <p className="text-sm text-foreground-muted">Connect your app to TaskForge</p>
            </div>
          </div>
          <button onClick={onClose} className="rounded-lg p-2 hover:bg-background-secondary"><X className="h-5 w-5" /></button>
        </div>
        <div className="p-6">
          <div className="mb-6 rounded-lg bg-amber-500/10 border border-amber-500/30 p-4">
            <p className="text-sm text-amber-200">
              <strong>Base URL:</strong> <code className="font-mono text-amber-300">http://localhost:5000</code>
            </p>
            <p className="text-sm text-amber-200 mt-1">
              <strong>Endpoint:</strong> <code className="font-mono text-amber-300">POST /api/v1/jobs/enqueue</code>
            </p>
          </div>
          <div className="flex gap-2 mb-4">
            {(Object.keys(SNIPPETS) as Language[]).map(lang => (
              <button key={lang} onClick={() => setActiveTab(lang)}
                className={`flex items-center gap-1.5 rounded-lg px-4 py-2 text-sm font-medium transition-all ${activeTab === lang ? 'bg-blue-500/20 text-blue-400 border border-blue-500/50' : 'bg-background-secondary text-foreground-muted hover:text-foreground'}`}>
                <Code className="h-4 w-4" />{SNIPPETS[lang].label}
              </button>
            ))}
          </div>
          <div className="relative">
            <button onClick={handleCopy}
              className="absolute right-3 top-3 flex items-center gap-1.5 rounded-lg bg-background-secondary px-3 py-1.5 text-xs font-medium text-foreground-muted hover:text-foreground transition-colors">
              {copied ? <><Check className="h-3 w-3 text-emerald-400" />Copied!</> : <><Copy className="h-3 w-3" />Copy</>}
            </button>
            <pre className="overflow-x-auto rounded-lg border border-border bg-background-secondary p-4 text-sm font-mono text-foreground">
              <code>{SNIPPETS[activeTab].code}</code>
            </pre>
          </div>
          <div className="mt-6 rounded-lg border border-border p-4">
            <h3 className="text-sm font-semibold mb-3">Request Schema</h3>
            <div className="space-y-2 text-sm">
              <div className="flex justify-between"><span className="text-foreground-muted">queueName</span><code className="font-mono text-blue-400">string</code></div>
              <div className="flex justify-between"><span className="text-foreground-muted">payload</span><code className="font-mono text-blue-400">string (JSON)</code></div>
              <div className="flex justify-between"><span className="text-foreground-muted">maxRetries</span><code className="font-mono text-blue-400">number</code></div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
