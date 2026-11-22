import {
  AbstractControl,
  FormControl,
  FormGroup,
  ValidationErrors,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import type { ClaimType, DamageType, Policy, SubmitClaimRequest } from '../../_models/api-types';

/**
 * The first-notice-of-loss form: structure, validation and mapping, kept out of the component so
 * the rules are unit tested directly. Client rules mirror the server's (no future date, date within
 * the policy period, type-specific details), but the server stays the authority: its 400/422
 * errors are still shown (see applyServerErrors).
 */
export type FnolForm = FormGroup<{
  policyNumber: FormControl<string>;
  details: FormGroup<{
    /** From the Material datepicker: local midnight of the chosen day, or null. */
    incidentDate: FormControl<Date | null>;
    incidentLocation: FormControl<string>;
    description: FormControl<string>;
    estimatedLossAmount: FormControl<number | null>;
  }>;
  motor: FormGroup<{
    vehicleRegistration: FormControl<string>;
    thirdPartyInvolved: FormControl<boolean>;
    policeReportNumber: FormControl<string>;
  }>;
  property: FormGroup<{
    propertyAddress: FormControl<string>;
    damageType: FormControl<DamageType | null>;
  }>;
}>;

export const DAMAGE_TYPES: readonly DamageType[] = ['Fire', 'Water', 'Storm', 'Theft', 'Other'];

/**
 * yyyy-MM-dd of a day in the user's local time zone. The API takes the incident date in this form,
 * and the rules below compare these strings (ISO dates sort correctly as text).
 */
export function localIsoDate(date: Date = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** Local midnight of a yyyy-MM-dd day (for the datepicker's min/max). */
export function fromIsoDate(iso: string): Date {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(y, m - 1, d);
}

export function notInFuture(today: () => string): ValidatorFn {
  return (control: AbstractControl<Date | null>): ValidationErrors | null =>
    control.value && localIsoDate(control.value) > today() ? { future: true } : null;
}

/**
 * The incident must be inside the selected policy's cover, and recent enough to still be reported
 * (the API gives the earliest reportable day, so the reporting window isn't hard-coded here).
 */
export function withinPolicy(policy: () => Policy | undefined): ValidatorFn {
  return (control: AbstractControl<Date | null>): ValidationErrors | null => {
    const p = policy();
    if (!p || !control.value) return null;
    const day = localIsoDate(control.value);
    if (day < p.effectiveFrom || day > p.effectiveTo)
      return { notCovered: { from: p.effectiveFrom, to: p.effectiveTo } };
    if (p.reportableFrom && day < p.reportableFrom) return { tooLate: { from: p.reportableFrom } };
    return null;
  };
}

export function createFnolForm(
  policy: () => Policy | undefined,
  today: () => string = () => localIsoDate(),
): FnolForm {
  const text = (max: number, required = true) =>
    new FormControl('', {
      nonNullable: true,
      validators: required
        ? [Validators.required, Validators.maxLength(max)]
        : [Validators.maxLength(max)],
    });

  const form: FnolForm = new FormGroup({
    policyNumber: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    details: new FormGroup({
      incidentDate: new FormControl<Date | null>(null, {
        validators: [Validators.required, notInFuture(today), withinPolicy(policy)],
      }),
      incidentLocation: text(500),
      description: text(4000),
      estimatedLossAmount: new FormControl<number | null>(null, {
        validators: [Validators.min(0.01)],
      }),
    }),
    motor: new FormGroup({
      vehicleRegistration: text(20),
      thirdPartyInvolved: new FormControl(false, { nonNullable: true }),
      policeReportNumber: text(50, false),
    }),
    property: new FormGroup({
      propertyAddress: text(500),
      damageType: new FormControl<DamageType | null>(null, { validators: [Validators.required] }),
    }),
  });

  // Until a policy is chosen neither detail group applies.
  form.controls.motor.disable();
  form.controls.property.disable();
  return form;
}

/**
 * Only the details group for the policy's type is enabled: a disabled group is excluded from
 * validation and from the value, so its required fields don't block a claim of the other type.
 */
export function applyClaimType(form: FnolForm, type: ClaimType): void {
  const [use, skip] =
    type === 'Motor'
      ? [form.controls.motor, form.controls.property]
      : [form.controls.property, form.controls.motor];
  use.enable();
  skip.disable();
  // The date's policy-period check depends on the chosen policy.
  form.controls.details.controls.incidentDate.updateValueAndValidity();
}

export function toSubmitRequest(form: FnolForm, policy: Policy): SubmitClaimRequest {
  const d = form.controls.details.getRawValue();
  const m = form.controls.motor.getRawValue();
  const p = form.controls.property.getRawValue();
  return {
    policyNumber: policy.policyNumber,
    type: policy.productType,
    incidentDate: d.incidentDate ? localIsoDate(d.incidentDate) : '', // required: set by now
    incidentLocation: d.incidentLocation.trim(),
    description: d.description.trim(),
    estimatedLossAmount: d.estimatedLossAmount,
    motor:
      policy.productType === 'Motor'
        ? {
            vehicleRegistration: m.vehicleRegistration.trim(),
            thirdPartyInvolved: m.thirdPartyInvolved,
            policeReportNumber: m.policeReportNumber.trim() || null,
          }
        : null,
    property:
      policy.productType === 'Property' && p.damageType
        ? { propertyAddress: p.propertyAddress.trim(), damageType: p.damageType }
        : null,
  };
}

/** Request field (as the server names it) → path of the form control that edits it. */
const FIELD_TO_CONTROL: Record<string, string> = {
  policyNumber: 'policyNumber',
  incidentDate: 'details.incidentDate',
  incidentLocation: 'details.incidentLocation',
  description: 'details.description',
  estimatedLossAmount: 'details.estimatedLossAmount',
};

/**
 * Puts the server's 400 field errors on the matching controls (error key `server`, shown under the
 * field). Returns the messages that have no matching control, to show in the summary.
 */
export function applyServerErrors(
  form: FnolForm,
  fieldErrors: Readonly<Record<string, readonly string[]>>,
): string[] {
  const unmatched: string[] = [];
  for (const [field, messages] of Object.entries(fieldErrors)) {
    const control = form.get(FIELD_TO_CONTROL[field] ?? field); // motor.* / property.* already match
    const message = messages.join(' ');
    if (control && control.enabled) {
      control.setErrors({ ...control.errors, server: message });
      control.markAsTouched();
    } else {
      unmatched.push(message);
    }
  }
  return unmatched;
}
