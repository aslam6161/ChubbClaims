import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Toast } from '../../_services/toast';
import { RouterLink } from '@angular/router';
import {
  claimantNextStep,
  isClosed,
  needsClaimantAction,
  statusLabel,
} from '../../_helpers/claim-status';
import { valueOr } from '../../_helpers/resource';
import { reloadOnClaimUpdates } from '../../_services/realtime';
import type { ClaimDetail, UserRole } from '../../_models/api-types';
import { AppError } from '../../_models/app-error';
import { ClaimDocumentsPanel } from '../../_shared/claim-documents/claim-documents';
import { MoneyPipe } from '../../_shared/money.pipe';
import { PageState } from '../../_shared/page-state/page-state';
import { StatusBadge } from '../../_shared/status-badge/status-badge';
import { ClaimantClaims } from '../claimant-claims';

/** One claim as the claimant sees it. Internal fields (reserve, notes) are not sent to claimants. */
@Component({
  selector: 'app-claim-detail',
  imports: [
    ClaimDocumentsPanel,
    DatePipe,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MoneyPipe,
    PageState,
    ReactiveFormsModule,
    RouterLink,
    StatusBadge,
  ],
  templateUrl: './claim-detail.html',
  styleUrl: './claim-detail.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClaimDetailPage {
  private readonly claims = inject(ClaimantClaims);
  private readonly toast = inject(Toast);

  /** Route param :id (component input binding). */
  readonly id = input.required<string>();

  protected readonly claim = httpResource<ClaimDetail>(() => `/api/claims/${this.id()}`);
  protected readonly detail = computed(() => valueOr(this.claim, undefined));

  constructor() {
    reloadOnClaimUpdates(this.claim, (m) => m.claimId === this.id());
  }
  protected readonly answeredRequests = computed(() =>
    (this.detail()?.informationRequests ?? []).filter((r) => !r.isOpen),
  );
  protected readonly timeline = computed(() => [...(this.detail()?.statusHistory ?? [])].reverse());

  // A FormGroup bound with [formGroup], so (ngSubmit) exists. With a lone FormControl the
  // <form> would do a native browser submit (page reload) and the answer would be lost.
  protected readonly answerForm = new FormGroup({
    answer: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(4000)],
    }),
  });
  protected readonly answer = this.answerForm.controls.answer;
  protected readonly sending = signal(false);
  protected readonly answerError = signal<AppError | null>(null);

  protected readonly nextStep = claimantNextStep;
  protected readonly needsAction = needsClaimantAction;
  protected readonly closed = isClosed;
  protected readonly label = statusLabel;

  protected who(role: UserRole): string {
    return role === 'Claimant' ? 'You' : 'Claims team';
  }

  protected async sendAnswer(claim: ClaimDetail): Promise<void> {
    const request = claim.openInformationRequest;
    if (!request || this.answerForm.invalid) {
      this.answerForm.markAllAsTouched();
      return;
    }
    this.sending.set(true);
    this.answerError.set(null);
    try {
      await this.claims.answerInformationRequest(claim.id, request.id, this.answer.value.trim());
      this.answerForm.reset();
      this.toast.success('Thanks, your answer was sent to the claims team.');
      this.claim.reload(); // the server decides the new status; show what it says
    } catch (e) {
      this.answerError.set(AppError.from(e));
    } finally {
      this.sending.set(false);
    }
  }
}
