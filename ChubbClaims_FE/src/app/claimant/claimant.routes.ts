import { Routes } from '@angular/router';
import { AreaLayout, NavLink } from '../_layout/area-layout/area-layout';

const nav: NavLink[] = [{ label: 'My claims', path: '/claimant/claims', icon: 'description' }];

/** Lazy-loaded; only reachable through roleGuard(['Claimant']). */
export const claimantRoutes: Routes = [
  {
    path: '',
    component: AreaLayout,
    data: { nav },
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'claims' },
      {
        path: 'claims',
        loadComponent: () => import('./my-claims/my-claims').then((m) => m.MyClaims),
      },
    ],
  },
];
