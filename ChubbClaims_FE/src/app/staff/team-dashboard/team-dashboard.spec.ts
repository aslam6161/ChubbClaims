import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import type { ExposureReport, TeamWorkload, UserRole } from '../../_models/api-types';
import { AuthStore } from '../../_services/auth-store';
import { loginResponse } from '../../_testing';
import { TeamDashboard } from './team-dashboard';

const workload: TeamWorkload = {
  market: 'MY',
  generatedAt: '2026-11-21T10:00:00Z',
  unassignedOpenClaims: 2,
  officers: [
    {
      officerId: 'o1',
      name: 'Hafiz Ismail',
      role: 'ClaimsOfficer',
      openClaims: 3,
      underReview: 1,
      awaitingClaimant: 1,
      approvedAwaitingPayment: 1,
      overdue: 1,
      oldestOpenAgeDays: 9,
      decisionsLast7Days: 1,
      averageDaysToDecision: 6,
    },
  ],
};

const exposure: ExposureReport = {
  generatedAt: '2026-11-21T10:00:00Z',
  rows: [
    {
      market: 'MY',
      currency: 'MYR',
      claimType: 'Motor',
      openClaims: 3,
      outstandingReserve: 29300,
      lastEventAt: '2026-11-21T09:59:50Z',
    },
    {
      market: 'ID',
      currency: 'IDR',
      claimType: 'Motor',
      openClaims: 1,
      outstandingReserve: 20000000,
      lastEventAt: '2026-11-21T09:00:00Z',
    },
  ],
  totalsByCurrency: [
    {
      currency: 'IDR',
      openClaims: 1,
      outstandingReserve: 20000000,
      lastEventAt: '2026-11-21T09:00:00Z',
    },
    {
      currency: 'MYR',
      openClaims: 3,
      outstandingReserve: 29300,
      lastEventAt: '2026-11-21T09:59:50Z',
    },
  ],
};

describe('TeamDashboard', () => {
  async function render(role: UserRole) {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      imports: [TeamDashboard],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    TestBed.inject(AuthStore).signIn(loginResponse(role));
    const backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(TeamDashboard);
    fixture.detectChanges();
    return { fixture, backend };
  }

  it('officers see team workload and never request exposure', async () => {
    const { fixture, backend } = await render('ClaimsOfficer');

    backend.expectOne('/api/team/workload').flush(workload);
    backend.expectNone('/api/reports/exposure');
    await fixture.whenStable();
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.textContent).toContain('Hafiz Ismail');
    expect(page.textContent).toContain('2 unassigned claims waiting to be picked up');
    // ...and it's a link to the work queue, which opens on its Unassigned tab.
    expect(page.querySelector('a.pill')?.getAttribute('href')).toBe('/staff/queue');
    expect(page.textContent).not.toContain('Outstanding exposure');
  });

  it('managers also see exposure, one total per currency (never added together)', async () => {
    const { fixture, backend } = await render('ClaimsManager');

    backend.expectOne('/api/team/workload').flush(workload);
    backend.expectOne('/api/reports/exposure').flush(exposure);
    await fixture.whenStable();
    fixture.detectChanges();

    const totals = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('.total .amount'),
    ).map((e) => e.textContent?.replace(/ /g, ' ').trim());
    // Two decimals for every currency, matching the API, whatever the runtime's ICU data says.
    expect(totals).toEqual(['IDR 20,000,000.00', 'MYR 29,300.00']);
  });
});
