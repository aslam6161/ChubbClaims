import { checkFiles, formatSize, MAX_DOCUMENT_BYTES, uploaderLabel } from './documents';

const file = (name: string, type: string, size = 1000): File =>
  new File([new Uint8Array(size)], name, { type });

describe('checkFiles', () => {
  it('accepts photos and PDFs within the limit', () => {
    const { accepted, rejected } = checkFiles(
      [file('a.jpg', 'image/jpeg'), file('b.png', 'image/png'), file('c.pdf', 'application/pdf')],
      0,
    );
    expect(accepted.map((f) => f.name)).toEqual(['a.jpg', 'b.png', 'c.pdf']);
    expect(rejected).toEqual([]);
  });

  it('refuses other types, empty files and files over 10 MB, each with its reason', () => {
    const { accepted, rejected } = checkFiles(
      [
        file('movie.mp4', 'video/mp4'),
        file('logo.svg', 'image/svg+xml'),
        file('empty.jpg', 'image/jpeg', 0),
        file('huge.jpg', 'image/jpeg', MAX_DOCUMENT_BYTES + 1),
      ],
      0,
    );
    expect(accepted).toEqual([]);
    expect(rejected).toEqual([
      { name: 'movie.mp4', reason: 'Only JPEG, PNG or WebP photos and PDF files.' },
      { name: 'logo.svg', reason: 'Only JPEG, PNG or WebP photos and PDF files.' },
      { name: 'empty.jpg', reason: 'The file is empty.' },
      { name: 'huge.jpg', reason: 'Too large (10.0 MB); at most 10 MB.' },
    ]);
  });

  it('stops at 20 files per claim, counting those already attached', () => {
    const { accepted, rejected } = checkFiles(
      [file('1.jpg', 'image/jpeg'), file('2.jpg', 'image/jpeg'), file('3.jpg', 'image/jpeg')],
      18,
    );
    expect(accepted.map((f) => f.name)).toEqual(['1.jpg', '2.jpg']);
    expect(rejected).toEqual([{ name: '3.jpg', reason: 'A claim can have at most 20 files.' }]);
  });
});

describe('formatSize', () => {
  it('reads like a person would say it', () => {
    expect(formatSize(512)).toBe('512 B');
    expect(formatSize(740 * 1024)).toBe('740 KB');
    expect(formatSize(2.4 * 1024 * 1024)).toBe('2.4 MB');
  });
});

describe('uploaderLabel', () => {
  it('a claimant sees "You" and "Claims team"; staff see who it was', () => {
    expect(uploaderLabel('Claimant', false)).toBe('You');
    expect(uploaderLabel('ClaimsOfficer', false)).toBe('Claims team');
    expect(uploaderLabel('Claimant', true)).toBe('Claimant');
    expect(uploaderLabel('ClaimsOfficer', true)).toBe('Officer');
    expect(uploaderLabel('ClaimsManager', true)).toBe('Manager');
  });
});
