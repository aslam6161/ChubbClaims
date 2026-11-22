import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule } from '@angular/material/paginator';
import { MatTableModule } from '@angular/material/table';
import { Router, RouterLink } from '@angular/router';
import { claimantNextStep, needsClaimantAction } from '../../_helpers/claim-status';
import { createListQuery, PAGE_SIZES } from '../../_helpers/paging';
import { keepWhileLoading } from '../../_helpers/resource';
import { reloadOnClaimUpdates } from '../../_services/realtime';
import type { ClaimSummary, MyClaimsPage } from '../../_models/api-types';
import { MoneyPipe } from '../../_shared/money.pipe';
import { PageState } from '../../_shared/page-state/page-state';
import { SearchBox } from '../../_shared/search-box/search-box';
import { StatusBadge } from '../../_shared/status-badge/status-badge';

@Component({
  selector: 'app-my-claims',
  imports: [
    DatePipe,
    MatButtonModule,
    MatIconModule,
    MatPaginatorModule,
    MatTableModule,
    MoneyPipe,
    PageState,
    RouterLink,
    SearchBox,
    StatusBadge,
  ],
  templateUrl: './my-claims.html',
  styleUrl: './my-claims.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MyClaims {
  private readonly router = inject(Router);

  /** Search and page are applied by the server, so only one page of claims is ever downloaded. */
  protected readonly query = createListQuery();
  protected readonly pageSizes = PAGE_SIZES;

  protected readonly claims = httpResource<MyClaimsPage>(() => ({
    url: '/api/claims/mine',
    params: this.query.params(),
  }));
  constructor() {
    reloadOnClaimUpdates(this.claims);
  }

  private readonly result = keepWhileLoading(this.claims);
  protected readonly rows = computed(() => this.result()?.items ?? []);
  protected readonly total = computed(() => this.result()?.totalCount ?? 0);
  /** Over all the claimant's claims (from the server), so it doesn't change as they search or page. */
  protected readonly actionNeeded = computed(() => this.result()?.awaitingYouCount ?? 0);
  protected readonly firstToAnswer = computed(
    () => this.result()?.oldestAwaitingYouClaimId ?? null,
  );
  protected readonly emptyMessage = computed(() =>
    this.query.search()
      ? `No claims match “${this.query.search()}”.`
      : "You haven't reported any claims yet.",
  );

  protected readonly columns = ['claimNumber', 'type', 'status', 'nextStep', 'reportedAt'];
  protected readonly nextStep = claimantNextStep;
  protected readonly needsAction = needsClaimantAction;

  protected open(claim: ClaimSummary): void {
    void this.router.navigate(['/claimant/claims', claim.id]);
  }
}
