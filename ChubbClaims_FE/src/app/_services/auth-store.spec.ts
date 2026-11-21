import { TestBed } from '@angular/core/testing';
import { loginResponse } from '../_testing';
import { AuthStore } from './auth-store';

describe('AuthStore', () => {
  beforeEach(() => sessionStorage.clear());

  it('signs in, exposes the role and survives a refresh (new store instance)', () => {
    TestBed.inject(AuthStore).signIn(loginResponse('ClaimsOfficer'));

    TestBed.resetTestingModule();
    const afterRefresh = TestBed.inject(AuthStore);

    expect(afterRefresh.isAuthenticated()).toBe(true);
    expect(afterRefresh.role()).toBe('ClaimsOfficer');
    expect(afterRefresh.isStaff()).toBe(true);
    expect(afterRefresh.token()).toBe('token-ClaimsOfficer');
  });

  it('treats an expired token as signed out', () => {
    const store = TestBed.inject(AuthStore);
    store.signIn(loginResponse('Claimant', -1));

    expect(store.isAuthenticated()).toBe(false);
  });

  it('signs out and clears storage', () => {
    const store = TestBed.inject(AuthStore);
    store.signIn(loginResponse('Claimant'));

    store.signOut();

    expect(store.user()).toBeNull();
    expect(sessionStorage.length).toBe(0);
  });
});
