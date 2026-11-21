import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatToolbarModule } from '@angular/material/toolbar';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { homeUrlFor, roleLabel } from '../../_helpers/home-route';
import { AuthStore } from '../../_services/auth-store';

export interface NavLink {
  readonly label: string;
  readonly path: string;
  readonly icon: string;
}

/**
 * Toolbar + navigation for a role area. One layout for claimants and staff: they differ only in
 * their links, which each area passes as route data (`nav`), bound here as an input.
 */
@Component({
  selector: 'app-area-layout',
  imports: [
    MatButtonModule,
    MatIconModule,
    MatToolbarModule,
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
  ],
  templateUrl: './area-layout.html',
  styleUrl: './area-layout.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AreaLayout {
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);

  readonly nav = input<readonly NavLink[]>([]);

  protected readonly user = this.auth.user;
  protected readonly home = computed(() => homeUrlFor(this.auth.role()));
  protected readonly who = computed(() => {
    const user = this.user();
    if (!user) return '';
    return [roleLabel(user.role), user.market].filter(Boolean).join(' · ');
  });

  protected signOut(): void {
    this.auth.signOut();
    void this.router.navigateByUrl('/login');
  }
}
