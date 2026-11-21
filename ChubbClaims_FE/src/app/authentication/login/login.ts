import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router } from '@angular/router';
import { homeUrlFor, roleLabel } from '../../_helpers/home-route';
import { valueOr } from '../../_helpers/resource';
import type { User } from '../../_models/api-types';
import { AppError } from '../../_models/app-error';
import { PageState } from '../../_shared/page-state/page-state';
import { DevAuth } from '../dev-auth';

/** Pick a seeded user to sign in as (development only; there is no password). */
@Component({
  selector: 'app-login',
  imports: [MatButtonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule, PageState],
  templateUrl: './login.html',
  styleUrl: './login.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Login {
  private readonly devAuth = inject(DevAuth);
  private readonly router = inject(Router);

  /** ?expired=true when the auth interceptor signed the user out after a 401. */
  readonly expired = input<string>();

  protected readonly users = httpResource<User[]>(() => '/api/auth/dev-users');
  protected readonly allUsers = computed(() => valueOr(this.users, [] as User[]));
  protected readonly claimants = computed(() =>
    this.allUsers().filter((u) => u.role === 'Claimant'),
  );
  protected readonly staff = computed(() => this.allUsers().filter((u) => u.role !== 'Claimant'));

  protected readonly signingInAs = signal<string | null>(null);
  protected readonly error = signal<AppError | null>(null);
  protected readonly roleLabel = roleLabel;

  protected async signIn(user: User): Promise<void> {
    this.signingInAs.set(user.email);
    this.error.set(null);
    try {
      const signedIn = await this.devAuth.signIn(user.email);
      await this.router.navigateByUrl(homeUrlFor(signedIn.role));
    } catch (e) {
      this.error.set(AppError.from(e));
    } finally {
      this.signingInAs.set(null);
    }
  }
}
