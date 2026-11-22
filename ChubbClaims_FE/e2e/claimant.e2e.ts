import { approve, call, isoDay, pickUp, requestInformation } from './support/api';
import { expect, test, text } from './support/fixtures';

test('My claims: newest first, a settlement in its currency, and a banner for claims waiting on you', async ({
  signIn,
  claims,
}) => {
  const approved = await claims.submit({ estimate: 6000 });
  await pickUp(approved.id);
  await approve(approved.id, 5800);
  const waiting = await claims.submit();
  await pickUp(waiting.id);
  await requestInformation(waiting.id, 'Please send the police report.');
  const awaitingCount = (await call('GET', '/api/claims/mine', 'claimant.my')).body
    .awaitingYouCount;

  const page = await signIn('claimant.my');
  const rows = page.locator('tr.row');
  await expect(rows.first()).toContainText(waiting.claimNumber);
  await expect(rows.nth(1)).toContainText(approved.claimNumber);
  expect(await text(rows.nth(1))).toContain('MYR 5,800.00');

  const claimsWord = awaitingCount === 1 ? '1 claim needs' : `${awaitingCount} claims need`;
  await expect(page.locator('.banner--warning')).toContainText(
    `${claimsWord} information from you`,
  );
});

test("answering the claims team's question", async ({ signIn, claims }) => {
  const claim = await claims.submit();
  await pickUp(claim.id);
  await requestInformation(claim.id, 'Please send the fire department report.');

  const page = await signIn('claimant.my');
  await page.locator('tr.row', { hasText: claim.claimNumber }).click();
  await expect(page.locator('blockquote')).toContainText('fire department report');
  expect((await page.content()).toLowerCase(), 'the reserve is internal').not.toContain('reserve');

  await page.getByRole('button', { name: 'Send answer' }).click();
  await expect(
    page.getByText('Please write an answer.'),
    'an empty answer is blocked',
  ).toBeVisible();

  await page.getByLabel('Your answer').fill('Fire report and photos uploaded to the portal.');
  await page.getByRole('button', { name: 'Send answer' }).click();
  await expect(page.getByText('Thanks, your answer was sent')).toBeVisible();
  await expect(page.locator('app-status-badge'), "the server's new status").toHaveText(
    'Under review',
  );
  await expect(page.locator('.qa')).toContainText('Fire report and photos uploaded');
});

test('reporting a motor incident', async ({ signIn, claims }) => {
  const page = await signIn('claimant.my');
  await page.goto('/claimant/claims/new');

  // POL-MY-M-0000's cover ended more than a year ago: every date in it is past the reporting deadline.
  const expired = page.locator('mat-radio-button', { hasText: 'POL-MY-M-0000' });
  await expect(expired.locator('input')).toBeDisabled();
  await expect(expired).toContainText('past the deadline for reporting');
  await expect(
    page.locator('mat-radio-button', { hasText: 'POL-MY-M-0001' }).locator('input'),
  ).toBeEnabled();

  await page.getByRole('radio', { name: /POL-MY-M-0001/ }).check();
  await page.getByRole('button', { name: 'Next' }).click();

  await page.getByLabel('Date of the incident').fill(isoDay(-1));
  await page.getByLabel('Where did it happen?').click();
  await expect(page.getByText("The date can't be in the future.")).toBeVisible();

  await page.getByLabel('Date of the incident').fill(isoDay(1));
  await page.getByLabel('Where did it happen?').fill('Jalan Ampang, Kuala Lumpur');
  await page.getByLabel('What happened?').fill('Rear-ended at a traffic light.');
  await page.getByLabel('Estimated loss (optional)').fill('7300.50');
  await page.getByRole('button', { name: 'Next' }).click();

  await expect(
    page.getByLabel('Vehicle registration'),
    'a motor policy asks for the vehicle',
  ).toBeVisible();
  await expect(page.getByLabel('Property address')).toHaveCount(0);
  await page.getByLabel('Vehicle registration').fill('WXY 1234');
  await page.getByRole('checkbox', { name: /Another vehicle/ }).check();
  await page.getByRole('button', { name: 'Next' }).click();

  await expect(page.locator('.review'), 'the estimate in the policy currency').toContainText(
    'MYR 7,300.50',
  );
  await page.getByRole('button', { name: 'Submit claim' }).click();

  await page.waitForURL(/\/claimant\/claims\/[0-9a-f-]{36}$/);
  claims.track(page.url().split('/').pop()!);
  await expect(page.locator('h1')).toHaveText(/^CLM-MY-\d{4}-\d{6}$/);
  await expect(page.locator('.next')).toContainText('Waiting for a claims officer');
  await expect(page.locator('dl').first(), 'the vehicle details were saved').toContainText(
    'WXY 1234',
  );
});

