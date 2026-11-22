import type { Page } from '@playwright/test';
import { API_URL } from './support/api';
import { expect, test, text } from './support/fixtures';

// Component source paths as the dev server reports them: which lazy area's code was downloaded.
const STAFF_CODE = /%2Fapp%2Fstaff%2F/;
const CLAIMANT_CODE = /%2Fapp%2Fclaimant%2F/;

function recordScripts(into: string[]) {
  return (page: Page) => {
    page.on('request', (r) => r.resourceType() === 'script' && into.push(r.url()));
  };
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
  const scripts: string[] = [];
  const page = await signIn('claimant.my', { beforeSignIn: recordScripts(scripts) });

  await expect(page).toHaveURL(/\/claimant\/claims$/);
  expect(await text(page.locator('mat-toolbar'))).toContain('Aisyah Rahman');
  expect(
    scripts.some((u) => CLAIMANT_CODE.test(u)),
    'positive control: claimant code was loaded',
  ).toBe(true);
  expect(
    scripts.some((u) => STAFF_CODE.test(u)),
    'staff code was downloaded',
  ).toBe(false);

  await page.goto('/staff/queue');
  await expect(page).toHaveURL(/\/claimant\/claims$/);
  expect(
    scripts.some((u) => STAFF_CODE.test(u)),
    'staff code was downloaded',
  ).toBe(false);

  await page.reload();
  await expect(page, 'a refresh keeps the session').toHaveURL(/\/claimant\/claims$/);

  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/login$/);
});

test('an officer lands on the work queue and never downloads claimant code', async ({ signIn }) => {
  const scripts: string[] = [];
  const page = await signIn('officer.my', { beforeSignIn: recordScripts(scripts) });

  await expect(page).toHaveURL(/\/staff\/queue$/);
  expect(
    scripts.some((u) => STAFF_CODE.test(u)),
    'positive control: staff code was loaded',
  ).toBe(true);
  expect(
    scripts.some((u) => CLAIMANT_CODE.test(u)),
    'claimant code was downloaded',
  ).toBe(false);
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
