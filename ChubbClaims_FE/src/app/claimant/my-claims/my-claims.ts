import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { Router, RouterLink } from '@angular/router';
import { claimantNextStep, needsClaimantAction } from '../../_helpers/claim-status';
import { valueOr } from '../../_helpers/resource';
import type { ClaimSummary } from '../../_models/api-types';
import { MoneyPipe } from '../../_shared/money.pipe';
import { PageState } from '../../_shared/page-state/page-state';
import { StatusBadge } from '../../_shared/status-badge/status-badge';

@Component({
  selector: 'app-my-claims',
  imports: [
    DatePipe,
    MatButtonModule,
    MatIconModule,
    MatTableModule,
    MoneyPipe,
    PageState,
    RouterLink,
    StatusBadge,
  ],
  templateUrl: './my-claims.html',
  styleUrl: './my-claims.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MyClaims {
  private readonly router = inject(Router);

  protected readonly claims = httpResource<ClaimSummary[]>(() => '/api/claims/mine');
  protected readonly rows = computed(() => valueOr(this.claims, [] as ClaimSummary[]));
  protected readonly actionNeeded = computed(
    () => this.rows().filter((c) => needsClaimantAction(c.status)).length,
  );

  protected readonly columns = ['claimNumber', 'type', 'status', 'nextStep', 'reportedAt'];
  protected readonly nextStep = claimantNextStep;
  protected readonly needsAction = needsClaimantAction;

  protected open(claim: ClaimSummary): void {
    void this.router.navigate(['/claimant/claims', claim.id]);
  }
}