test('a property policy asks for the property instead of a vehicle', async ({ signIn }) => {
  const page = await signIn('claimant.my');
  await page.goto('/claimant/claims/new');
  await page.getByRole('radio', { name: /POL-MY-P-0001/ }).check();
  await page.getByRole('button', { name: 'Next' }).click();
  await page.getByLabel('Date of the incident').fill(isoDay(1));
  await page.getByLabel('Where did it happen?').fill('Bangsar');
  await page.getByLabel('What happened?').fill('Pipe burst.');
  await page
    .locator('.mat-vertical-content', { has: page.getByLabel('Where did it happen?') })
    .getByRole('button', { name: 'Next' })
    .click();

  await expect(page.getByLabel('Property address')).toBeVisible();
  await expect(page.getByLabel('Vehicle registration')).toHaveCount(0);
});

test('the incident date: a calendar with no future days, and the picked day reaches the API unchanged', async ({
  signIn,
  claims,
}) => {
  const posted: { incidentDate: string }[] = [];
  const page = await signIn('claimant.my', {
    beforeSignIn: (p) => {
      p.on('request', (r) => {
        if (r.method() === 'POST' && r.url().endsWith('/api/claims')) posted.push(r.postDataJSON());
      });
    },
  });
  await page.goto('/claimant/claims/new');
  await page.getByText('POL-MY-M-0001').first().click();
  await page.getByRole('button', { name: 'Next' }).click();

  const calendar = page.locator('mat-datepicker-content');
  await page.locator('mat-form-field', { hasText: 'Date of the incident' }).click(); // where a user clicks
  await expect(calendar, 'clicking the field opens the calendar').toBeVisible();
  // Material ignores Escape while the calendar is still animating open; a person can't be that quick.
  await calendar.evaluate((el) =>
    Promise.all(el.getAnimations({ subtree: true }).map((a) => a.finished)),
  );
  await page.keyboard.press('Escape');
  await expect(calendar).toHaveCount(0);
  await page.getByRole('button', { name: 'Open calendar' }).click();
  await expect(calendar, 'so does the calendar button').toBeVisible();

  const label = (d: Date) =>
    d.toLocaleDateString('en-US', { month: 'long', day: 'numeric', year: 'numeric' });
  const today = new Date();
  const tomorrow = new Date(today.getFullYear(), today.getMonth(), today.getDate() + 1);
  const picked = new Date(today.getFullYear(), today.getMonth(), today.getDate() - 2);
  if (tomorrow.getMonth() === today.getMonth())
    await expect(page.locator(`[aria-label="${label(tomorrow)}"]`)).toHaveAttribute(
      'aria-disabled',
      'true',
    );

  if (picked.getMonth() !== today.getMonth())
    await page.getByRole('button', { name: 'Previous month' }).click();
  await page.locator(`[aria-label="${label(picked)}"]`).click();
  await expect(calendar).toHaveCount(0);
  await expect(page.getByLabel(/Date of the incident/)).not.toHaveValue('');

  await page.getByLabel(/Where did it happen/).fill('Jalan Ampang, Kuala Lumpur');
  await page
    .getByRole('textbox', { name: /What happened?/ })
    .fill('Datepicker check: rear-ended at a light');
  await page
    .locator('.mat-vertical-content', { has: page.getByLabel(/Where did it happen/) })
    .getByRole('button', { name: 'Next' })
    .click();
  await page.getByLabel(/Vehicle registration/).fill('DATE 1');
  await page
    .locator('.mat-vertical-content', { has: page.getByLabel(/Vehicle registration/) })
    .getByRole('button', { name: 'Next' })
    .click();

  const pretty = picked.toLocaleDateString('en-GB', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  });
  const iso = `${picked.getFullYear()}-${String(picked.getMonth() + 1).padStart(2, '0')}-${String(picked.getDate()).padStart(2, '0')}`;
  await expect(
    page.locator('dl', { hasText: pretty }),
    'the review shows the picked day',
  ).toBeVisible();
  await page.getByRole('button', { name: /Submit/ }).click();
  await page.waitForURL(/\/claimant\/claims\/[0-9a-f-]{36}$/);
  claims.track(page.url().split('/').pop()!);

  expect(posted[0]?.incidentDate, 'the day sent to the API').toBe(iso);
  await expect(page.getByText('Datepicker check')).toBeVisible();
  await expect(page.locator('body')).toContainText(pretty);
});

test('a failed load shows the error and Try again, not a blank page', async ({ signIn }) => {
  const page = await signIn('claimant.my', { allowConsoleErrors: /503/ });
  await page.route('**/api/claims/mine?*', (route) =>
    route.fulfill({
      status: 503,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: 'Service unavailable', status: 503 }),
    }),
  );
  await page.reload();
  await expect(page.getByRole('alert')).toContainText('Service unavailable');
  await expect(page.getByRole('button', { name: 'Try again' })).toBeVisible();
});
