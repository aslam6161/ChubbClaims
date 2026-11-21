import { computed, Service, signal } from '@angular/core';
import type { DevLoginResponse, User, UserRole } from '../_models/api-types';

interface Session {
  readonly accessToken: string;
  readonly expiresAt: string;
  readonly user: User;
}

const STORAGE_KEY = 'chubbclaims.session';

/**
 * The signed-in user, as signals. Persisted to sessionStorage so a page refresh keeps you signed
 * in, but a new tab or a closed browser does not (dev login; a real IdP would own this).
 * The UI uses the role only to choose screens: every action is re-authorised by the API.
 */
@Service()
export class AuthStore {
  private readonly session = signal<Session | null>(readStored());

  readonly user = computed(() => this.session()?.user ?? null);
  readonly role = computed<UserRole | null>(() => this.user()?.role ?? null);
  readonly token = computed(() => this.session()?.accessToken ?? null);
  readonly isStaff = computed(
    () => this.role() === 'ClaimsOfficer' || this.role() === 'ClaimsManager',
  );

  /** Signed in with a token that has not expired yet (checked at call time, not cached). */
  isAuthenticated(now: Date = new Date()): boolean {
    const session = this.session();
    return session !== null && new Date(session.expiresAt) > now;
  }

  signIn(response: DevLoginResponse): void {
    const session: Session = {
      accessToken: response.accessToken,
      expiresAt: response.expiresAt,
      user: response.user,
    };
    this.session.set(session);
    tryStorage((s) => s.setItem(STORAGE_KEY, JSON.stringify(session)));
  }

  signOut(): void {
    this.session.set(null);
    tryStorage((s) => s.removeItem(STORAGE_KEY));
  }
}

function readStored(): Session | null {
  const raw = tryStorage((s) => s.getItem(STORAGE_KEY));
  if (!raw) return null;
  try {
    const session = JSON.parse(raw) as Session;
    return new Date(session.expiresAt) > new Date() ? session : null;
  } catch {
    return null; // corrupted entry: start signed out
  }
}

function tryStorage<T>(action: (storage: Storage) => T): T | null {
  try {
    return action(sessionStorage);
  } catch {
    return null; // storage blocked (privacy mode): the session simply isn't kept across refreshes
  }
}
