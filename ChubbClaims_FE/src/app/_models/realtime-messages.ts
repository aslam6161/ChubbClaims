import type { ClaimStatus, ClaimType, Market } from './api-types';

// SignalR messages from /hubs/claims. Hand-written because the hub is not part of the OpenAPI
// document; they mirror the backend records in ChubbClaims.Messaging/Realtime/IRealtimePush.cs
// (camelCase JSON, enums as strings). Pushes are hints: screens re-fetch over REST.

/** Sent to the claimant who owns the claim and to staff in its market. */
export interface ClaimUpdatedMessage {
  readonly claimId: string;
  readonly claimNumber: string;
  readonly market: Market;
  readonly eventType: string;
  /** Status after the event; null when it doesn't change (e.g. reserve adjusted). */
  readonly status: ClaimStatus | null;
  readonly occurredAt: string;
}

/** Sent to managers when the exposure read model changed. */
export interface ExposureUpdatedMessage {
  readonly market: Market;
  readonly currency: string;
  readonly claimType: ClaimType;
  readonly occurredAt: string;
}
