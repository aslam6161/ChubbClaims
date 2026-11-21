import { Routes } from '@angular/router';
import { AreaLayout, NavLink } from '../_layout/area-layout/area-layout';

const nav: NavLink[] = [
  { label: 'Work queue', path: '/staff/queue', icon: 'inbox' },
  { label: 'Team', path: '/staff/team', icon: 'groups' },
];

/** Lazy-loaded; only reachable through roleGuard(['ClaimsOfficer', 'ClaimsManager']). */
export const staffRoutes: Routes = [
  {
    path: '',
    component: AreaLayout,
    data: { nav },
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'queue' },
      {
        path: 'queue',
        loadComponent: () => import('./work-queue/work-queue').then((m) => m.WorkQueue),
      },
      {
        path: 'team',
        loadComponent: () => import('./team-dashboard/team-dashboard').then((m) => m.TeamDashboard),
      },
    ],
  },
];
