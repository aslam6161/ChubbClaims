// Friendly names over the types generated from the backend OpenAPI document (src/app/_api/schema.d.ts).
// Never hand-write an API shape here: if a type is missing, the backend contract is missing it.
import type { components } from '../_api/schema';

type Schemas = components['schemas'];

// Enums
export type ClaimStatus = Schemas['ClaimStatus'];
export type ClaimType = Schemas['ClaimType'];
export type DamageType = Schemas['DamageType'];
export type Market = Schemas['Market'];
export type Priority = Schemas['Priority'];
export type UserRole = Schemas['UserRole'];
export type WaitingOn = Schemas['WaitingOn'];
export type WorkQueueView = Schemas['WorkQueueView'];
export type WorkQueueSort = Schemas['WorkQueueSort'];

// Auth
export type User = Schemas['UserResponse'];
export type DevLoginResponse = Schemas['DevLoginResponse'];

// Claimant
export type Policy = Schemas['PolicyResponse'];
export type SubmitClaimRequest = Schemas['SubmitClaimRequest'];
export type SubmitClaimResponse = Schemas['SubmitClaimResponse'];
export type ClaimSummary = Schemas['ClaimSummaryResponse'];
export type ClaimDetail = Schemas['ClaimDetailResponse'];

// Staff
export type WorkQueueItem = Schemas['WorkQueueItemResponse'];
export type TeamWorkload = Schemas['TeamWorkloadResponse'];
export type ExposureReport = Schemas['ExposureReportResponse'];

// Notifications
export type NotificationList = Schemas['NotificationListResponse'];

// Errors
export type ProblemDetails = Schemas['ProblemDetails'];
