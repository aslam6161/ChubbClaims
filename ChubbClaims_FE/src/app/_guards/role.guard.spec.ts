import { TestBed } from '@angular/core/testing';
import { CanMatchFn, provideRouter, Route, Router, UrlSegment, UrlTree } from '@angular/router';
import { AuthStore } from '../_services/auth-store';
import { loginResponse } from '../_testing';
import { roleGuard, signedOutGuard } from './role.guard';

function run(guard: CanMatchFn): boolean | UrlTree {
  const snapshot = {} as Parameters<CanMatchFn>[2];
  return TestBed.runInInjectionContext(() => guard({} as Route, [] as UrlSegment[], snapshot)) as
    boolean | UrlTree;
}

function url(result: boolean | UrlTree): boolean | string {
  return result instanceof UrlTree ? TestBed.inject(Router).serializeUrl(result) : result;
}

describe('roleGuard', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
  });

  it('lets an allowed role match', () => {
    TestBed.inject(AuthStore).signIn(loginResponse('ClaimsOfficer'));

    expect(run(roleGuard(['ClaimsOfficer', 'ClaimsManager']))).toBe(true);
  });

  it('sends another role to its own home instead (a claimant never matches the staff area)', () => {
    TestBed.inject(AuthStore).signIn(loginResponse('Claimant'));

    expect(url(run(roleGuard(['ClaimsOfficer', 'ClaimsManager'])))).toBe('/claimant/claims');
  });

  it('sends a signed-out user to the login page', () => {
    expect(url(run(roleGuard(['Claimant'])))).toBe('/login');
  });

  it('treats an expired session as signed out and clears it', () => {
    const auth = TestBed.inject(AuthStore);
    auth.signIn(loginResponse('Claimant', -1));

    expect(url(run(roleGuard(['Claimant'])))).toBe('/login');
    expect(auth.user()).toBeNull();
  });

  it('keeps signed-in users off the login page', () => {
    TestBed.inject(AuthStore).signIn(loginResponse('ClaimsManager'));

    expect(url(run(signedOutGuard))).toBe('/staff/team');
  });
});
