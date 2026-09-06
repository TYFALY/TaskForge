import { useEffect, useRef } from 'react';
import { Job } from '../types';

interface UseJobEventsOptions {
  onJobEnqueued?: (job: Job) => void;
  onJobUpdated?: (job: Job) => void;
}

export function useJobEvents(options: UseJobEventsOptions = {}) {
  const optionsRef = useRef(options);
  optionsRef.current = options;

  useEffect(() => {
    let eventSource: EventSource | null = null;
    let reconnectTimeout: NodeJS.Timeout;

    const connect = () => {
      eventSource = new EventSource('/api/v1/jobs/stream');

      eventSource.addEventListener('job_enqueued', (event: MessageEvent) => {
        try {
          const job: Job = JSON.parse(event.data);
          optionsRef.current.onJobEnqueued?.(job);
        } catch (err) {
          console.error('Failed to parse job_enqueued event', err);
        }
      });

      eventSource.addEventListener('job_updated', (event: MessageEvent) => {
        try {
          const job: Job = JSON.parse(event.data);
          optionsRef.current.onJobUpdated?.(job);
        } catch (err) {
          console.error('Failed to parse job_updated event', err);
        }
      });

      eventSource.onerror = () => {
        eventSource?.close();
        reconnectTimeout = setTimeout(connect, 3000);
      };
    };

    connect();

    return () => {
      if (eventSource) {
        eventSource.close();
      }
      clearTimeout(reconnectTimeout);
    };
  }, []);
}
﻿import { useEffect, useRef, useState, useCallback } from '"'react'"';
import type { Job, JobEvent } from '"'../types'"';

interface UseJobEventsOptions {
  onJobEnqueued?: (job: Job) => void;
  onJobUpdated?: (job: Job) => void;
}

export function useJobEvents(url: string, options?: UseJobEventsOptions) {
  const [connected, setConnected] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const eventSourceRef = useRef<EventSource | null>(null);
  const reconnectTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  
  // Use refs to avoid dependency issues - callbacks will use latest values
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

    // Handle job_enqueued events - data is already the full Job object
    eventSource.addEventListener('job_enqueued', (event: MessageEvent) => {
      try {
        const jobData = JSON.parse(event.data);
        console.log('[SSE] Job enqueued:', jobData);
        
        const job: Job = {
          id: jobData.id || jobData.JobId,
          queueName: jobData.queueName || '"'default'"',
          payload: jobData.payload || '"''"',
          status: (jobData.status || '"'Queued'"') as Job['"'status'"'],
          jobType: jobData.jobType || '"'Default'"',
          retryCount: jobData.retryCount || 0,
          maxRetries: jobData.maxRetries || 3,
          createdAt: jobData.createdAt || jobData.EnqueuedAt || new Date().toISOString(),
          updatedAt: jobData.updatedAt || jobData.EnqueuedAt || new Date().toISOString(),
        };
        
        // Use ref to access latest callback without causing re-render
        optionsRef.current?.onJobEnqueued?.(job);
      } catch (err) {
        console.error('[SSE] Failed to parse job_enqueued event:', err);
      }
    });

    // Handle job_updated events - may be partial data or full job
    eventSource.addEventListener('"'job_updated'"', (event: MessageEvent) => {
      try {
        const jobData = JSON.parse(event.data);
        console.log('[SSE] Job updated:', jobData);
        
        // Job data can be either full job or just { id, status }
        const job: Job = {
          id: jobData.id || jobData.JobId,
          queueName: jobData.queueName || '"'default'"',
          payload: jobData.payload || '"''"',
          status: (jobData.status || jobData.Status || '"'Unknown'"') as Job['"'status'"'],
          jobType: jobData.jobType || jobData.JobType || '"'Default'"',
          retryCount: jobData.retryCount || jobData.RetryCount || 0,
          maxRetries: jobData.maxRetries || jobData.MaxRetries || 3,
          createdAt: jobData.createdAt || jobData.CreatedAt || new Date().toISOString(),
          updatedAt: jobData.updatedAt || jobData.UpdatedAt || new Date().toISOString(),
        };
        
        // Use ref to access latest callback without causing re-render
        optionsRef.current?.onJobUpdated?.(job);
      } catch (err) {
        console.error('[SSE] Failed to parse job_updated event:', err);
      }
    });

    eventSource.onerror = () => {
      setConnected(false);
      setError('"'Connection lost'"');
      console.error('[SSE] Job events error');
      eventSource.close();
      // Reconnect after 5 seconds
      reconnectTimeoutRef.current = setTimeout(connect, 5000);
    };

    return eventSource;
  }, [url]); // Only reconnect if URL changes, not options

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
