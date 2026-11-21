import { DestroyRef, effect, inject, Service, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { filter, Observable, Subject } from 'rxjs';
import type { ClaimUpdatedMessage, ExposureUpdatedMessage } from '../_models/realtime-messages';
import { AuthStore } from './auth-store';

export type LiveState = 'connecting' | 'live' | 'reconnecting' | 'paused';

/** While live updates are down, visible data is polled at this interval instead. */
export const FALLBACK_POLL_MS = 30_000;

/** Anything that can re-fetch itself, e.g. an httpResource. */
export interface Reloadable {
  reload(): unknown;
}

/**
 * One SignalR connection per signed-in session (opened by the area layout, closed on sign-out).
 * The server decides which updates we receive (our claims, our market, managers), from the JWT.
 */
@Service()
export class RealtimeService {
  private readonly auth = inject(AuthStore);
  private connection: HubConnection | null = null;
  private readonly claimUpdates = new Subject<ClaimUpdatedMessage>();
  private readonly exposureUpdates = new Subject<ExposureUpdatedMessage>();

  readonly state = signal<LiveState>('paused');
  readonly claimUpdated$: Observable<ClaimUpdatedMessage> = this.claimUpdates.asObservable();
  readonly exposureUpdated$: Observable<ExposureUpdatedMessage> =
    this.exposureUpdates.asObservable();

  async connect(): Promise<void> {
    if (this.connection) return;
    const connection = this.createConnection();
    this.connection = connection;
    // After a quick sign-out and sign-in, the old connection's late callbacks must not
    // overwrite the new connection's state.
    const setState = (s: LiveState) => {
      if (this.connection === connection) this.state.set(s);
    };

    connection.on('ClaimUpdated', (m: ClaimUpdatedMessage) => this.claimUpdates.next(m));
    connection.on('ExposureUpdated', (m: ExposureUpdatedMessage) => this.exposureUpdates.next(m));
    connection.onreconnecting(() => setState('reconnecting'));
    connection.onreconnected(() => setState('live'));
    connection.onclose(() => setState('paused'));

    setState('connecting');
    try {
      await connection.start();
      setState('live');
    } catch {
      // Includes a page reload during negotiation: the browser cancels the request and SignalR
      // logs it to the console. Screens fall back to polling; the app works without push.
      setState('paused');
    }
  }

  /** Separate so a test can supply a fake connection. */
  protected createConnection(): HubConnection {
    return new HubConnectionBuilder()
      .withUrl('/hubs/claims', { accessTokenFactory: () => this.auth.token() ?? '' })
      .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
      .configureLogging(LogLevel.Warning)
      .build();
  }

  async disconnect(): Promise<void> {
    const connection = this.connection;
    this.connection = null;
    this.state.set('paused');
    if (connection && connection.state !== HubConnectionState.Disconnected) await connection.stop();
  }
}

/**
 * Keeps a screen's data fresh: reloads `resource` when a matching ClaimUpdated arrives, and polls
 * every 30 s while live updates are paused. Call from a component constructor (injection context);
 * everything is cleaned up when the component is destroyed.
 */
export function reloadOnClaimUpdates(
  resource: Reloadable,
  matches: (m: ClaimUpdatedMessage) => boolean = () => true,
): void {
  const realtime = inject(RealtimeService);
  realtime.claimUpdated$
    .pipe(filter(matches), takeUntilDestroyed())
    .subscribe(() => resource.reload());
  pollWhilePaused(realtime, () => resource.reload());
}

/** Same as reloadOnClaimUpdates, for the exposure read model (managers). */
export function reloadOnExposureUpdates(resource: Reloadable): void {
  const realtime = inject(RealtimeService);
  realtime.exposureUpdated$.pipe(takeUntilDestroyed()).subscribe(() => resource.reload());
  pollWhilePaused(realtime, () => resource.reload());
}

function pollWhilePaused(realtime: RealtimeService, reload: () => void): void {
  const destroyRef = inject(DestroyRef);
  let timer: ReturnType<typeof setInterval> | undefined;
  const stop = () => {
    clearInterval(timer);
    timer = undefined;
  };
  effect(() => {
    if (realtime.state() === 'live') stop();
    else if (timer === undefined) timer = setInterval(reload, FALLBACK_POLL_MS);
  });
  destroyRef.onDestroy(stop);
}
