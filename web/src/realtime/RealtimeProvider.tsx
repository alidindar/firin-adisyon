import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';

type Status = 'connecting' | 'connected' | 'reconnecting';

const RealtimeContext = createContext<Status>('connecting');
export const useConnection = () => useContext(RealtimeContext);
export const useOnline = () => useConnection() === 'connected';

/**
 * Hub'a bağlanır, olay gelince ilgili sorguları geçersiz sayar.
 * Bağlantı koparsa sonsuza kadar yeniden dener; tablet uykudan uyanınca hemen dener.
 */
export function RealtimeProvider({ children }: { children: ReactNode }) {
  const qc = useQueryClient();
  const [status, setStatus] = useState<Status>('connecting');

  useEffect(() => {
    let stopped = false;
    let retryTimer: number | undefined;

    const conn = new HubConnectionBuilder()
      .withUrl('/hubs/adisyon')
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (c) => Math.min(1000 * 2 ** c.previousRetryCount, 5000) })
      .configureLogging(LogLevel.Warning)
      .build();

    conn.on('TableChanged', (tableId: number) => {
      qc.invalidateQueries({ queryKey: ['tables'] });
      qc.invalidateQueries({ queryKey: ['table', tableId] });
    });
    conn.on('TablesChanged', () => {
      qc.invalidateQueries({ queryKey: ['tables'] });
      qc.invalidateQueries({ queryKey: ['admin', 'tables'] });
    });
    conn.on('CatalogChanged', () => {
      qc.invalidateQueries({ queryKey: ['catalog'] });
      qc.invalidateQueries({ queryKey: ['admin', 'products'] });
      qc.invalidateQueries({ queryKey: ['admin', 'categories'] });
    });

    const schedule = () => {
      window.clearTimeout(retryTimer);
      retryTimer = window.setTimeout(start, 3000);
    };
    const start = async () => {
      if (stopped || conn.state !== HubConnectionState.Disconnected) return;
      try {
        await conn.start();
        if (stopped) return;
        setStatus('connected');
        qc.invalidateQueries(); // kopukken kaçan değişiklikleri al
      } catch {
        if (!stopped) {
          setStatus('reconnecting');
          schedule();
        }
      }
    };

    conn.onreconnecting(() => setStatus('reconnecting'));
    conn.onreconnected(() => {
      setStatus('connected');
      qc.invalidateQueries();
    });
    conn.onclose(() => {
      if (stopped) return;
      setStatus('reconnecting');
      schedule();
    });

    const onVisible = () => {
      if (document.visibilityState === 'visible') start();
    };
    document.addEventListener('visibilitychange', onVisible);
    start();

    return () => {
      stopped = true;
      window.clearTimeout(retryTimer);
      document.removeEventListener('visibilitychange', onVisible);
      conn.stop();
    };
  }, [qc]);

  return <RealtimeContext.Provider value={status}>{children}</RealtimeContext.Provider>;
}
