import type { Page } from '@playwright/test';
import { API_URL } from './support/api';
import { expect, test, text } from './support/fixtures';

// Which lazy area's code reached the browser, told by the component selectors inside the scripts
// it downloaded. File names can't tell: the dev server and a production build name them differently.
const STAFF_SCREENS = ['app-work-queue', 'app-claim-workbench', 'app-team-dashboard'];
const CLAIMANT_SCREENS = ['app-my-claims', 'app-claim-detail', 'app-report-incident'];

class Scripts {
  private readonly bodies: Promise<string>[] = [];

  record = (page: Page) => {
    page.on('response', (r) => {
      if (r.request().resourceType() === 'script') this.bodies.push(r.text().catch(() => ''));
    });
  };

  async contain(selectors: string[]): Promise<boolean> {
    const code = await Promise.all(this.bodies);
    return code.some((c) => selectors.some((s) => c.includes(s)));
  }
}

test('signed out, a protected page sends you to sign-in, which lists the nine demo users', async ({
  page,
}) => {
  await page.goto('/claimant/claims');
  await expect(page).toHaveURL(/\/login$/);
  await expect(page.getByRole('button').filter({ hasText: '@demo.local' })).toHaveCount(9);
});

test('a claimant lands on their claims and never downloads staff code, even by typing a staff URL', async ({
  signIn,
}) => {
  const scripts = new Scripts();
  const page = await signIn('claimant.my', { beforeSignIn: scripts.record });

  await expect(page).toHaveURL(/\/claimant\/claims$/);
  expect(await text(page.locator('mat-toolbar'))).toContain('Aisyah Rahman');
  expect(
    await scripts.contain(CLAIMANT_SCREENS),
    'positive control: claimant code was loaded',
  ).toBe(true);
  expect(await scripts.contain(STAFF_SCREENS), 'staff code was downloaded').toBe(false);

  await page.goto('/staff/queue');
  await expect(page).toHaveURL(/\/claimant\/claims$/);
  expect(await scripts.contain(STAFF_SCREENS), 'staff code was downloaded').toBe(false);

  await page.reload();
  await expect(page, 'a refresh keeps the session').toHaveURL(/\/claimant\/claims$/);

  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/login$/);
});

test('an officer lands on the work queue and never downloads claimant code', async ({ signIn }) => {
  const scripts = new Scripts();
  const page = await signIn('officer.my', { beforeSignIn: scripts.record });

  await expect(page).toHaveURL(/\/staff\/queue$/);
  expect(await scripts.contain(STAFF_SCREENS), 'positive control: staff code was loaded').toBe(
    true,
  );
  expect(await scripts.contain(CLAIMANT_SCREENS), 'claimant code was downloaded').toBe(false);
});

test('a manager lands on the team page', async ({ signIn }) => {
  const page = await signIn('manager.my');
  await expect(page).toHaveURL(/\/staff\/team$/);
});

test('the API refuses a forged token', async () => {
  const response = await fetch(`${API_URL}/api/me`, {
    headers: { Authorization: 'Bearer not-a-real-token' },
  });
  expect(response.status).toBe(401);
});
