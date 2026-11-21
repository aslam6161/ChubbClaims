import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import type { ClaimSummary } from '../../_models/api-types';
import { MyClaims } from './my-claims';

function claim(overrides: Partial<ClaimSummary>): ClaimSummary {
  return {
    id: 'c1',
    claimNumber: 'CLM-MY-2026-000001',
    type: 'Motor',
    status: 'Submitted',
    market: 'MY',
    currency: 'MYR',
    policyNumber: 'POL-MY-M-0001',
    incidentDate: '2026-11-20',
    reportedAt: '2026-11-21T02:00:00Z',
    settlementAmount: null,
    decisionAt: null,
    ...overrides,
  };
}

describe('MyClaims', () => {
  async function render(response: ClaimSummary[] | { status: number }) {
    TestBed.configureTestingModule({
      imports: [MyClaims],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(MyClaims);
    fixture.detectChanges();
    const request = TestBed.inject(HttpTestingController).expectOne('/api/claims/mine');
    if (Array.isArray(response)) request.flush(response);
    else request.flush(null, { status: response.status, statusText: 'Error' });
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('lists claims with their next step and flags the ones waiting on the claimant', async () => {
    const page = await render([
      claim({ id: 'a', claimNumber: 'CLM-MY-2026-000004', status: 'AwaitingInformation' }),
      claim({
        id: 'b',
        claimNumber: 'CLM-MY-2026-000005',
        status: 'Approved',
        settlementAmount: 5800,
      }),
    ]);

    expect(page.querySelectorAll('tr.row').length).toBe(2);
    expect(page.textContent).toContain('1 claim needs information from you.');
    expect(page.textContent).toContain('We need more information from you.');
    expect(page.textContent?.replace(/ /g, ' ')).toContain('MYR 5,800.00');
  });

  it('shows an empty state instead of a blank table', async () => {
    const page = await render([]);

    expect(page.textContent).toContain("You haven't reported any claims yet.");
    expect(page.querySelector('table')).toBeNull();
  });

  it('shows the error and a retry button when loading fails', async () => {
    const page = await render({ status: 500 });

    expect(page.querySelector('[role="alert"]')).not.toBeNull();
    expect(page.textContent).toContain('Try again');
  });
});
