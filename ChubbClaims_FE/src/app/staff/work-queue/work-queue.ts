import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Toast } from '../../_services/toast';
import { MatTableModule } from '@angular/material/table';
import { MatTabsModule } from '@angular/material/tabs';
import { Router, RouterLink } from '@angular/router';
import { waitingOnLabel } from '../../_helpers/claim-status';
import { createListQuery, PAGE_SIZES } from '../../_helpers/paging';
import { keepWhileLoading } from '../../_helpers/resource';
import { reloadOnClaimUpdates } from '../../_services/realtime';
import type { WorkQueueItem, WorkQueuePage, WorkQueueView } from '../../_models/api-types';
import { AppError } from '../../_models/app-error';
import { MoneyPipe } from '../../_shared/money.pipe';
import { PageState } from '../../_shared/page-state/page-state';
import { SearchBox } from '../../_shared/search-box/search-box';
import { StatusBadge } from '../../_shared/status-badge/status-badge';
import { StaffClaims } from '../staff-claims';

/** The officer's market queue: claims nobody has picked up, and my open claims. Most urgent first. */
@Component({
  selector: 'app-work-queue',
  imports: [
    DatePipe,
    MatButtonModule,
    MatIconModule,
    MatPaginatorModule,
    MatProgressSpinnerModule,
    MatTableModule,
    MatTabsModule,
    MoneyPipe,
    PageState,
    RouterLink,
    SearchBox,
    StatusBadge,
  ],
  templateUrl: './work-queue.html',
  styleUrl: './work-queue.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkQueue {
  private readonly staffClaims = inject(StaffClaims);
  private readonly router = inject(Router);
  private readonly toast = inject(Toast);

  protected readonly views: readonly { view: WorkQueueView; label: string }[] = [
    { view: 'Unassigned', label: 'Unassigned' },
    { view: 'Mine', label: 'My claims' },
  ];
  protected readonly view = signal<WorkQueueView>('Unassigned');

  /** Search and page are applied by the server: only one page of the queue is downloaded. */
  protected readonly query = createListQuery();
  protected readonly pageSizes = PAGE_SIZES;

  protected readonly queue = httpResource<WorkQueuePage>(() => ({
    url: '/api/work-queue',
    params: { view: this.view(), sort: 'Sla', ...this.query.params() },
  }));
  constructor() {
    // Any change in my market can move a claim in or out of either tab.
    reloadOnClaimUpdates(this.queue);
  }

  private readonly result = keepWhileLoading(this.queue);
  protected readonly rows = computed(() => this.result()?.items ?? []);
  protected readonly total = computed(() => this.result()?.totalCount ?? 0);
  /** Whole queue (from the server), not just this page or this search. */
  protected readonly overdue = computed(() => this.result()?.overdueCount ?? 0);
  protected readonly emptyMessage = computed(() => {
    if (this.query.search()) return `No claims in this tab match “${this.query.search()}”.`;
    return this.view() === 'Unassigned'
      ? 'No unassigned claims in your market.'
      : 'You have no open claims.';
  });

  protected readonly pickingUp = signal<string | null>(null);
  protected readonly waitingOn = waitingOnLabel;

  protected readonly columns = computed(() => [
    'claimNumber',
    'type',
    'status',
    'age',
    'sla',
    'waitingOn',
    'outstanding',
    ...(this.view() === 'Unassigned' ? ['actions'] : []),
  ]);

  protected selectTab(index: number): void {
    this.view.set(this.views[index].view);
    this.query.resetPage(); // keep the search, start the other tab at page 1
  }

  protected open(row: WorkQueueItem): void {
    void this.router.navigate(['/staff/claims', row.id]);
  }

  protected async pickUp(row: WorkQueueItem, event: Event): Promise<void> {
    event.stopPropagation(); // the row itself opens the claim
    this.pickingUp.set(row.id);
    try {
      await this.staffClaims.pickUp(row.id);
      this.toast.success(`${row.claimNumber} is now yours.`);
      await this.router.navigate(['/staff/claims', row.id]);
    } catch (e) {
      const error = AppError.from(e);
      // 409: another officer got there first (status changed or RowVersion conflict). That's a
      // warning (nothing broke, the queue is refreshed); anything else is an error.
      if (error.status === 409)
        this.toast.warning(`${row.claimNumber} was already picked up by another officer.`);
      else this.toast.error(error.userMessage);
      this.queue.reload();
    } finally {
      this.pickingUp.set(null);
    }
  }
}
