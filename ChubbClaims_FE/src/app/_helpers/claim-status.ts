import type { ClaimStatus, UserRole, WaitingOn } from '../_models/api-types';

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

// ---------------------------------------------------------------- Staff actions

export type ClaimAction =
  'pickUp' | 'adjustReserve' | 'requestInformation' | 'approve' | 'settle' | 'reject' | 'addNote';

/** Which status each action may start from: the same table as the backend's ClaimTransitions. */
const ACTION_FROM: Record<Exclude<ClaimAction, 'addNote'>, readonly ClaimStatus[]> = {
  pickUp: ['Submitted'],
  adjustReserve: ['UnderReview', 'AwaitingInformation', 'Approved'],
  requestInformation: ['UnderReview'],
  approve: ['UnderReview'],
  settle: ['Approved'],
  reject: ['UnderReview'],
};

/**
 * The actions to offer for a claim, mirroring the domain rules (FRONTEND.md §6):
 * - claimants get none here (they answer questions on their own screen);
 * - any staff member in the market can pick up an unassigned claim and add notes;
 * - everything else needs the claim to be assigned to the caller and in the right status.
 * This only decides which buttons to show: the API re-checks every action.
 */
export function allowedActions(
  status: ClaimStatus,
  role: UserRole,
  isAssignedToMe: boolean,
): ClaimAction[] {
  if (role === 'Claimant') return [];

  const actions: ClaimAction[] = [];
  if (ACTION_FROM.pickUp.includes(status)) actions.push('pickUp');
  if (isAssignedToMe) {
    for (const action of [
      'adjustReserve',
      'requestInformation',
      'approve',
      'settle',
      'reject',
    ] as const) {
      if (ACTION_FROM[action].includes(status)) actions.push(action);
    }
  }
  actions.push('addNote');
  return actions;
}

const WAITING_ON: Record<WaitingOn, string> = {
  Officer: 'Claims team',
  Claimant: 'Claimant',
  Nobody: 'Closed',
};

export function waitingOnLabel(waitingOn: WaitingOn): string {
  return WAITING_ON[waitingOn];
}
