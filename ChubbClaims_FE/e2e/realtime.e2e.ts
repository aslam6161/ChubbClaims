import type { Page } from '@playwright/test';
import { pickUp } from './support/api';
import { expect, test, text } from './support/fixtures';

// A decision in one browser updates the others: API → outbox → Kafka → SignalR → the screen re-fetches.

test('every signed-in user sees "Live" in the toolbar', async ({ signIn }) => {
  for (const user of ['claimant.my', 'officer.my', 'manager.my'] as const) {
    const page = await signIn(user);
    await expect(page.locator('.live')).toHaveText('Live');
  }
});

test("the officer approves and the claimant's open page shows it by itself", async ({
  signIn,
  claims,
}) => {
  const claim = await claims.submit({ estimate: 15000 });
  await pickUp(claim.id);

  const apiCalls: string[] = [];
  const claimant = await signIn('claimant.my', {
    beforeSignIn: (p) =>
      p.on('request', (r) => r.url().includes('/api/') && apiCalls.push(r.url())),
  });
  await claimant.goto(`/claimant/claims/${claim.id}`);
  await expect(claimant.locator('app-status-badge')).toHaveText('Under review');
  const callsBefore = apiCalls.length;

  const officer = await signIn('officer.my');
  await officer.goto(`/staff/claims/${claim.id}`);
  await officer.getByRole('button', { name: 'Approve', exact: true }).click();
  await officer.getByLabel('Settlement amount').fill('14000');
  await officer.getByRole('button', { name: 'Approve…' }).click();
  await officer.getByRole('dialog').getByRole('button', { name: 'Approve' }).click();

  await expect(claimant.locator('app-status-badge'), 'without a reload').toHaveText('Approved', {
    timeout: 15_000,
  });
  await expect
    .poll(() => text(claimant.locator('mat-card', { hasText: 'Decision' })))
    .toContain('MYR 14,000.00');
  expect(
    apiCalls.slice(callsBefore).some((u) => /\/api\/claims\/[0-9a-f-]{36}$/.test(u)),
    'the push is only a hint: the page fetched the claim again over REST',
  ).toBe(true);
});

test("a new claim appears in the officer's queue, and the manager's exposure and unassigned count move", async ({
  signIn,
  claims,
}) => {
  const officer = await signIn('officer.my');
  const manager = await signIn('manager.my');
  await expect(officer.locator('tr.row').first()).toBeVisible();
  const queueBefore = await officer.locator('tr.row').count();
  const exposure = manager.locator('.total', { hasText: 'MYR' });
  await expect(exposure).toBeVisible();
  const exposureBefore = await text(exposure);
  const unassignedBefore = await text(manager.locator('.pill'));

  const claim = await claims.submit({ estimate: 1234.5, location: 'Subang' });

  await expect(officer.locator('tr.row', { hasText: claim.claimNumber })).toBeVisible({
    timeout: 15_000,
  });
  await expect(officer.locator('tr.row')).toHaveCount(Math.min(queueBefore + 1, 20));
  await expect.poll(() => text(exposure), { timeout: 15_000 }).not.toBe(exposureBefore);
  expect(await text(exposure), 'and says how fresh it is').toMatch(/updated (just now|\d+ s ago)/);
  await expect.poll(() => text(manager.locator('.pill'))).not.toBe(unassignedBefore);
});

test('without a WebSocket the toolbar says live updates are paused, and the screen still works', async ({
  signIn,
}) => {
  const page = await signIn('claimant.sg', {
    live: false,
    allowConsoleErrors: /hubs|negotiat|WebSocket|Failed to fetch|ERR_FAILED/,
    beforeSignIn: (p) => p.route('**/hubs/**', (route) => route.abort()),
  });
  await expect(page.locator('.live')).toHaveText('Live updates paused', { timeout: 15_000 });
  await expect(page.locator('tr.row').first(), 'loaded over REST').toBeVisible();
});

test('exposure is for managers only: an officer never requests it', async ({ signIn }) => {
  const requests: string[] = [];
  const record = (p: Page) => {
    p.on('request', (r) => requests.push(r.url()));
  };
  const officer = await signIn('officer.my', { beforeSignIn: record });
  await officer.getByRole('link', { name: /Team/ }).click();
  await expect(officer.getByRole('link', { name: /unassigned claims? waiting/ })).toBeVisible();
  expect(requests.filter((u) => u.includes('/api/reports/exposure'))).toEqual([]);

  const managerRequests: string[] = [];
  await signIn('manager.my', {
    beforeSignIn: (p) => p.on('request', (r) => managerRequests.push(r.url())),
  });
  expect(
    managerRequests.some((u) => u.includes('/api/reports/exposure')),
    'positive control: the manager does',
  ).toBe(true);
});
