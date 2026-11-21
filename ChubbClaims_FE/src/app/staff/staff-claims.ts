import { HttpClient } from '@angular/common/http';
import { inject, Service } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/**
 * Officer commands, one per task-based endpoint. Each resolves when the server accepted the action
 * and rejects with an AppError otherwise (409 wrong state / changed by someone else, 422 rule,
 * 403 not your claim). Never optimistic: callers reload the claim afterwards.
 */
@Service()
export class StaffClaims {
  private readonly http = inject(HttpClient);

  pickUp(claimId: string): Promise<void> {
    return this.post(`/api/claims/${claimId}/pick-up`);
  }

  adjustReserve(claimId: string, amount: number, reason: string): Promise<void> {
    return firstValueFrom(
      this.http.put<void>(`/api/claims/${claimId}/reserve`, { amount, reason }),
    );
  }

  requestInformation(claimId: string, question: string): Promise<void> {
    return this.post(`/api/claims/${claimId}/information-requests`, { question });
  }

  approve(claimId: string, settlementAmount: number, note: string | null): Promise<void> {
    return this.post(`/api/claims/${claimId}/approve`, { settlementAmount, note });
  }

  settle(claimId: string): Promise<void> {
    return this.post(`/api/claims/${claimId}/settle`);
  }

  reject(claimId: string, reason: string): Promise<void> {
    return this.post(`/api/claims/${claimId}/reject`, { reason });
  }

  addNote(claimId: string, text: string): Promise<void> {
    return this.post(`/api/claims/${claimId}/notes`, { text });
  }

  private post(url: string, body: object = {}): Promise<void> {
    return firstValueFrom(this.http.post<void>(url, body));
  }
}
