import { call, pickUp, requestInformation } from './support/api';
import { expect, expectToastShown, test, text } from './support/fixtures';

test('the unassigned queue: overdue claims first and highlighted, new ones waiting to be picked up', async ({
  signIn,
  claims,
}) => {
  const claim = await claims.submit();
  const page = await signIn('officer.my');

  await expect(page.locator('tr.row', { hasText: claim.claimNumber })).toBeVisible();
  const rows = await page.locator('tr.row').all();
  const overdue = await Promise.all(
    rows.map(async (r) => (await r.getAttribute('class'))!.includes('row--overdue')),
  );
  expect(overdue, 'overdue rows come first').toEqual(
    [...overdue].sort((a, b) => Number(b) - Number(a)),
  );
  for (const [i, row] of rows.entries())
    if (overdue[i]) await expect(row, 'an overdue row says so').toContainText('Overdue');
});

test('the My claims tab: no pick-up button, and who each claim waits on', async ({
  signIn,
  claims,
}) => {
  const claim = await claims.submit();
  await pickUp(claim.id);
  await requestInformation(claim.id, 'Photos, please.');

  const page = await signIn('officer.my');
  await page.getByRole('tab', { name: 'My claims' }).click();
  const row = page.locator('tr.row', { hasText: claim.claimNumber });
  await expect(row).toContainText('Awaiting information');
  await expect(row, 'it waits on the claimant').toContainText('Claimant');
  await expect(page.getByRole('button', { name: 'Pick up' })).toHaveCount(0);
});

test('pick up from the queue, adjust the reserve, add a note, ask the claimant', async ({
  signIn,
  claims,
}) => {
  const claim = await claims.submit({ estimate: 8500 });
  const page = await signIn('officer.my');

  await page
    .locator('tr.row', { hasText: claim.claimNumber })
    .getByRole('button', { name: 'Pick up' })
    .click();
  await page.waitForURL(/\/staff\/claims\/[0-9a-f-]{36}$/);
  await expect(page.getByText('Assigned to you')).toBeVisible();
  const actions = page.locator('aside');
  for (const name of ['Adjust reserve', 'Request information', 'Approve', 'Reject'])
    await expect(actions.getByRole('button', { name: new RegExp(name) })).toBeVisible();
  await expect(actions.getByRole('button', { name: /Pick up/ })).toHaveCount(0);
  const money = page.locator('mat-card', { hasText: 'Money' });
  expect(await text(money), 'staff see the reserve').toContain('Reserve MYR 8,500.00');

  await page.getByRole('button', { name: /Adjust reserve/ }).click();
  await page.getByLabel('New reserve').fill('-5');
  await page.getByLabel('Reason').fill('typo');
  await page.getByRole('button', { name: 'Save reserve' }).click();
  await expect(page.getByText("The reserve can't be negative.")).toBeVisible();
  await page.getByLabel('New reserve').fill('9200');
  await page.getByLabel('Reason').fill('Panel workshop quote received');
  await page.getByRole('button', { name: 'Save reserve' }).click();
  await expect(page.getByText('Reserve updated.')).toBeVisible();
  await expect(money.locator('dd', { hasText: /MYR\s9,200\.00/ }).first()).toBeVisible();
  const audit = page.locator('.timeline').last();
  await expect(audit).toContainText('Panel workshop quote received');
  expect(await text(audit)).toContain('MYR 8,500.00 → MYR 9,200.00');

  await page.getByLabel('Add a note').fill('Called the claimant, photos on the way.');
  await page.getByRole('button', { name: 'Add', exact: true }).click();
  await expect(page.locator('.note', { hasText: 'Called the claimant' })).toBeVisible();
  await expect(page.getByLabel('Add a note'), 'the note field is cleared').toHaveValue('');
  await expect(
    page.locator('mat-form-field', { hasText: 'Add a note' }),
    'and not shown as an error',
  ).not.toHaveClass(/mat-form-field-invalid/);

  await page.getByRole('button', { name: /Request information/ }).click();
  await page.getByLabel('Question for the claimant').fill('Please upload photos of the damage.');
  await page.getByRole('button', { name: 'Send question' }).click();
  await expect(page.getByText('Question sent to the claimant.')).toBeVisible();
  await expect(page.locator('app-status-badge')).toHaveText('Awaiting information');
  await expect(
    actions.getByRole('button', { name: /Adjust reserve/ }),
    'only the reserve can change now',
  ).toBeVisible();
  await expect(actions.getByRole('button', { name: /Approve|Reject/ })).toHaveCount(0);
});

