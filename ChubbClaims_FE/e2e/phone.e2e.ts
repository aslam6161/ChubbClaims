import type { Page } from '@playwright/test';
import { pickUp } from './support/api';
import { expect, test } from './support/fixtures';

// At phone width no screen may scroll the whole page sideways (a wide table may scroll inside its own box).
const PHONE = { width: 390, height: 844 };

async function expectNoSidewaysScroll(page: Page, screen: string) {
  await expect
    .poll(
      () =>
        page.evaluate(
          () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
        ),
      { message: `${screen}: page wider than the phone by` },
    )
    .toBeLessThanOrEqual(0);
}

test('sign-in fits a phone', async ({ browser }) => {
  const page = await browser.newPage({ viewport: PHONE });
  await page.goto('/login');
  await expect(page.getByRole('button', { name: /claimant\.my/ })).toBeVisible();
  await expectNoSidewaysScroll(page, 'sign-in');
  await page.close();
});

test("the claimant's screens fit a phone", async ({ signIn, claims }) => {
  const claim = await claims.submit();
  const page = await signIn('claimant.my', { viewport: PHONE });
  await expect(page.locator('tr.row, .row').first()).toBeVisible();
  await expectNoSidewaysScroll(page, 'my claims');

  await page.goto(`/claimant/claims/${claim.id}`);
  await expect(page.locator('app-status-badge').first()).toBeVisible();
  await expectNoSidewaysScroll(page, 'claim detail');

  await page.goto('/claimant/claims/new');
  await expect(page.getByText('POL-MY-M-0001').first()).toBeVisible();
  await expectNoSidewaysScroll(page, 'report an incident');
});

test('staff screens fit a phone', async ({ signIn, claims }) => {
  const claim = await claims.submit();
  await pickUp(claim.id);
  const officer = await signIn('officer.my', { viewport: PHONE });
  await expect(officer.locator('tr.row').first()).toBeVisible();
  await expectNoSidewaysScroll(officer, 'work queue');

  await officer.goto(`/staff/claims/${claim.id}`);
  await expect(officer.getByText('Actions', { exact: true })).toBeVisible();
  await expectNoSidewaysScroll(officer, 'workbench');

  const manager = await signIn('manager.my', { viewport: PHONE });
  await expect(manager.locator('.total').first()).toBeVisible();
  await expectNoSidewaysScroll(manager, 'team');
});
