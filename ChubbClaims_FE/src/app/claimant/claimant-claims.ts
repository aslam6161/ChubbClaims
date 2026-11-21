import { HttpClient } from '@angular/common/http';
import { inject, Service } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { SubmitClaimRequest, SubmitClaimResponse } from '../_models/api-types';

/**
 * Claimant commands. Reads use httpResource in the components; commands are explicit calls that
 * either succeed or throw an AppError (409/422 shown to the user), never optimistic.
 */
@Service()
export class ClaimantClaims {
  private readonly http = inject(HttpClient);

  submit(request: SubmitClaimRequest): Promise<SubmitClaimResponse> {
    return firstValueFrom(this.http.post<SubmitClaimResponse>('/api/claims', request));
  }

  answerInformationRequest(claimId: string, requestId: string, response: string): Promise<void> {
    return firstValueFrom(
      this.http.post<void>(`/api/claims/${claimId}/information-requests/${requestId}/response`, {
        response,
      }),
    );
  }
}
