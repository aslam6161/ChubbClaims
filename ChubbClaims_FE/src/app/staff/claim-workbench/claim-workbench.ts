import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import {
  FormControl,
  FormGroup,
  FormGroupDirective,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { allowedActions, ClaimAction, statusLabel } from '../../_helpers/claim-status';
import { valueOr } from '../../_helpers/resource';
import type { ClaimDetail, UserRole } from '../../_models/api-types';
import { AppError } from '../../_models/app-error';
import { AuthStore } from '../../_services/auth-store';
import { confirm } from '../../_shared/confirm-dialog/confirm-dialog';
import { MoneyPipe } from '../../_shared/money.pipe';
import { PageState } from '../../_shared/page-state/page-state';
import { StatusBadge } from '../../_shared/status-badge/status-badge';
import { StaffClaims } from '../staff-claims';

type FormAction = Exclude<ClaimAction, 'pickUp' | 'settle' | 'addNote'>;

/**
 * Everything an officer needs to work one claim. The action buttons come from allowedActions()
 * (same rules as the API); every action waits for the server, then reloads the claim.
 */
@Component({
  selector: 'app-claim-workbench',
  imports: [
    DatePipe,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MoneyPipe,
    PageState,
    ReactiveFormsModule,
    RouterLink,
    StatusBadge,
  ],
  templateUrl: './claim-workbench.html',
  styleUrl: './claim-workbench.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClaimWorkbench {
  private readonly staffClaims = inject(StaffClaims);
  private readonly auth = inject(AuthStore);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  /** Route param :id. */
  readonly id = input.required<string>();

  protected readonly claim = httpResource<ClaimDetail>(() => `/api/claims/${this.id()}`);
  protected readonly detail = computed(() => valueOr(this.claim, undefined));

  protected readonly assignedToMe = computed(
    () =>
      !!this.detail()?.assignedOfficerId &&
      this.detail()?.assignedOfficerId === this.auth.user()?.id,
  );
  protected readonly actions = computed(() => {
    const c = this.detail();
    const role = this.auth.role();
    return c && role ? allowedActions(c.status, role, this.assignedToMe()) : [];
  });
  protected readonly can = (action: ClaimAction) => this.actions().includes(action);
  protected readonly isOverdue = computed(() => {
    const c = this.detail();
    return !!c && c.decisionAt === null && new Date(c.slaDueAt) < new Date();
  });
  protected readonly timeline = computed(() => [...(this.detail()?.statusHistory ?? [])].reverse());
  protected readonly reserveHistory = computed(() =>
    [...(this.detail()?.reserveHistory ?? [])].reverse(),
  );

  /** Which action form is open (one at a time). */
  protected readonly open = signal<FormAction | null>(null);
  protected readonly busy = signal(false);
  protected readonly actionError = signal<AppError | null>(null);

  protected readonly reserveForm = new FormGroup({
    amount: new FormControl<number | null>(null, [Validators.required, Validators.min(0)]),
    reason: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(2000)],
    }),
  });
  protected readonly infoForm = new FormGroup({
    question: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(2000)],
    }),
  });
  protected readonly approveForm = new FormGroup({
    settlementAmount: new FormControl<number | null>(null, [
      Validators.required,
      Validators.min(0.01),
    ]),
    note: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(4000)] }),
  });
  protected readonly rejectForm = new FormGroup({
    reason: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(2000)],
    }),
  });
  protected readonly noteForm = new FormGroup({
    text: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(4000)],
    }),
  });

  protected readonly label = statusLabel;

  protected who(role: UserRole): string {
    return role === 'Claimant' ? 'Claimant' : role === 'ClaimsManager' ? 'Manager' : 'Officer';
  }

  protected toggle(action: FormAction, claim: ClaimDetail): void {
    this.actionError.set(null);
    if (this.open() === action) {
      this.open.set(null);
      return;
    }
    // Sensible starting values: the current reserve, and the reserve as the settlement offer.
    if (action === 'adjustReserve')
      this.reserveForm.reset({ amount: claim.reserveAmount, reason: '' });
    if (action === 'approve')
      this.approveForm.reset({ settlementAmount: claim.reserveAmount, note: '' });
    this.open.set(action);
  }

  protected pickUp(c: ClaimDetail): Promise<void> {
    return this.run('You picked up this claim.', () => this.staffClaims.pickUp(c.id));
  }

  protected adjustReserve(c: ClaimDetail): Promise<void> {
    const { amount, reason } = this.reserveForm.getRawValue();
    return this.run(
      'Reserve updated.',
      () => this.staffClaims.adjustReserve(c.id, amount!, reason.trim()),
      this.reserveForm,
    );
  }

  protected requestInformation(c: ClaimDetail): Promise<void> {
    const { question } = this.infoForm.getRawValue();
    return this.run(
      'Question sent to the claimant.',
      () => this.staffClaims.requestInformation(c.id, question.trim()),
      this.infoForm,
    );
  }

  protected async approve(c: ClaimDetail): Promise<void> {
    const { settlementAmount, note } = this.approveForm.getRawValue();
    if (this.approveForm.invalid) return this.touch(this.approveForm);
    const amount = new MoneyPipe().transform(settlementAmount, c.currency);
    if (
      !(await confirm(this.dialog, {
        title: `Approve ${c.claimNumber}?`,
        message: `Settle for ${amount}. The reserve becomes this amount and the claimant is told.`,
        confirmLabel: 'Approve',
      }))
    )
      return;
    return this.run(
      'Claim approved.',
      () => this.staffClaims.approve(c.id, settlementAmount!, note.trim() || null),
      this.approveForm,
    );
  }

  protected async settle(c: ClaimDetail): Promise<void> {
    const amount = new MoneyPipe().transform(c.settlementAmount, c.currency);
    if (
      !(await confirm(this.dialog, {
        title: `Record payment for ${c.claimNumber}?`,
        message: `Mark ${amount} as paid and close the claim.`,
        confirmLabel: 'Record payment',
      }))
    )
      return;
    return this.run('Payment recorded. Claim closed.', () => this.staffClaims.settle(c.id));
  }

  protected async reject(c: ClaimDetail): Promise<void> {
    const { reason } = this.rejectForm.getRawValue();
    if (this.rejectForm.invalid) return this.touch(this.rejectForm);
    if (
      !(await confirm(this.dialog, {
        title: `Reject ${c.claimNumber}?`,
        message: 'The claim is closed and the claimant sees your reason. This cannot be undone.',
        confirmLabel: 'Reject claim',
        danger: true,
      }))
    )
      return;
    return this.run(
      'Claim rejected.',
      () => this.staffClaims.reject(c.id, reason.trim()),
      this.rejectForm,
    );
  }

  /**
   * The note form stays on screen after saving, so it is reset through its FormGroupDirective:
   * FormGroup.reset() would keep the form "submitted" and Material would then show the now-empty
   * required field in red.
   */
  protected addNote(c: ClaimDetail, directive: FormGroupDirective): Promise<void> {
    const { text } = this.noteForm.getRawValue();
    return this.run(
      'Note added.',
      () => this.staffClaims.addNote(c.id, text.trim()),
      this.noteForm,
      false,
      directive,
    );
  }

  /**
   * Runs one command: validate → call → on success close the form and reload; on 409 (someone
   * else changed the claim, or it moved on) explain and reload; other errors stay on screen.
   */
  private async run(
    done: string,
    call: () => Promise<void>,
    form?: FormGroup,
    closePanel = true,
    directive?: FormGroupDirective,
  ): Promise<void> {
    if (form?.invalid) return this.touch(form);
    this.busy.set(true);
    this.actionError.set(null);
    try {
      await call();
      if (directive) directive.resetForm();
      else form?.reset();
      if (closePanel) this.open.set(null);
      this.snackBar.open(done, 'OK', { duration: 3000 });
      this.claim.reload();
    } catch (e) {
      const error = AppError.from(e);
      if (error.status === 409) {
        this.snackBar.open(`${error.userMessage} The claim has been reloaded.`, 'OK', {
          duration: 6000,
        });
        this.open.set(null);
        this.claim.reload();
      } else {
        this.actionError.set(error);
      }
    } finally {
      this.busy.set(false);
    }
  }

  private touch(form: FormGroup): void {
    form.markAllAsTouched();
  }
}
