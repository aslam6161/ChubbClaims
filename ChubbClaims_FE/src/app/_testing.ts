import type { DevLoginResponse, User, UserRole } from './_models/api-types';

/** Test data shared by specs (only spec files import this, so it is not in the app bundle). */
export function loginResponse(role: UserRole, minutesValid = 60): DevLoginResponse {
  const user: User = {
    id: `id-${role}`,
    name: `${role} user`,
    email: `${role.toLowerCase()}@demo.local`,
    role,
    market: role === 'Claimant' ? null : 'MY',
  };
  return {
    accessToken: `token-${role}`,
    expiresAt: new Date(Date.now() + minutesValid * 60_000).toISOString(),
    user,
  };
}
