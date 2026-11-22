import { test as base, expect, type Locator, type Page } from '@playwright/test';
import { closeClaim, submitClaim, type DemoUser, type NewClaim, type SubmitOptions } from './api';

export { expect };

export const WEB_URL = process.env['E2E_WEB_URL'] ?? 'http://localhost:4200';

const HOME: Record<DemoUser, string> = {
  'claimant.my': '/claimant/claims',
  'claimant.sg': '/claimant/claims',
  'officer.my': '/staff/queue',
  'officer.sg': '/staff/queue',
  'manager.my': '/staff/team',
};

/**
 * Console errors that aren't the app's fault: leaving a page while SignalR is still negotiating
 * aborts that request, and the browser logs it.
 */
const BENIGN = /Failed to complete negotiation with the server: (TypeError|AbortError)/;

export interface SignInOptions {
  viewport?: { width: number; height: number };
  /** Wait for the toolbar to say "Live" (default true). */
  live?: boolean;
  /** Console errors this test expects, e.g. a 409 it provokes. */
  allowConsoleErrors?: RegExp;
  /** Runs before the app loads, e.g. to record requests or stub a route. */
  beforeSignIn?: (page: Page) => unknown;
}

interface Fixtures {
  /** A separate browser per user (own session), signed in through the login page. */
  signIn: (user: DemoUser, options?: SignInOptions) => Promise<Page>;
  /** Claims made for this test through the API; closed again when the test ends. */
  claims: {
    submit: (options?: SubmitOptions) => Promise<NewClaim>;
    /** Close this one too (e.g. a claim the test created through the UI). */
    track: (id: string) => void;
  };
}

/**
 * Runs in every page before the app: counts chime notes (wraps Web Audio's oscillator; a headless
 * browser has no speakers) and records the text of every pop-up shown, since a newer pop-up
 * replaces an older one and a check could otherwise miss it.
 */
function instrument() {
  const w = window as any;
  w.__tones = 0;
  w.__toasts = [];
  const original = AudioContext.prototype.createOscillator;
  AudioContext.prototype.createOscillator = function (this: AudioContext) {
    w.__tones++;
    return original.call(this);
  };
  const seen = new WeakSet<Element>();
  new MutationObserver(() => {
    for (const toast of document.querySelectorAll('mat-snack-bar-container')) {
      const label = toast.textContent?.replace(/\s+/g, ' ').trim();
      if (!seen.has(toast) && label) {
        seen.add(toast);
        w.__toasts.push(label);
      }
    }
  }).observe(document, { childList: true, subtree: true, characterData: true });
}

export const chimes = (page: Page): Promise<number> => page.evaluate(() => (window as any).__tones);

/** Every pop-up this page has shown, oldest first (also those already gone or replaced). */
export const toastsSeen = (page: Page): Promise<string[]> =>
  page.evaluate(() => (window as any).__toasts);

/** Waits until a pop-up containing `fragment` has been shown on the page. */
export const expectToastShown = (page: Page, fragment: string, timeout = 15_000) =>
  expect
    .poll(async () => (await toastsSeen(page)).some((t) => t.includes(fragment)), {
      message: `a pop-up saying "${fragment}"`,
      timeout,
    })
    .toBe(true);

/** Visible text with non-breaking spaces and runs of whitespace made single spaces. */
export const text = async (locator: Locator) =>
  (await locator.innerText()).replace(/\s+/g, ' ').trim(); // \s covers the non-breaking space too

export const test = base.extend<Fixtures>({
  signIn: async ({ browser }, use) => {
    const opened: {
      user: DemoUser;
      errors: string[];
      allow?: RegExp;
      close: () => Promise<void>;
    }[] = [];

    await use(async (user, options = {}) => {
      const context = await browser.newContext({
        baseURL: WEB_URL,
        viewport: options.viewport ?? { width: 1280, height: 900 },
      });
      await context.addInitScript(instrument);
      const page = await context.newPage();
      const errors: string[] = [];
      page.on('console', (m) => m.type() === 'error' && errors.push(m.text()));
      opened.push({
        user,
        errors,
        allow: options.allowConsoleErrors,
        close: () => context.close(),
      });

      await options.beforeSignIn?.(page);
      await page.goto('/login');
      await page.getByRole('button', { name: `${user}@demo.local` }).click();
      await page.waitForURL(`**${HOME[user]}`);
      if (options.live !== false)
        await expect(page.locator('.live')).toHaveAttribute('data-state', 'live', {
          timeout: 15_000,
        });
      return page;
    });

    for (const o of opened) await o.close();
    for (const o of opened) {
      const unexpected = o.errors.filter((e) => !BENIGN.test(e) && !o.allow?.test(e));
      expect(unexpected, `errors in ${o.user}'s browser console`).toEqual([]);
    }
  },

  claims: async ({}, use) => {
    const created: string[] = [];
    await use({
      submit: async (options) => {
        const claim = await submitClaim(options);
        created.push(claim.id);
        return claim;
      },
      track: (id) => created.push(id),
    });
    for (const id of created) await closeClaim(id).catch(() => undefined);
  },
});
