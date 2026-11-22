import zlib from 'node:zlib';
import { pickUp } from './support/api';
import { expect, test, WEB_URL } from './support/fixtures';

/** A real 120 × 80 PNG (orange with a dark stripe), so the browser really renders it. */
function png(width = 120, height = 80): Buffer {
  const table = Array.from({ length: 256 }, (_, n) => {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    return c >>> 0;
  });
  const crc = (buf: Buffer) => {
    let c = 0xffffffff;
    for (const b of buf) c = table[(c ^ b) & 0xff] ^ (c >>> 8);
    return (c ^ 0xffffffff) >>> 0;
  };
  const chunk = (type: string, data: Buffer) => {
    const length = Buffer.alloc(4);
    length.writeUInt32BE(data.length);
    const typed = Buffer.concat([Buffer.from(type), data]);
    const sum = Buffer.alloc(4);
    sum.writeUInt32BE(crc(typed));
    return Buffer.concat([length, typed, sum]);
  };
  const header = Buffer.alloc(13);
  header.writeUInt32BE(width, 0);
  header.writeUInt32BE(height, 4);
  header[8] = 8; // bit depth
  header[9] = 2; // RGB
  const rows: number[] = [];
  for (let y = 0; y < height; y++) {
    rows.push(0);
    for (let x = 0; x < width; x++)
      rows.push(...(y > 30 && y < 50 ? [40, 30, 25] : [249, 115, 22]));
  }
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', header),
    chunk('IDAT', zlib.deflateSync(Buffer.from(rows))),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

const PDF = Buffer.from(
  '%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj 2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj ' +
    '3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 100]>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n',
);

test('photos and documents go both ways between claimant and officer, live', async ({
  signIn,
  claims,
}) => {
  const claim = await claims.submit();
  await pickUp(claim.id);

  const officer = await signIn('officer.my');
  await officer.goto(`/staff/claims/${claim.id}`);
  await expect(officer.getByText('No photos or documents yet.')).toBeVisible();

  const claimant = await signIn('claimant.my');
  await claimant.goto(`/claimant/claims/${claim.id}`);
  await expect(claimant.getByRole('heading', { name: 'Photos and documents' })).toBeVisible();
  await expect(claimant.getByRole('button', { name: /Add photos or files/ })).toBeVisible();

  // Two good files and one refused at once, in the browser, with the reason.
  await claimant.locator('input[type=file]').setInputFiles([
    { name: 'mirror.png', mimeType: 'image/png', buffer: png() },
    { name: 'police-report.pdf', mimeType: 'application/pdf', buffer: PDF },
    { name: 'notes.txt', mimeType: 'text/plain', buffer: Buffer.from('hello') },
  ]);
  await expect(
    claimant
      .getByRole('alert')
      .filter({ hasText: 'notes.txt: Only JPEG, PNG or WebP photos and PDF files.' }),
  ).toBeVisible();
  await expect(claimant.getByText('2 files added.')).toBeVisible();

  const preview = claimant.locator('.tile img');
  await expect(preview).toBeVisible();
  await expect
    .poll(() => preview.evaluate((img: HTMLImageElement) => img.complete && img.naturalWidth), {
      message: 'a real preview, fetched with the token (no public URL)',
    })
    .toBe(120);
  const metas = (await claimant.locator('.meta').allInnerTexts()).map((t) =>
    t.replace(/\s+/g, ' '),
  );
  expect(metas, 'each tile says who added it and its size').toHaveLength(2);
  for (const m of metas) expect(m).toMatch(/^You ·/);

  await claimant.getByRole('button', { name: 'Open mirror.png' }).click();
  const viewer = claimant.getByRole('dialog');
  await expect
    .poll(() => viewer.locator('img').evaluate((img: HTMLImageElement) => img.naturalWidth))
    .toBe(120);
  await viewer.getByRole('button', { name: 'Close' }).click();

  // Headless Chromium has no PDF viewer, so the new tab downloads it; either way it came from a local blob.
  const [tab] = await Promise.all([
    claimant.context().waitForEvent('page'),
    claimant.getByRole('button', { name: 'Open police-report.pdf' }).click(),
  ]);
  const download = await tab.waitForEvent('download');
  expect(download.url(), 'a local blob, not a storage URL').toMatch(
    new RegExp(`^blob:${new URL(WEB_URL).origin}/`),
  );
  await tab.close();

  // The officer's workbench updates by itself, and the officer is told.
  await expect(officer.locator('.tile')).toHaveCount(2, { timeout: 15_000 });
  for (const m of await officer.locator('.meta').allInnerTexts()) expect(m).toMatch(/^Claimant/);
  await officer.locator('app-notification-bell button').first().click();
  await expect(officer.locator('.notification-menu')).toContainText(
    `The claimant added a photo to claim ${claim.claimNumber}: mirror.png`,
  );
  await officer.keyboard.press('Escape');

  // The officer drops a file onto the section.
  const files = await officer.evaluateHandle(
    (bytes) => {
      const transfer = new DataTransfer();
      transfer.items.add(
        new File([new Uint8Array(bytes)], 'assessor-note.pdf', { type: 'application/pdf' }),
      );
      return transfer;
    },
    [...PDF],
  );
  const section = officer.locator('section.documents');
  await section.dispatchEvent('dragover', { dataTransfer: files });
  await expect(
    officer.locator('section.documents--dragging'),
    'dragging over highlights it',
  ).toBeVisible();
  await section.dispatchEvent('drop', { dataTransfer: files });
  await expect(officer.getByText('assessor-note.pdf added.')).toBeVisible();

  await expect(claimant.locator('.tile')).toHaveCount(3, { timeout: 15_000 });
  await expect(
    claimant.locator('.meta').nth(2),
    'the claimant sees it live, from the claims team',
  ).toHaveText(/^Claims team/);
});

test('a closed claim shows its files but offers no way to add more', async ({ signIn, claims }) => {
  const claim = await claims.submit();
  await pickUp(claim.id);
  const officer = await signIn('officer.my');
  await officer.goto(`/staff/claims/${claim.id}`);
  await officer.getByRole('button', { name: 'Reject', exact: true }).click();
  await officer.getByLabel('Reason (the claimant sees this)').fill('Not covered.');
  await officer.getByRole('button', { name: 'Reject…' }).click();
  await officer.getByRole('dialog').getByRole('button', { name: 'Reject claim' }).click();
  await expect(officer.locator('app-status-badge')).toHaveText('Rejected');

  const claimant = await signIn('claimant.my');
  await claimant.goto(`/claimant/claims/${claim.id}`);
  await expect(claimant.getByRole('heading', { name: 'Photos and documents' })).toBeVisible();
  await expect(claimant.getByRole('button', { name: /Add photos or files/ })).toHaveCount(0);
});
