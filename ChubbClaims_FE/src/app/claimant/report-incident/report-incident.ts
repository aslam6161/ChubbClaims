import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatStepperModule } from '@angular/material/stepper';
import { Router, RouterLink } from '@angular/router';
import type { Policy } from '../../_models/api-types';
import { valueOr } from '../../_helpers/resource';
import { AppError } from '../../_models/app-error';
import { MoneyPipe } from '../../_shared/money.pipe';
import { PageState } from '../../_shared/page-state/page-state';
import { ClaimantClaims } from '../claimant-claims';
import {
  applyClaimType,
  applyServerErrors,
  createFnolForm,
  DAMAGE_TYPES,
  localIsoDate,
  toSubmitRequest,
} from './fnol-form';

/** Report an incident: policy → what happened → vehicle or property → review and submit. */
@Component({
  selector: 'app-report-incident',
  imports: [
    DatePipe,
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatRadioModule,
    MatSelectModule,
    MatStepperModule,
    MoneyPipe,
    PageState,
    ReactiveFormsModule,
    RouterLink,
  ],
  templateUrl: './report-incident.html',
  styleUrl: './report-incident.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportIncident {
  private readonly claims = inject(ClaimantClaims);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly policies = httpResource<Policy[]>(() => '/api/policies/mine');
  protected readonly policyList = computed(() => valueOr(this.policies, [] as Policy[]));

  // Declared BEFORE the form: the form runs its validators as soon as it is built, and the
  // date validator reads the chosen policy (class fields initialise top to bottom).
  protected readonly selectedPolicy = signal<Policy | undefined>(undefined);
  protected readonly form = createFnolForm(() => this.selectedPolicy());
  protected readonly today = localIsoDate();
  protected readonly damageTypes = DAMAGE_TYPES;

  protected readonly submitting = signal(false);
  protected readonly submitError = signal<AppError | null>(null);
  protected readonly unmatchedErrors = signal<string[]>([]);

  protected choosePolicy(policy: Policy): void {
    this.selectedPolicy.set(policy);
    this.form.controls.policyNumber.setValue(policy.policyNumber);
    applyClaimType(this.form, policy.productType);
  }

  protected async submit(): Promise<void> {
    const policy = this.selectedPolicy();
    if (!policy || this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.submitting.set(true);
    this.submitError.set(null);
    this.unmatchedErrors.set([]);
    try {
      const created = await this.claims.submit(toSubmitRequest(this.form, policy));
      this.snackBar.open(`Claim ${created.claimNumber} submitted.`, 'OK', { duration: 5000 });
      await this.router.navigate(['/claimant/claims', created.id]);
    } catch (e) {
      const error = AppError.from(e);
      this.submitError.set(error); // 422: a business rule, e.g. policy not in force on that date
      this.unmatchedErrors.set(applyServerErrors(this.form, error.fieldErrors)); // 400: per field
    } finally {
      this.submitting.set(false);
    }
  }
}
