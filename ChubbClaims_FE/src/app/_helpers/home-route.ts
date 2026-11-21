import type { UserRole } from '../_models/api-types';

/** Where each role lands after login (FRONTEND.md §3). */
export function homeUrlFor(role: UserRole | null): string {
  switch (role) {
    case 'Claimant':
      return '/claimant/claims';
    case 'ClaimsOfficer':
      return '/staff/queue';
    case 'ClaimsManager':
      return '/staff/team';
    default:
      return '/login';
  }
}

/** Human label for a role, e.g. in the toolbar. */
export function roleLabel(role: UserRole): string {
  switch (role) {
    case 'Claimant':
      return 'Claimant';
    case 'ClaimsOfficer':
      return 'Claims officer';
    case 'ClaimsManager':
      return 'Claims manager';
  }
}
