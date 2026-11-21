import { Routes } from '@angular/router';

// Role areas (claimant, staff) and login are added in phase 2.
export const routes: Routes = [
  {
    path: '**',
    loadComponent: () => import('./errors/not-found/not-found').then((m) => m.NotFound),
  },
];
