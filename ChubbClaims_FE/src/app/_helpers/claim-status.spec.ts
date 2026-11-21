import type { ClaimStatus } from '../_models/api-types';
import { MoneyPipe } from '../_shared/money.pipe';
import {
  claimantNextStep,
  isClosed,
  needsClaimantAction,
  statusLabel,
  statusTone,
} from './claim-status';

const ALL: ClaimStatus[] = [
  'Submitted',
  'UnderReview',
  'AwaitingInformation',
  'Approved',
  'Settled',
  'Rejected',
];

describe('claim status helpers', () => {
  it('every status has a label, a tone and a "what happens next" line', () => {
    for (const status of ALL) {
      expect(statusLabel(status)).toBeTruthy();
      expect(statusTone(status)).toBeTruthy();
      expect(claimantNextStep(status)).toBeTruthy();
    }
  });

  it('pending states say who is waiting for whom', () => {
    expect(claimantNextStep('Submitted')).toContain('Waiting for a claims officer');
    expect(claimantNextStep('AwaitingInformation')).toBe('We need more information from you.');
    expect(claimantNextStep('Approved')).toContain('payment is being processed');
  });

  it('only "awaiting information" needs the claimant to act', () => {
    expect(ALL.filter(needsClaimantAction)).toEqual(['AwaitingInformation']);
  });

  it('settled and rejected are closed', () => {
    expect(ALL.filter(isClosed)).toEqual(['Settled', 'Rejected']);
  });
});

describe('MoneyPipe', () => {
  const pipe = new MoneyPipe();

  it('formats in the amount’s own currency', () => {
    // Intl separates code and amount with a non-breaking space
    expect(pipe.transform(5800, 'MYR')).toBe('MYR 5,800.00');
    expect(pipe.transform(null, 'MYR')).toBe('—');
    expect(pipe.transform(20000000, 'IDR')).toBe('IDR 20,000,000.00'); // same in Node and Chromium
  });
});
