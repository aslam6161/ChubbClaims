import type { AppNotification, UserRole } from '../_models/api-types';

/**
 * Unread notifications that weren't in the previous load: the ones worth a sound and a pop-up.
 * `previousIds` is null on the first load, so notifications that were already waiting when the
 * user signed in are counted on the bell but never announced.
 */
export function newUnread(
  previousIds: ReadonlySet<string> | null,
  items: readonly AppNotification[],
): AppNotification[] {
  if (previousIds === null) return [];
  return items.filter((n) => !n.isRead && !previousIds.has(n.id));
}

/** Where a notification leads: the claim, in the signed-in user's own area. */
export function claimUrlFor(role: UserRole | null, claimId: string): string[] {
  return role === 'Claimant' ? ['/claimant/claims', claimId] : ['/staff/claims', claimId];
}