test('approve with a confirmation of the amount, then record the payment', async ({
  signIn,
  claims,
}) => {
  const claim = await claims.submit({ estimate: 15000 });
  await pickUp(claim.id);
  const page = await signIn('officer.my');
  await page.goto(`/staff/claims/${claim.id}`);
  await expect(page.getByText('Assigned to you')).toBeVisible();

  await page.getByRole('button', { name: 'Approve', exact: true }).click();
  await page.getByLabel('Settlement amount').fill('14000');
  await page.getByRole('button', { name: 'Approve…' }).click();
  const dialog = page.getByRole('dialog');
  expect(await text(dialog)).toContain('Settle for MYR 14,000.00');
  await dialog.getByRole('button', { name: 'Approve' }).click();
  await expect(page.getByText('Claim approved.')).toBeVisible();
  await expect(page.locator('app-status-badge')).toHaveText('Approved');
  const money = page.locator('mat-card', { hasText: 'Money' });
  await expect.poll(() => text(money)).toContain('Settlement MYR 14,000.00');

  await page.getByRole('button', { name: /Record payment/ }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Record payment' }).click();
  await expect(page.getByText('Payment recorded. Claim closed.')).toBeVisible();
  await expect(page.locator('app-status-badge')).toHaveText('Settled');
  await expect.poll(() => text(money)).toContain('Outstanding MYR 0.00');
  await expect(page.getByText('This claim is closed. You can still add notes.')).toBeVisible();
});

test('reject: cancelling the confirmation changes nothing; confirming records the reason', async ({
  signIn,
  claims,
}) => {
  const claim = await claims.submit();
  await pickUp(claim.id);
  const page = await signIn('officer.my');
  await page.goto(`/staff/claims/${claim.id}`);

  await page.getByRole('button', { name: 'Reject', exact: true }).click();
  await page
    .getByLabel('Reason (the claimant sees this)')
    .fill('Damage from a long-term leak; excluded under the policy.');
  await page.getByRole('button', { name: 'Reject…' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.locator('app-status-badge')).toHaveText('Under review');

  await page.getByRole('button', { name: 'Reject…' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Reject claim' }).click();
  await expect(page.locator('app-status-badge')).toHaveText('Rejected');
  await expect(page.locator('.timeline').first()).toContainText('excluded under the policy');
});

test('a stale queue: someone else picked the claim up first, a clear 409 and the row goes', async ({
  signIn,
  claims,
}) => {
  const claim = await claims.submit();
  const page = await signIn('officer.my', { allowConsoleErrors: /409|Conflict/ });
  const row = page.locator('tr.row', { hasText: claim.claimNumber });
  await expect(row).toBeVisible();

  // Freeze the officer's queue on its current copy, so the live refresh can't remove the row first.
  const stale = (await call('GET', '/api/work-queue?view=unassigned', 'officer.my')).body;
  await page.route('**/api/work-queue?*', (r) => r.fulfill({ json: stale }));
  await pickUp(claim.id, 'manager.my');
  await page.waitForTimeout(1500); // the push arrives; the refresh gets the frozen copy
  await page.unroute('**/api/work-queue?*');

  await row.getByRole('button', { name: 'Pick up' }).click();
  // The new claim's own notification may still be showing too, so look for this message.
  await expectToastShown(page, `${claim.claimNumber} was already picked up by another officer.`);
  await expect(row, 'the queue reloads without it').toHaveCount(0);
});

test("a manager on another officer's claim can only add notes", async ({ signIn, claims }) => {
  const claim = await claims.submit();
  await pickUp(claim.id);
  const page = await signIn('manager.my');
  await page.goto(`/staff/claims/${claim.id}`);

  await expect(page.locator('.assignee')).toContainText('Assigned to another officer');
  await expect(
    page.locator('aside').getByRole('button', { name: /Approve|Reject|Adjust|Request|Record/ }),
  ).toHaveCount(0);
  await expect(page.getByLabel('Add a note')).toBeVisible();
});
