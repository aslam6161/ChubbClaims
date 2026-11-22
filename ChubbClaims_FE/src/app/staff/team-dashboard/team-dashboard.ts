import { httpResource } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  signal,
} from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { valueOr } from '../../_helpers/resource';
import { roleLabel } from '../../_helpers/home-route';
import { timeAgo } from '../../_helpers/time-ago';
import type { ExposureReport, TeamWorkload } from '../../_models/api-types';
import { AuthStore } from '../../_services/auth-store';
import { reloadOnClaimUpdates, reloadOnExposureUpdates } from '../../_services/realtime';
import { MoneyPipe } from '../../_shared/money.pipe';
import { PageState } from '../../_shared/page-state/page-state';

/**
 * Team workload for any staff member (their market), and for managers the outstanding liability
 * exposure across all markets. Exposure comes from the Kafka-fed read model, so it shows how fresh
 * each figure is; amounts are per currency and never added across currencies.
 */
@Component({
  selector: 'app-team-dashboard',
  imports: [MatCardModule, MatIconModule, MatTableModule, MoneyPipe, PageState, RouterLink],
  templateUrl: './team-dashboard.html',
  styleUrl: './team-dashboard.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamDashboard {
  private readonly auth = inject(AuthStore);

  protected readonly isManager = computed(() => this.auth.role() === 'ClaimsManager');

  protected readonly workload = httpResource<TeamWorkload>(() => '/api/team/workload');
  protected readonly team = computed(() => valueOr(this.workload, undefined));

  /** Only managers request exposure (the API would answer 403 to officers anyway). */
  protected readonly exposure = httpResource<ExposureReport>(() =>
    this.isManager() ? '/api/reports/exposure' : undefined,
  );
  protected readonly report = computed(() => valueOr(this.exposure, undefined));

  /** Ticks so "updated X ago" stays current without reloading anything. */
  protected readonly now = signal(new Date());

  protected readonly workloadColumns = [
    'name',
    'open',
    'underReview',
    'awaitingClaimant',
    'approved',
    'overdue',
    'oldest',
    'decisions',
    'avgDays',
  ];
  protected readonly exposureColumns = ['market', 'claimType', 'open', 'outstanding', 'updated'];
  protected readonly roleLabel = roleLabel;

  constructor() {
    reloadOnClaimUpdates(this.workload);
    reloadOnExposureUpdates(this.exposure);
    const tick = setInterval(() => this.now.set(new Date()), 10_000);
    inject(DestroyRef).onDestroy(() => clearInterval(tick));
  }

  protected ago(iso: string): string {
    return timeAgo(iso, this.now());
  }
}
