import type { ClaimStatus, UserRole } from '../_models/api-types';
import { allowedActions, ClaimAction } from './claim-status';

const STATUSES: ClaimStatus[] = [
  'Submitted',
  'UnderReview',
  'AwaitingInformation',
  'Approved',
  'Settled',
  'Rejected',
];

/**
 * What the assigned officer may do in each status. Mirrors the backend's ClaimTransitions table;
 * if the two ever disagree, the UI offers a button the API rejects (409) or hides a valid one.
 */
const ASSIGNED: Record<ClaimStatus, ClaimAction[]> = {
  Submitted: ['pickUp', 'addNote'], // never assigned in practice; pick-up is the way in
  UnderReview: ['adjustReserve', 'requestInformation', 'approve', 'reject', 'addNote'],
  AwaitingInformation: ['adjustReserve', 'addNote'],
  Approved: ['adjustReserve', 'settle', 'addNote'],
  Settled: ['addNote'],
  Rejected: ['addNote'],
};

/** Staff who are not the assignee: pick up unassigned claims, add notes, nothing else. */
const NOT_ASSIGNED: Record<ClaimStatus, ClaimAction[]> = {
  Submitted: ['pickUp', 'addNote'],
  UnderReview: ['addNote'],
  AwaitingInformation: ['addNote'],
  Approved: ['addNote'],
  Settled: ['addNote'],
  Rejected: ['addNote'],
};

describe('allowedActions (every status × role × assigned)', () => {
  for (const role of ['ClaimsOfficer', 'ClaimsManager'] as UserRole[]) {
    for (const status of STATUSES) {
      it(`${role}, ${status}, assigned to me`, () => {
        expect(allowedActions(status, role, true)).toEqual(ASSIGNED[status]);
      });
      it(`${role}, ${status}, not assigned to me`, () => {
        expect(allowedActions(status, role, false)).toEqual(NOT_ASSIGNED[status]);
      });
    }
  }

  it('claimants get no staff actions in any status', () => {
    for (const status of STATUSES) {
      expect(allowedActions(status, 'Claimant', true)).toEqual([]);
      expect(allowedActions(status, 'Claimant', false)).toEqual([]);
    }
  });
});
