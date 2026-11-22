import type { Policy } from '../../_models/api-types';
import {
  applyClaimType,
  applyServerErrors,
  createFnolForm,
  fromIsoDate,
  localIsoDate,
  toSubmitRequest,
} from './fnol-form';

const motorPolicy: Policy = {
  policyNumber: 'POL-MY-M-0001',
  productType: 'Motor',
  market: 'MY',
  currency: 'MYR',
  effectiveFrom: '2026-01-01',
  effectiveTo: '2026-12-31',
  isActive: true,
  reportableFrom: '2026-01-01', // as the API computes it for "today" 2026-11-21
  reportableTo: '2026-11-21',
};
const propertyPolicy: Policy = {
  ...motorPolicy,
  policyNumber: 'POL-MY-P-0001',
  productType: 'Property',
};

function formFor(policy: Policy, today = '2026-11-21') {
  const form = createFnolForm(
    () => policy,
    () => today,
  );
  form.controls.policyNumber.setValue(policy.policyNumber);
  applyClaimType(form, policy.productType);
  form.controls.details.setValue({
    incidentDate: new Date(2026, 10, 20), // 20 Nov, local time
    incidentLocation: ' Jalan Ampang ',
    description: 'Rear-ended',
    estimatedLossAmount: 7500,
  });
  return form;
}

describe('FNOL form', () => {
  it('converts between picked days and the API date without shifting a day', () => {
    expect(localIsoDate(new Date(2026, 0, 1))).toBe('2026-01-01');
    expect(localIsoDate(fromIsoDate('2026-12-31'))).toBe('2026-12-31');
  });

  it('accepts today and the first day of cover (boundaries are inclusive)', () => {
    const date = formFor(motorPolicy).controls.details.controls.incidentDate;

    date.setValue(new Date(2026, 10, 21)); // "today" in formFor
    expect(date.valid).toBe(true);
    date.setValue(new Date(2026, 0, 1)); // effectiveFrom
    expect(date.valid).toBe(true);
  });

  it('requires vehicle details for a motor policy and ignores property fields', () => {
    const form = formFor(motorPolicy);

    expect(form.valid).toBe(false); // vehicle registration missing
    expect(form.controls.property.disabled).toBe(true);

    form.controls.motor.controls.vehicleRegistration.setValue('WXY 1234');
    expect(form.valid).toBe(true);
  });

  it('switches the required fields when a property policy is chosen instead', () => {
    const form = formFor(motorPolicy);
    applyClaimType(form, 'Property');

    expect(form.controls.motor.disabled).toBe(true);
    expect(form.valid).toBe(false); // address and damage type now required

    form.controls.property.setValue({ propertyAddress: 'Bangsar', damageType: 'Water' });
    expect(form.valid).toBe(true);
  });

  it('rejects a future incident date', () => {
    const date = formFor(motorPolicy).controls.details.controls.incidentDate;

    date.setValue(new Date(2026, 10, 22));

    expect(date.hasError('future')).toBe(true);
  });

  it('rejects a date outside the policy cover', () => {
    const date = formFor({ ...motorPolicy, effectiveFrom: '2026-06-01' }).controls.details.controls
      .incidentDate;

    date.setValue(new Date(2026, 4, 31));

    expect(date.hasError('notCovered')).toBe(true);
  });

  it('rejects a covered date that is past the reporting deadline (earliest day from the API)', () => {
    const expired: Policy = {
      ...motorPolicy,
      effectiveFrom: '2025-01-01',
      effectiveTo: '2025-12-31',
      reportableFrom: '2025-11-21', // reporting window cuts into the cover
      reportableTo: '2025-12-31',
    };
    const date = formFor(expired).controls.details.controls.incidentDate;

    date.setValue(new Date(2025, 5, 1)); // covered, but too long ago to report
    expect(date.getError('tooLate')).toEqual({ from: '2025-11-21' });

    date.setValue(new Date(2025, 10, 21)); // the earliest reportable day itself
    expect(date.valid).toBe(true);
  });

  it('rejects a zero estimate but allows none', () => {
    const amount = formFor(motorPolicy).controls.details.controls.estimatedLossAmount;

    amount.setValue(0);
    expect(amount.hasError('min')).toBe(true);
    amount.setValue(null);
    expect(amount.valid).toBe(true);
  });

  it('builds the request with only the details for the policy type, trimmed', () => {
    const form = formFor(motorPolicy);
    form.controls.motor.setValue({
      vehicleRegistration: ' WXY 1234 ',
      thirdPartyInvolved: true,
      policeReportNumber: '',
    });

    expect(toSubmitRequest(form, motorPolicy)).toEqual({
      policyNumber: 'POL-MY-M-0001',
      type: 'Motor',
      incidentDate: '2026-11-20',
      incidentLocation: 'Jalan Ampang',
      description: 'Rear-ended',
      estimatedLossAmount: 7500,
      motor: {
        vehicleRegistration: 'WXY 1234',
        thirdPartyInvolved: true,
        policeReportNumber: null,
      },
      property: null,
    });
    expect(toSubmitRequest(formFor(propertyPolicy), propertyPolicy).motor).toBeNull();
  });

  it('puts server field errors on the matching controls and returns the rest', () => {
    const form = formFor(motorPolicy);

    const unmatched = applyServerErrors(form, {
      incidentDate: ['The incident date is required.'],
      'motor.vehicleRegistration': ['Too long.'],
      type: ['Unknown type.'],
    });

    expect(form.controls.details.controls.incidentDate.getError('server')).toBe(
      'The incident date is required.',
    );
    expect(form.controls.motor.controls.vehicleRegistration.getError('server')).toBe('Too long.');
    expect(unmatched).toEqual(['Unknown type.']);
  });
});
