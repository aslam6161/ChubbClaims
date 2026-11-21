import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTabsModule } from '@angular/material/tabs';
import { Router, RouterLink } from '@angular/router';
import { waitingOnLabel } from '../../_helpers/claim-status';
import { valueOr } from '../../_helpers/resource';
import type { WorkQueueItem, WorkQueueView } from '../../_models/api-types';
import { AppError } from '../../_models/app-error';
import { MoneyPipe } from '../../_shared/money.pipe';
import { PageState } from '../../_shared/page-state/page-state';
import { StatusBadge } from '../../_shared/status-badge/status-badge';
import { StaffClaims } from '../staff-claims';

/** The officer's market queue: claims nobody has picked up, and my open claims. Most urgent first. */
@Component({
  selector: 'app-work-queue',
  imports: [
    DatePipe,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatTableModule,
    MatTabsModule,
    MoneyPipe,
    PageState,
    RouterLink,
    StatusBadge,
  ],
  templateUrl: './work-queue.html',
  styleUrl: './work-queue.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkQueue {
  private readonly staffClaims = inject(StaffClaims);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly views: readonly { view: WorkQueueView; label: string }[] = [
    { view: 'Unassigned', label: 'Unassigned' },
    { view: 'Mine', label: 'My claims' },
  ];
  protected readonly view = signal<WorkQueueView>('Unassigned');

  protected readonly queue = httpResource<WorkQueueItem[]>(
    () => `/api/work-queue?view=${this.view()}&sort=Sla`,
  );
  protected readonly rows = computed(() => valueOr(this.queue, [] as WorkQueueItem[]));
  protected readonly overdue = computed(() => this.rows().filter((r) => r.isOverdue).length);

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
  }

  protected open(row: WorkQueueItem): void {
    void this.router.navigate(['/staff/claims', row.id]);
  }

  protected async pickUp(row: WorkQueueItem, event: Event): Promise<void> {
    event.stopPropagation(); // the row itself opens the claim
    this.pickingUp.set(row.id);
    try {
      await this.staffClaims.pickUp(row.id);
      this.snackBar.open(`${row.claimNumber} is now yours.`, 'OK', { duration: 3000 });
      await this.router.navigate(['/staff/claims', row.id]);
    } catch (e) {
      const error = AppError.from(e);
      // 409: another officer got there first (status changed or RowVersion conflict).
      const message =
        error.status === 409
          ? `${row.claimNumber} was already picked up by another officer.`
          : error.userMessage;
      this.snackBar.open(message, 'OK', { duration: 5000 });
      this.queue.reload();
    } finally {
      this.pickingUp.set(null);
    }
  }
}
