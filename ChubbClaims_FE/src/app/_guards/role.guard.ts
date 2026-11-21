import { inject } from '@angular/core';
import { CanMatchFn, Router } from '@angular/router';
import { homeUrlFor } from '../_helpers/home-route';
import type { UserRole } from '../_models/api-types';
import { AuthStore } from '../_services/auth-store';

/**
 * canMatch, not canActivate: when it returns false the lazy route never matches, so a claimant's
 * browser never even downloads the staff code (and vice versa).
 * This is UX, not security: the API re-checks the role and the resource on every call.
 */
export function roleGuard(allowed: readonly UserRole[]): CanMatchFn {
  return () => {
    const auth = inject(AuthStore);
    const router = inject(Router);

    if (!auth.isAuthenticated()) {
      auth.signOut(); // drop an expired session
      return router.createUrlTree(['/login']);
    }
    const role = auth.role();
    return role !== null && allowed.includes(role)
      ? true
      : router.createUrlTree([homeUrlFor(role)]);
  };
}

/** The login page is for signed-out users; a signed-in user goes to their home. */
export const signedOutGuard: CanMatchFn = () => {
  const auth = inject(AuthStore);
  return auth.isAuthenticated() ? inject(Router).createUrlTree([homeUrlFor(auth.role())]) : true;
};
