import type { ClaimStatus } from '../_models/api-types';

/** Visual tone for a status badge. */
export type StatusTone = 'neutral' | 'progress' | 'attention' | 'success' | 'danger';

const LABELS: Record<ClaimStatus, string> = {
  Submitted: 'Submitted',
  UnderReview: 'Under review',
  AwaitingInformation: 'Awaiting information',
  Approved: 'Approved',
  Settled: 'Settled',
  Rejected: 'Rejected',
};

const TONES: Record<ClaimStatus, StatusTone> = {
  Submitted: 'neutral',
  UnderReview: 'progress',
  AwaitingInformation: 'attention',
  Approved: 'success',
  Settled: 'success',
  Rejected: 'danger',
};

/**
 * "What happens next", in the claimant's words. Decisions arrive later (a claims officer acts on
 * the claim), so every pending state says who is waiting for whom (FRONTEND.md §7).
 */
const CLAIMANT_NEXT_STEP: Record<ClaimStatus, string> = {
  Submitted: 'Received. Waiting for a claims officer to pick it up.',
  UnderReview: 'A claims officer is assessing your claim.',
  AwaitingInformation: 'We need more information from you.',
  Approved: 'Approved: payment is being processed.',
  Settled: 'Paid. This claim is closed.',
  Rejected: 'Not approved. This claim is closed.',
};

export function statusLabel(status: ClaimStatus): string {
  return LABELS[status];
}

export function statusTone(status: ClaimStatus): StatusTone {
  return TONES[status];
}

export function claimantNextStep(status: ClaimStatus): string {
  return CLAIMANT_NEXT_STEP[status];
}

/** The claimant has something to do (answer a question). Used to draw attention in lists. */
export function needsClaimantAction(status: ClaimStatus): boolean {
  return status === 'AwaitingInformation';
}

export function isClosed(status: ClaimStatus): boolean {
  return status === 'Settled' || status === 'Rejected';
}
