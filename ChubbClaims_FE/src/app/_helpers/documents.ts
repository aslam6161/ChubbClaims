import type { UserRole } from '../_models/api-types';

/**
 * Client-side checks for photos and documents, matching the API's rules (ClaimDocumentRules) so
 * people hear about a wrong file at once, not after uploading 8 MB. The API still decides: it
 * detects the real type from the bytes, so these checks are for speed, not for safety.
 */
export const ACCEPTED_TYPES = ['image/jpeg', 'image/png', 'image/webp', 'application/pdf'] as const;
export const ACCEPT_ATTRIBUTE = ACCEPTED_TYPES.join(',');
export const MAX_DOCUMENT_BYTES = 10 * 1024 * 1024;
export const MAX_DOCUMENTS_PER_CLAIM = 20;

export interface RejectedFile {
  readonly name: string;
  readonly reason: string;
}

export interface FileCheck {
  readonly accepted: readonly File[];
  readonly rejected: readonly RejectedFile[];
}

/** Splits picked files into those worth uploading and those refused, with a reason each. */
export function checkFiles(files: readonly File[], alreadyAttached: number): FileCheck {
  const accepted: File[] = [];
  const rejected: RejectedFile[] = [];
  let room = MAX_DOCUMENTS_PER_CLAIM - alreadyAttached;

  for (const file of files) {
    if (!(ACCEPTED_TYPES as readonly string[]).includes(file.type)) {
      rejected.push({ name: file.name, reason: 'Only JPEG, PNG or WebP photos and PDF files.' });
    } else if (file.size === 0) {
      rejected.push({ name: file.name, reason: 'The file is empty.' });
    } else if (file.size > MAX_DOCUMENT_BYTES) {
      rejected.push({
        name: file.name,
        reason: `Too large (${formatSize(file.size)}); at most 10 MB.`,
      });
    } else if (room <= 0) {
      rejected.push({
        name: file.name,
        reason: `A claim can have at most ${MAX_DOCUMENTS_PER_CLAIM} files.`,
      });
    } else {
      accepted.push(file);
      room--;
    }
  }
  return { accepted, rejected };
}

export function isImage(contentType: string): boolean {
  return contentType.startsWith('image/');
}

/** "740 KB", "2.4 MB". */
export function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** Who added it, from the viewer's side: a claimant sees "You" and "Claims team". */
export function uploaderLabel(role: UserRole, viewerIsStaff: boolean): string {
  if (!viewerIsStaff) return role === 'Claimant' ? 'You' : 'Claims team';
  return role === 'Claimant' ? 'Claimant' : role === 'ClaimsManager' ? 'Manager' : 'Officer';
}
