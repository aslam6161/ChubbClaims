import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { problemInterceptor } from '../../_interceptors/problem.interceptor';
import type { WorkQueueItem, WorkQueuePage } from '../../_models/api-types';
import { WorkQueue } from './work-queue';

function item(overrides: Partial<WorkQueueItem>): WorkQueueItem {
  return {
    id: 'c1',
    claimNumber: 'CLM-MY-2026-000001',
    type: 'Motor',
    market: 'MY',
    status: 'Submitted',
    priority: 'Normal',
    reportedAt: '2026-11-20T02:00:00Z',
    ageDays: 1,
    slaDueAt: '2026-11-30T02:00:00Z',
    isOverdue: false,
    waitingOn: 'Officer',
    assignedOfficerId: null,
    outstandingReserve: 8500,
    currency: 'MYR',
    ...overrides,
  };
}

function pageOf(items: WorkQueueItem[], overdueCount = 0): WorkQueuePage {
  return { items, page: 1, pageSize: 20, totalCount: items.length, overdueCount };
}

/** The queue request for a tab (search and page are checked where they matter). */
const queueFor =
  (view: string) => (r: { url: string; params: { get(k: string): string | null } }) =>
    r.url === '/api/work-queue' && r.params.get('view') === view;

describe('WorkQueue', () => {
  let backend: HttpTestingController;
  let snackBar: { open: ReturnType<typeof vi.fn> };

  async function render(rows: WorkQueueItem[], overdueCount = 0) {
    snackBar = { open: vi.fn() };
    TestBed.configureTestingModule({
      imports: [WorkQueue],
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([problemInterceptor])),
        provideHttpClientTesting(),
        { provide: MatSnackBar, useValue: snackBar },
      ],
    });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(WorkQueue);
    fixture.detectChanges();
    backend.expectOne(queueFor('Unassigned')).flush(pageOf(rows, overdueCount));
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, page: fixture.nativeElement as HTMLElement };
  }

  it('renders the unassigned queue and highlights overdue claims', async () => {
    const { page } = await render(
      [
        item({ id: 'a', claimNumber: 'CLM-MY-2026-000002', isOverdue: true, ageDays: 14 }),
        item({ id: 'b', claimNumber: 'CLM-MY-2026-000001' }),
      ],
      1, // the server's count for the whole queue
    );

    const rows = page.querySelectorAll('tr.row');
    expect(rows.length).toBe(2);
    expect(rows[0].classList).toContain('row--overdue');
    expect(rows[1].classList).not.toContain('row--overdue');
    expect(page.textContent).toContain('1 overdue');
    expect(page.textContent?.replace(/ /g, ' ')).toContain('MYR 8,500.00');
  });

  it('on a 409 pick-up says another officer got it first and reloads the queue', async () => {
    const { fixture, page } = await render([item({ id: 'a', claimNumber: 'CLM-MY-2026-000002' })]);

    page.querySelector<HTMLButtonElement>('tr.row button')!.click();
    backend
      .expectOne('/api/claims/a/pick-up')
      .flush(
        { status: 409, title: 'Claim was changed by someone else' },
        { status: 409, statusText: 'Conflict' },
      );
    await new Promise((resolve) => setTimeout(resolve)); // let the 409 handler run

    expect(snackBar.open).toHaveBeenCalledWith(
      'CLM-MY-2026-000002 was already picked up by another officer.',
      'OK',
      expect.anything(),
    );
    // ...and the queue is reloaded (flush it so the fixture can become stable).
    backend.expectOne(queueFor('Unassigned')).flush(pageOf([]));
    await fixture.whenStable();
  });

  it('the overdue count is the whole queue, not just this page', async () => {
    const { page } = await render([item({})], 7);

    expect(page.textContent).toContain('7 overdue');
  });

  it('switching tabs keeps the search but starts at page 1', async () => {
    const { fixture } = await render(Array.from({ length: 20 }, (_, i) => item({ id: `r${i}` })));
    const component = fixture.componentInstance as unknown as {
      query: {
        setSearch(t: string): void;
        onPage(e: { pageIndex: number; pageSize: number }): void;
      };
      selectTab(i: number): void;
    };

    component.query.setSearch('ampang');
    TestBed.tick();
    backend.expectOne(queueFor('Unassigned')).flush({ ...pageOf([item({})]), totalCount: 45 });
    component.query.onPage({ pageIndex: 1, pageSize: 20 });
    TestBed.tick();
    const second = backend.expectOne(queueFor('Unassigned'));
    expect(second.request.params.get('page')).toBe('2');
    second.flush({ ...pageOf([item({})]), totalCount: 45 });

    component.selectTab(1);
    TestBed.tick();
    const mine = backend.expectOne(queueFor('Mine'));
    expect(mine.request.params.get('search')).toBe('ampang');
    expect(mine.request.params.get('page')).toBe('1');
    mine.flush(pageOf([]));
    await fixture.whenStable();
  });
});
