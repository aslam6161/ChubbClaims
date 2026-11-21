import { HttpClient } from '@angular/common/http';
import { inject, Service } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { DevLoginResponse, User } from '../_models/api-types';
import { AuthStore } from '../_services/auth-store';

/** Development sign-in: exchanges a seeded user's email for a JWT. Stands in for a real identity provider. */
@Service()
export class DevAuth {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthStore);

  async signIn(email: string): Promise<User> {
    const response = await firstValueFrom(
      this.http.post<DevLoginResponse>('/api/auth/dev-login', { email }),
    );
    this.auth.signIn(response);
    return response.user;
  }
}
