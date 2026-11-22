import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { Subject, of } from 'rxjs';
import { claimUrlFor, newUnread } from '../../_helpers/notifications';
import type { AppNotification, NotificationList } from '../../_models/api-types';
import type { ClaimUpdatedMessage } from '../../_models/realtime-messages';
import { AuthStore } from '../../_services/auth-store';
import { NotificationSound } from '../../_services/notification-sound';
import { LiveState, RealtimeService } from '../../_services/realtime';
import { loginResponse } from '../../_testing';
import { NotificationBell } from './notification-bell';

const note = (id: string, isRead = false): AppNotification => ({
  id,
  claimId: `claim-${id}`,
  claimNumber: 'CLM-MY-2026-000003',
  message: `Your claim CLM-MY-2026-000003 was approved (${id}).`,
  createdAt: '2026-11-22T08:00:00Z',
  isRead,
});
const list = (...items: AppNotification[]): NotificationList => ({
  unreadCount: items.filter((n) => !n.isRead).length,
  items,
});

describe('newUnread', () => {
  it('announces nothing on the first load (those were waiting before sign-in)', () => {
    expect(newUnread(null, [note('a')])).toEqual([]);
  });

  it('announces only unread notifications that are new since the last load', () => {
    const seen = new Set(['a']);
    expect(newUnread(seen, [note('a'), note('b'), note('c', true)]).map((n) => n.id)).toEqual([
      'b',
    ]);
  });

  it('leads to the claim in the user own area', () => {
    expect(claimUrlFor('Claimant', 'x')).toEqual(['/claimant/claims', 'x']);
    expect(claimUrlFor('ClaimsOfficer', 'x')).toEqual(['/staff/claims', 'x']);
  });
});

describe('NotificationBell', () => {
  async function render() {
    sessionStorage.clear();
    const updates = new Subject<ClaimUpdatedMessage>();
    const play = vi.fn();
    const toast = vi.fn(() => ({ onAction: () => of() }));
    const navigate = vi.fn(async () => true);
    TestBed.configureTestingModule({
      imports: [NotificationBell],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: RealtimeService,
          useValue: { claimUpdated$: updates, state: signal<LiveState>('live') },
        },
        { provide: NotificationSound, useValue: { play, enabled: signal(true), toggle: vi.fn() } },
        { provide: MatSnackBar, useValue: { open: toast } },
        { provide: Router, useValue: { navigate } },
      ],
    });
    TestBed.inject(AuthStore).signIn(loginResponse('Claimant'));
    const backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(NotificationBell);
    fixture.detectChanges();
    const flushList = async (body: NotificationList) => {
      backend.expectOne((r) => r.method === 'GET' && r.url === '/api/notifications').flush(body);
      await fixture.whenStable();
      fixture.detectChanges();
    };
    return { fixture, backend, updates, play, toast, navigate, flushList };
  }

  it('shows the unread count, but stays quiet about notifications that were already waiting', async () => {
    const { fixture, play, toast, flushList } = await render();

    await flushList(list(note('a'), note('b', true)));

    const badge = (fixture.nativeElement as HTMLElement).querySelector('.mat-badge-content');
    expect(badge?.textContent?.trim()).toBe('1');
    expect(play).not.toHaveBeenCalled();
    expect(toast).not.toHaveBeenCalled();
  });

  it('chimes and shows a pop-up with an Open action when a new one arrives live', async () => {
    const { updates, play, toast, flushList } = await render();
    await flushList(list(note('a', true)));

    updates.next({
      claimId: 'claim-b',
      claimNumber: 'CLM-MY-2026-000003',
      market: 'MY',
      eventType: 'ClaimApproved',
      status: 'Approved',
      occurredAt: '2026-11-22T08:00:00Z',
    });
    TestBed.tick(); // the reload's request goes out on the next change-detection pass
    await flushList(list(note('b'), note('a', true)));

    expect(play).toHaveBeenCalledTimes(1);
    expect(toast).toHaveBeenCalledWith(note('b').message, 'Open', expect.anything());
  });

  it('clicking a notification marks it read and opens the claim', async () => {
    const { fixture, backend, navigate, flushList } = await render();
    await flushList(list(note('a')));

    (fixture.nativeElement as HTMLElement).querySelector('button')!.click(); // open the menu
    fixture.detectChanges();
    (document.querySelector('.notification-menu .item') as HTMLButtonElement).click();

    backend.expectOne({ method: 'POST', url: '/api/notifications/a/read' }).flush(null);
    await new Promise((resolve) => setTimeout(resolve));
    await flushList(list(note('a', true)));
    expect(navigate).toHaveBeenCalledWith(['/claimant/claims', 'claim-a']);
  });
});
