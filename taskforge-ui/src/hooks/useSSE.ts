import { useEffect, useRef, useState, useCallback } from 'react';
import type { QueueMetrics } from '../types';

interface UseSseOptions {
  onMessage?: (data: QueueMetrics) => void;
  onError?: (error: Event) => void;
}

export function useSSE(url: string, options?: UseSseOptions) {
  const [metrics, setMetrics] = useState<QueueMetrics | null>(null);
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
    };

    eventSource.onmessage = (event) => {
      try {
        const data = JSON.parse(event.data) as QueueMetrics;
        setMetrics(data);
        // Use ref to access latest callback without causing re-render
        optionsRef.current?.onMessage?.(data);
      } catch {
        console.error('Failed to parse SSE message');
      }
    };

    eventSource.onerror = (event) => {
      setConnected(false);
      setError('Connection lost');
      optionsRef.current?.onError?.(event);
      eventSource.close();
      // Reconnect after 3 seconds
      reconnectTimeoutRef.current = setTimeout(connect, 3000);
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

  return { metrics, connected, error };
}