import type { Page } from '@playwright/test';
import { call, pickUp, requestInformation, settled, type DemoUser } from './support/api';
import { chimes, expect, expectToastShown, test, toastsSeen } from './support/fixtures';

/** Starts the bell at zero: waits for notifications still arriving from earlier tests, then reads them all. */
async function clearBell(user: DemoUser) {
  await settled(async () => (await call('GET', '/api/notifications', user)).body.unreadCount);
  await call('POST', '/api/notifications/read-all', user);
}

const badge = (page: Page) => page.locator('app-notification-bell .mat-badge-content');
const unread = async (page: Page) =>
  (await badge(page).isVisible()) ? Number(await badge(page).innerText()) : 0;
const bell = (page: Page) => page.getByRole('button', { name: /notifications/i });

async function approveInBrowser(officer: Page, claimId: string, amount: string) {
  await officer.goto(`/staff/claims/${claimId}`);
  await officer.getByRole('button', { name: 'Approve', exact: true }).click();
  await officer.getByLabel('Settlement amount').fill(amount);
  await officer.getByRole('button', { name: 'Approve…' }).click();
  await officer.getByRole('dialog').getByRole('button', { name: 'Approve' }).click();
}

test('notifications from before sign-in are counted but make no sound', async ({
  signIn,
  claims,
}) => {
  await clearBell('claimant.my');
  const claim = await claims.submit();
  await pickUp(claim.id); // "received" and "being reviewed", both before the claimant signs in
  await settled(
    async () => (await call('GET', '/api/notifications', 'claimant.my')).body.unreadCount,
  );

  const page = await signIn('claimant.my');
  await expect.poll(() => unread(page)).toBeGreaterThan(0);
  await page.waitForTimeout(800);
  expect(await chimes(page)).toBe(0);
});

test('a decision pops up for the claimant with a chime; Open goes to the claim and marks it read', async ({
  signIn,
  claims,
}) => {
  const claim = await claims.submit({ estimate: 15000 });
  await pickUp(claim.id);
  await clearBell('claimant.my');

  const claimant = await signIn('claimant.my');
  const officer = await signIn('officer.my');
  await expect.poll(() => unread(claimant)).toBe(0);
  await approveInBrowser(officer, claim.id, '14000');

  const toast = claimant.locator('mat-snack-bar-container', {
    hasText: `${claim.claimNumber} was approved`,
  });
  await expect(toast, 'the pop-up appears by itself').toBeVisible({ timeout: 15_000 });
  expect(await chimes(claimant), 'a chime of two notes').toBe(2);
  await expect.poll(() => unread(claimant), 'the bell counts it').toBe(1);

  await toast.getByRole('button', { name: 'Open' }).click();
  await expect(claimant.locator('h1')).toContainText(claim.claimNumber);
  await expect.poll(() => unread(claimant), 'opening it marks it read').toBe(0);
});

test('the bell menu: newest first; clicking an unread item opens its claim and marks it read', async ({
  signIn,
  claims,
}) => {
  await clearBell('claimant.my');
  const older = await claims.submit();
  await pickUp(older.id);
  const newer = await claims.submit();
  await pickUp(newer.id);
  await requestInformation(newer.id, 'Please also send the repair quote.');

  const claimant = await signIn('claimant.my');
  await expect.poll(() => unread(claimant)).toBe(5); // 2 × received, 2 × being reviewed, 1 question
  await bell(claimant).click();
  const items = claimant.locator('.notification-menu .item');
  await expect(items.first(), 'newest first').toContainText(
    `We need more information for claim ${newer.claimNumber}`,
  );

  await claimant
    .locator('.notification-menu .item.unread', { hasText: older.claimNumber })
    .first()
    .click();
  await expect(claimant.locator('h1')).toContainText(older.claimNumber);
  await expect.poll(() => unread(claimant), 'that one is now read').toBe(4);
});

test('the officer is told with a chime when the claimant answers; Open goes to the claim', async ({
  signIn,
  claims,
}) => {
  const claim = await claims.submit();
  await pickUp(claim.id);
  await requestInformation(claim.id, 'Please send the fire report.');
  // Earlier tests' clean-up answers open questions, which notifies the officer; let that land first,
  // or its late pop-up replaces the one this test opens.
  await clearBell('officer.my');

  const officer = await signIn('officer.my');
  const claimant = await signIn('claimant.my');
  const tonesBefore = await chimes(officer);
  await claimant.goto(`/claimant/claims/${claim.id}`);
  await claimant.getByLabel(/Your answer/).fill('Fire report and kitchen photos attached.');
  await claimant.getByRole('button', { name: 'Send answer' }).click();

  const toast = officer.locator('mat-snack-bar-container', { hasText: 'answered your question' });
  await expect(toast).toBeVisible({ timeout: 15_000 });
  expect(await chimes(officer), 'with a chime').toBe(tonesBefore + 2);
  await toast.getByRole('button', { name: 'Open' }).click();
  await expect(officer.locator('h1')).toContainText(claim.claimNumber);
});

test('with the sound off, the pop-up still shows but there is no chime', async ({
  signIn,
  claims,
}) => {
  const claim = await claims.submit();
  await pickUp(claim.id);
  await clearBell('claimant.my'); // nothing from earlier tests still on its way

  const claimant = await signIn('claimant.my');
  await bell(claimant).click();
  await claimant.getByRole('menuitem', { name: /Sound on/ }).click();
  const tonesBefore = await chimes(claimant);

  await requestInformation(claim.id, 'Please also send the repair quote.');
  await expectToastShown(claimant, `We need more information for claim ${claim.claimNumber}`);
  expect(await chimes(claimant)).toBe(tonesBefore);
});

test("a new claim notifies the market's officers with a chime, ready to pick up; another market hears nothing", async ({
  signIn,
  claims,
}) => {
  await clearBell('officer.my'); // anything still in flight from earlier tests lands first
  const officer = await signIn('officer.my');
  const sgOfficer = await signIn('officer.sg');
  const sgTones = await chimes(sgOfficer);
  const tonesBefore = await chimes(officer);

  const claim = await claims.submit();

  const toast = officer.locator('mat-snack-bar-container', {
    hasText: `${claim.claimNumber} is waiting to be picked up`,
  });
  await expect(toast).toBeVisible({ timeout: 15_000 });
  expect(await chimes(officer), 'with a chime').toBe(tonesBefore + 2);
  await toast.getByRole('button', { name: 'Open' }).click();
  await expect(officer.locator('h1')).toContainText(claim.claimNumber);
  await expect(officer.getByRole('button', { name: /Pick up/ })).toBeVisible();

  await sgOfficer.waitForTimeout(1500);
  expect(await chimes(sgOfficer), 'the SG officer heard nothing').toBe(sgTones);
  expect((await toastsSeen(sgOfficer)).filter((t) => t.includes(claim.claimNumber))).toEqual([]);
});
