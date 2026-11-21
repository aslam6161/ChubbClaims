import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { timeAgo } from '../_helpers/time-ago';
import type { ClaimUpdatedMessage } from '../_models/realtime-messages';
import { FALLBACK_POLL_MS, LiveState, RealtimeService, reloadOnClaimUpdates } from './realtime';

const update = (claimId: string): ClaimUpdatedMessage => ({
  claimId,
  claimNumber: 'CLM-MY-2026-000001',
  market: 'MY',
  eventType: 'ClaimApproved',
  status: 'Approved',
  occurredAt: '2026-11-21T02:00:00Z',
});

describe('reloadOnClaimUpdates', () => {
  let updates: Subject<ClaimUpdatedMessage>;
  let state: ReturnType<typeof signal<LiveState>>;
  const reload = vi.fn();

  @Component({ template: '' })
  class Screen {
    constructor() {
      reloadOnClaimUpdates({ reload }, (m) => m.claimId === 'mine');
    }
  }

  beforeEach(() => {
    vi.useFakeTimers();
    reload.mockClear();
    updates = new Subject<ClaimUpdatedMessage>();
    state = signal<LiveState>('live');
    TestBed.configureTestingModule({
      providers: [{ provide: RealtimeService, useValue: { claimUpdated$: updates, state } }],
    });
  });

  afterEach(() => vi.useRealTimers());

  it('reloads only for matching updates', () => {
    TestBed.createComponent(Screen).detectChanges();

    updates.next(update('someone-else'));
    expect(reload).not.toHaveBeenCalled();

    updates.next(update('mine'));
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('polls every 30 s while live updates are paused, and stops once they are back', () => {
    state.set('paused');
    const fixture = TestBed.createComponent(Screen);
    fixture.detectChanges();
    TestBed.tick(); // run the effect

    vi.advanceTimersByTime(FALLBACK_POLL_MS * 2);
    expect(reload).toHaveBeenCalledTimes(2);

    state.set('live');
    TestBed.tick();
    vi.advanceTimersByTime(FALLBACK_POLL_MS * 3);
    expect(reload).toHaveBeenCalledTimes(2);
  });

  it('stops listening when the screen is destroyed', () => {
    const fixture = TestBed.createComponent(Screen);
    fixture.detectChanges();
    fixture.destroy();

    updates.next(update('mine'));
    expect(reload).not.toHaveBeenCalled();
  });
});

describe('timeAgo', () => {
  const now = new Date('2026-11-21T10:00:00Z');
  const before = (seconds: number) => new Date(now.getTime() - seconds * 1000).toISOString();

  it('says how fresh a figure is', () => {
    expect(timeAgo(before(2), now)).toBe('just now');
    expect(timeAgo(before(45), now)).toBe('45 s ago');
    expect(timeAgo(before(3 * 60), now)).toBe('3 min ago');
    expect(timeAgo(before(2 * 3600), now)).toBe('2 h ago');
    expect(timeAgo(before(86400), now)).toBe('1 day ago');
    expect(timeAgo(before(4 * 86400), now)).toBe('4 days ago');
  });
});
