import { call, pickUp, requestInformation } from './support/api';
import { expect, test, text } from './support/fixtures';

const rangeLabel = (page: import('@playwright/test').Page) =>
  page.locator('.mat-mdc-paginator-range-label');

test('My claims: page size, paging that keeps the current page on screen, search, and the banner', async ({
  signIn,
  claims,
}) => {
  // More than one page of 10, and one claim waiting on the claimant.
  const total: number = (await call('GET', '/api/claims/mine', 'claimant.my')).body.totalCount;
  for (let i = total; i < 11; i++) await claims.submit();
  const word = `Kitchen${Date.now().toString(36)}`;
  const findMe = await claims.submit({ description: `Water leak under the ${word} sink` });
  const waiting = await claims.submit();
  await pickUp(waiting.id);
  await requestInformation(waiting.id, 'Photos of the damage, please.');

  const page = await signIn('claimant.my');
  const rows = page.locator('tr.row');
  await expect(page.locator('mat-paginator')).toBeVisible();
  const banner = page.locator('a.banner--warning');
  await expect(banner).toBeVisible();
  await expect(banner, 'in the warning colour').toHaveCSS('background-color', 'rgb(255, 232, 179)');
  const all = Number((await rangeLabel(page).innerText()).match(/of (\d+)/)![1]);
  await expect(rows, 'a page of 20 by default').toHaveCount(Math.min(all, 20));

  await page.locator('mat-paginator .mat-mdc-paginator-touch-target').click(); // Material's tap area over the selector
  await page.getByRole('option', { name: '10' }).click();
  await expect(rangeLabel(page)).toHaveText(`1 – 10 of ${all}`);
  await expect(rows).toHaveCount(10);

  // A slow network: while page 2 loads, page 1 stays on screen (no blank table, no jump).
  await page.route('**/api/claims/mine?*page=2*', async (r) => {
    await new Promise((x) => setTimeout(x, 1500));
    await r.continue().catch(() => undefined); // the app may have cancelled it for a newer request
  });
  await page.getByRole('button', { name: 'Next page' }).click();
  await page.waitForTimeout(400);
  await expect(rows, 'the current rows stay while the next page loads').toHaveCount(10, {
    timeout: 100,
  });
  await expect(rangeLabel(page)).toHaveText(/^\s*11 – /);
  await expect(rows.first()).toBeVisible();
  await page.unroute('**/api/claims/mine?*page=2*');

  const search = page.getByLabel(/Search claim, policy/);
  await search.fill(word.toLowerCase());
  await expect(rows, 'back to page 1 with the one match (ignoring case)').toHaveCount(1);
  await expect(rows).toContainText(findMe.claimNumber);
  await expect(rangeLabel(page)).toHaveText('1 – 1 of 1');
  await expect(banner, 'the banner still counts all claims').toBeVisible();

  await search.fill('no such thing');
  await expect(page.locator('body')).toContainText('No claims match “no such thing”.');
  await page.getByRole('button', { name: 'Clear search' }).click();
  await expect(rows, 'clear brings the full list back').toHaveCount(10);

  await banner.click();
  await expect(
    page.getByText('The claims team asked'),
    'the banner opens a claim waiting for an answer',
  ).toBeVisible();
});

test('the work queue: one page per request, search by place, the overdue count ignores the search, and tabs keep it', async ({
  signIn,
  claims,
}) => {
  const place = `Paging street ${Date.now().toString(36)}`;
  await claims.submit({ location: place });
  const requests: string[] = [];
  const page = await signIn('officer.my', {
    beforeSignIn: (p) =>
      p.on('request', (r) => r.url().includes('/api/work-queue') && requests.push(r.url())),
  });

  await expect(rangeLabel(page)).toHaveText(/^\s*1 – \d+ of \d+\s*$/);
  expect(
    requests.some((u) => /[?&]page=1\b/.test(u) && /[?&]pageSize=20\b/.test(u)),
    requests.join(' '),
  ).toBe(true);
  const overdue = await text(page.locator('.overdue-count'));

  const search = page.getByLabel(/Search claim, policy or place/);
  await search.fill(place);
  await expect(page.locator('tr.row')).toHaveCount(1);
  expect(
    await text(page.locator('.overdue-count')),
    'the overdue count covers the whole queue',
  ).toBe(overdue);

  await page.getByRole('tab', { name: 'My claims' }).click();
  await expect(search, 'switching tab keeps the search').toHaveValue(place);
  await expect(page.locator('body'), 'and searches that tab').toContainText(
    'No claims in this tab match',
  );
});

test('the team page link opens the unassigned queue with the claims it counts', async ({
  signIn,
}) => {
  for (const user of ['manager.my', 'officer.my'] as const) {
    const page = await signIn(user);
    if (user === 'officer.my') await page.getByRole('link', { name: /Team/ }).click();
    const link = page.getByRole('link', { name: /unassigned claims? waiting/ });
    const count = Number((await link.innerText()).match(/\d+/)![0]);
    await link.click();

    await expect(page).toHaveURL(/\/staff\/queue$/);
    await expect(page.getByRole('tab', { selected: true })).toHaveText('Unassigned');
    await expect(page.locator('tr.row'), `${user}: the link said ${count}`).toHaveCount(
      Math.min(count, 20),
    );
  }
});
