import type { Page } from '@playwright/test';
import { call, pickUp } from './support/api';
import { expect, test } from './support/fixtures';

// Each kind of pop-up looks different: info (a notification), error, warning (a 409), success.

async function expectToast(page: Page, kind: string): Promise<string> {
  const toast = page.locator('mat-snack-bar-container');
  await expect(toast).toBeVisible({ timeout: 15_000 });
  await expect(toast).toHaveClass(new RegExp(`toast--${kind}`));
  await toast.evaluate((el) =>
    Promise.all(el.getAnimations({ subtree: true }).map((a) => a.finished)),
  );
  const background = await toast
    .locator('.mdc-snackbar__surface')
    .evaluate((el) => getComputedStyle(el).backgroundColor);

  // Info's button is "Open" (it would navigate and mark it read), so let that one time out.
  if (kind === 'info') await expect(toast).toHaveCount(0, { timeout: 12_000 });
  else {
    await toast.getByRole('button').click();
    await expect(toast).toHaveCount(0);
  }
  return background;
}

test('info, error, warning and success pop-ups each look different', async ({ signIn, claims }) => {
  const page = await signIn('officer.my', {
    allowConsoleErrors: /500|409|Conflict|Internal Server Error/,
  });
  const backgrounds: string[] = [];

  // Info: a claimant reports an incident, so the officer gets a notification.
  const forWarning = await claims.submit();
  backgrounds.push(await expectToast(page, 'info'));

  // Error: "Mark all read" fails on the server.
  await page.route('**/api/notifications/read-all', (r) =>
    r.fulfill({
      status: 500,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: 'Server error', status: 500 }),
    }),
  );
  await page.getByRole('button', { name: /notifications/i }).click();
  await page.getByRole('button', { name: 'Mark all read' }).click();
  await page.locator('.cdk-overlay-backdrop').click({ position: { x: 20, y: 400 } }); // close the menu
  backgrounds.push(await expectToast(page, 'error'));
  await page.unroute('**/api/notifications/read-all');
  await expect(page.locator('.notification-menu')).toHaveCount(0);

  // Warning: a real 409. Freeze the queue so the row is still there after the manager takes it.
  const stale = (await call('GET', '/api/work-queue?view=unassigned', 'officer.my')).body;
  await page.route('**/api/work-queue?*', (r) => r.fulfill({ json: stale }));
  await pickUp(forWarning.id, 'manager.my');
  await page.waitForTimeout(1500); // the push arrives; the refresh gets the frozen copy
  await page.unroute('**/api/work-queue?*');
  await page
    .locator('tr.row', { hasText: forWarning.claimNumber })
    .getByRole('button', { name: 'Pick up' })
    .click();
  backgrounds.push(await expectToast(page, 'warning'));

  // Success: an ordinary pick-up. The new claim's own pop-up comes first; a reload clears it.
  const forSuccess = await claims.submit();
  await expect(page.locator('mat-snack-bar-container')).toContainText(forSuccess.claimNumber, {
    timeout: 15_000,
  });
  await page.reload();
  await expect(page.locator('.live')).toHaveAttribute('data-state', 'live');
  await page
    .locator('tr.row', { hasText: forSuccess.claimNumber })
    .getByRole('button', { name: 'Pick up' })
    .click();
  backgrounds.push(await expectToast(page, 'success'));

  expect(new Set(backgrounds).size, backgrounds.join(' | ')).toBe(4);
});
