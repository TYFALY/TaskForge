import { useEffect, useRef, useState, useCallback } from 'react';
import type { Job } from '../types';

interface UseJobEventsOptions {
  onJobEnqueued?: (job: Job) => void;
  onJobUpdated?: (job: Job) => void;
}

export function useJobEvents(url: string, options?: UseJobEventsOptions) {
  const [connected, setConnected] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const eventSourceRef = useRef<EventSource | null>(null);
  const reconnectTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const optionsRef = useRef(options);
  optionsRef.current = options;

  const connect = useCallback(() => {
    if (eventSourceRef.current) {
      eventSourceRef.current.close();
    }

    const eventSource = new EventSource(url);
    eventSourceRef.current = eventSource;

    eventSource.onopen = () => {
      setConnected(true);
      setError(null);
      console.log('[SSE] Job events connected');
    };

    eventSource.addEventListener('job_enqueued', (event: MessageEvent) => {
      try {
        const jobData = JSON.parse(event.data);
        console.log('[SSE] Job enqueued:', jobData);

        const job: Job = {
          id: jobData.id || jobData.JobId,
          queueName: jobData.queueName || 'default',
          payload: jobData.payload || '',
          status: (jobData.status || 'Queued') as Job['status'],
          jobType: jobData.jobType || 'Default',
          retryCount: jobData.retryCount || 0,
          maxRetries: jobData.maxRetries || 3,
          createdAt: jobData.createdAt || jobData.EnqueuedAt || new Date().toISOString(),
          updatedAt: jobData.updatedAt || jobData.EnqueuedAt || new Date().toISOString(),
        };

        optionsRef.current?.onJobEnqueued?.(job);
      } catch (err) {
        console.error('[SSE] Failed to parse job_enqueued event:', err);
      }
    });

    eventSource.addEventListener('job_updated', (event: MessageEvent) => {
      try {
        const jobData = JSON.parse(event.data);
        console.log('[SSE] Job updated:', jobData);

        const job: Job = {
          id: jobData.id || jobData.JobId,
          queueName: jobData.queueName || 'default',
          payload: jobData.payload || '',
          status: (jobData.status || jobData.Status || 'Unknown') as Job['status'],
          jobType: jobData.jobType || jobData.JobType || 'Default',
          retryCount: jobData.retryCount || jobData.RetryCount || 0,
          maxRetries: jobData.maxRetries || jobData.MaxRetries || 3,
          createdAt: jobData.createdAt || jobData.CreatedAt || new Date().toISOString(),
          updatedAt: jobData.updatedAt || jobData.UpdatedAt || new Date().toISOString(),
        };

        optionsRef.current?.onJobUpdated?.(job);
      } catch (err) {
        console.error('[SSE] Failed to parse job_updated event:', err);
      }
    });

    eventSource.onerror = () => {
      setConnected(false);
      setError('Connection lost');
      console.error('[SSE] Job events error');
      eventSource.close();
      reconnectTimeoutRef.current = setTimeout(connect, 5000);
    };

    return eventSource;
  }, [url]);

  useEffect(() => {
    connect();

    return () => {
      if (reconnectTimeoutRef.current) {
        clearTimeout(reconnectTimeoutRef.current);
      }
      if (eventSourceRef.current) {
        eventSourceRef.current.close();
      }
    };
  }, [connect]);

  return { connected, error };
}
