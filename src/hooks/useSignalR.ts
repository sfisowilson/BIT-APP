import { useEffect, useRef, useState, useCallback } from 'react';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';

export interface DetectionProgressEvent {
  contentId: string;
  percent: number;
  status: string;
  jobId: string | null;
}

export interface RenderProgressEvent {
  renderId: string;
  percent: number;
  status: string;
}

export interface ContentStatusEvent {
  contentId: string;
  newStatus: string;
  message: string | null;
}

export interface AlarmEvent {
  // dynamic from server
  [key: string]: unknown;
}

export interface NotificationEvent {
  type: string;
  title: string;
  message: string;
  timestamp: string;
}

interface SignalRCallbacks {
  onDetectionProgress?: (e: DetectionProgressEvent) => void;
  onRenderProgress?: (e: RenderProgressEvent) => void;
  onContentStatusChanged?: (e: ContentStatusEvent) => void;
  onAlarmEvent?: (e: AlarmEvent) => void;
  onNotification?: (e: NotificationEvent) => void;
}

const HUB_URL = '/hubs/bit';

function getAccessToken(): string | undefined {
  try {
    const raw = localStorage.getItem('token');
    if (raw) {
      const parsed = JSON.parse(raw);
      return parsed?.token || parsed;
    }
    return undefined;
  } catch {
    return localStorage.getItem('token') || undefined;
  }
}

/**
 * useSignalR – one persistent SignalR connection that pushes real‑time
 * pipeline progress, content status changes, alarms, and notifications
 * to the UI.  Replace polling in App.tsx with this hook.
 */
export function useSignalR(callbacks: SignalRCallbacks) {
  const [connectionState, setConnectionState] = useState<HubConnectionState>(
    HubConnectionState.Disconnected,
  );
  const connectionRef = useRef<HubConnection | null>(null);
  // Stable callback ref so the useEffect closure always sees the latest callbacks
  const callbacksRef = useRef<SignalRCallbacks>(callbacks);
  callbacksRef.current = callbacks;

  const start = useCallback(async () => {
    if (connectionRef.current?.state === HubConnectionState.Connected) return;

    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL, {
        accessTokenFactory: () => getAccessToken() || '',
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();

    // ── Wire up server → client invocations ──
    connection.on('DetectionProgress', (contentId: string, percent: number, status: string, jobId: string | null) => {
      callbacksRef.current.onDetectionProgress?.({ contentId, percent, status, jobId });
    });
    connection.on('RenderProgress', (renderId: string, percent: number, status: string) => {
      callbacksRef.current.onRenderProgress?.({ renderId, percent, status });
    });
    connection.on('ContentStatusChanged', (contentId: string, newStatus: string, message: string | null) => {
      callbacksRef.current.onContentStatusChanged?.({ contentId, newStatus, message });
    });
    connection.on('AlarmEvent', (alarm: AlarmEvent) => {
      callbacksRef.current.onAlarmEvent?.(alarm);
    });
    connection.on('Notification', (type: string, title: string, message: string, timestamp: string) => {
      callbacksRef.current.onNotification?.({ type, title, message, timestamp });
    });

    connection.onreconnecting(() => setConnectionState(HubConnectionState.Reconnecting));
    connection.onreconnected(() => setConnectionState(HubConnectionState.Connected));
    connection.onclose(() => setConnectionState(HubConnectionState.Disconnected));

    try {
      await connection.start();
      setConnectionState(HubConnectionState.Connected);
      connectionRef.current = connection;
    } catch (err) {
      console.error('[SignalR] Connection failed:', err);
      // Retry after 5 s
      setTimeout(() => start(), 5000);
    }
  }, []);

  const stop = useCallback(async () => {
    if (connectionRef.current) {
      await connectionRef.current.stop();
      connectionRef.current = null;
      setConnectionState(HubConnectionState.Disconnected);
    }
  }, []);

  useEffect(() => {
    start();
    return () => {
      stop();
    };
  }, [start, stop]);

  return { connectionState, start, stop };
}
