import { Service } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import { RealtimeService } from './realtime';

/** A hub connection whose start() and close we control. */
function fakeConnection() {
  let finishStart!: () => void;
  let failStart!: (e: Error) => void;
  const started = new Promise<void>((resolve, reject) => {
    finishStart = resolve;
    failStart = reject;
  });
  let closed: (() => void) | undefined;
  const connection = {
    state: HubConnectionState.Connecting,
    on: vi.fn(),
    onreconnecting: vi.fn(),
    onreconnected: vi.fn(),
    onclose: (callback: () => void) => (closed = callback),
    start: () => started,
    stop: vi.fn(async () => undefined),
  };
  return {
    connection: connection as unknown as HubConnection,
    finishStart: () => finishStart(),
    failStart: (e: Error) => failStart(e),
    close: () => closed?.(),
  };
}

describe('RealtimeService connection state', () => {
  const queue: HubConnection[] = [];

  @Service()
  class TestRealtime extends RealtimeService {
    protected override createConnection(): HubConnection {
      return queue.shift()!;
    }
  }

  beforeEach(() => {
    queue.length = 0;
    TestBed.configureTestingModule({
      providers: [{ provide: RealtimeService, useClass: TestRealtime }],
    });
  });

  it('goes connecting → live, and paused when start fails', async () => {
    const ok = fakeConnection();
    const failing = fakeConnection();
    queue.push(ok.connection, failing.connection);
    const realtime = TestBed.inject(RealtimeService);

    const connecting = realtime.connect();
    expect(realtime.state()).toBe('connecting');
    ok.finishStart();
    await connecting;
    expect(realtime.state()).toBe('live');

    await realtime.disconnect();
    const retry = realtime.connect();
    failing.failStart(new Error('Failed to complete negotiation with the server'));
    await retry;
    expect(realtime.state()).toBe('paused');
  });

  it('ignores the old connection failing or closing after a quick sign-out and sign-in', async () => {
    const old = fakeConnection();
    const current = fakeConnection();
    queue.push(old.connection, current.connection);
    const realtime = TestBed.inject(RealtimeService);

    const first = realtime.connect(); // still negotiating…
    await realtime.disconnect(); // …when the user signs out
    const second = realtime.connect(); // and signs in again
    current.finishStart();
    await second;
    expect(realtime.state()).toBe('live');

    old.failStart(new Error('The connection was stopped during negotiation.'));
    await first;
    old.close();
    expect(realtime.state()).toBe('live');
  });
});
