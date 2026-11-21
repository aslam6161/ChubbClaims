import { inject } from '@angular/core';
import { Routes } from '@angular/router';
import { roleGuard, signedOutGuard } from './_guards/role.guard';
import { homeUrlFor } from './_helpers/home-route';
import { AuthStore } from './_services/auth-store';

export const routes: Routes = [
  {
    path: 'login',
    canMatch: [signedOutGuard],
    loadComponent: () => import('./authentication/login/login').then((m) => m.Login),
  },
  {
    path: 'claimant',
    canMatch: [roleGuard(['Claimant'])],
    loadChildren: () => import('./claimant/claimant.routes').then((m) => m.claimantRoutes),
  },
  {
    path: 'staff',
    canMatch: [roleGuard(['ClaimsOfficer', 'ClaimsManager'])],
    loadChildren: () => import('./staff/staff.routes').then((m) => m.staffRoutes),
  },
  {
    // Each role has its own home; signed-out users go to the login page.
    path: '',
    pathMatch: 'full',
    redirectTo: () => {
      const auth = inject(AuthStore);
      return homeUrlFor(auth.isAuthenticated() ? auth.role() : null);
    },
  },
  {
    path: '**',
    loadComponent: () => import('./errors/not-found/not-found').then((m) => m.NotFound),
  },
];
