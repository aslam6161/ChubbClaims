import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import type { ClaimSummary, MyClaimsPage } from '../../_models/api-types';
import { SearchBox } from '../../_shared/search-box/search-box';
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

/** A page as the API returns it; the banner fields cover ALL the claimant's claims. */
function pageOf(items: ClaimSummary[], extra: Partial<MyClaimsPage> = {}): MyClaimsPage {
  const awaiting = items.filter((c) => c.status === 'AwaitingInformation');
  return {
    items,
    page: 1,
    pageSize: 20,
    totalCount: items.length,
    awaitingYouCount: awaiting.length,
    oldestAwaitingYouClaimId: awaiting[0]?.id ?? null,
    ...extra,
  };
}

const isMine = (r: { url: string }) => r.url === '/api/claims/mine';

describe('MyClaims', () => {
  let backend: HttpTestingController;

  async function render(response: MyClaimsPage | { status: number }) {
    TestBed.configureTestingModule({
      imports: [MyClaims],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(MyClaims);
    fixture.detectChanges();
    const request = backend.expectOne(isMine);
    expect(request.request.params.get('page')).toBe('1');
    if ('items' in response) request.flush(response);
    else request.flush(null, { status: response.status, statusText: 'Error' });
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, page: fixture.nativeElement as HTMLElement };
  }

  it('lists claims with their next step and flags the ones waiting on the claimant', async () => {
    const { page } = await render(
      pageOf([
        claim({ id: 'a', claimNumber: 'CLM-MY-2026-000004', status: 'AwaitingInformation' }),
        claim({
          id: 'b',
          claimNumber: 'CLM-MY-2026-000005',
          status: 'Approved',
          settlementAmount: 5800,
        }),
      ]),
    );

    expect(page.querySelectorAll('tr.row').length).toBe(2);
    expect(page.textContent).toContain('1 claim needs information from you.');
    expect(page.textContent).toContain('We need more information from you.');
    expect(page.textContent?.replace(/ /g, ' ')).toContain('MYR 5,800.00');
  });

  it('the warning banner links to the claim that has waited longest, whatever page is showing', async () => {
    // This page doesn't contain the waiting claims, but the banner still counts and links them.
    const { page } = await render(
      pageOf([claim({ id: 'x', status: 'Approved' })], {
        totalCount: 23,
        awaitingYouCount: 2,
        oldestAwaitingYouClaimId: 'oldest',
      }),
    );

    const banner = page.querySelector<HTMLAnchorElement>('a.banner--warning')!;
    expect(banner.textContent).toContain('2 claims need information from you.');
    expect(banner.getAttribute('href')).toBe('/claimant/claims/oldest');
  });

  it('searches on the server, from page 1', async () => {
    const { fixture } = await render(pageOf([claim({})], { totalCount: 30 }));

    fixture.debugElement.query(By.directive(SearchBox)).componentInstance.search.emit('ampang');
    TestBed.tick();

    const request = backend.expectOne(isMine);
    expect(request.request.params.get('search')).toBe('ampang');
    expect(request.request.params.get('page')).toBe('1');
    request.flush(pageOf([]));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No claims match “ampang”.');
  });

  it('shows an empty state instead of a blank table', async () => {
    const { page } = await render(pageOf([]));

    expect(page.textContent).toContain("You haven't reported any claims yet.");
    expect(page.querySelector('table')).toBeNull();
  });

  it('shows the error and a retry button when loading fails', async () => {
    const { page } = await render({ status: 500 });

    expect(page.querySelector('[role="alert"]')).not.toBeNull();
    expect(page.textContent).toContain('Try again');
  });
});
