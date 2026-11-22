import { httpResource } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { Toast } from '../../_services/toast';
import { Router } from '@angular/router';
import { claimUrlFor, newUnread } from '../../_helpers/notifications';
import { valueOr } from '../../_helpers/resource';
import { timeAgo } from '../../_helpers/time-ago';
import type { AppNotification, NotificationList } from '../../_models/api-types';
import { AuthStore } from '../../_services/auth-store';
import { NotificationSound } from '../../_services/notification-sound';
import { Notifications } from '../../_services/notifications';
import { reloadOnClaimUpdates } from '../../_services/realtime';

/**
 * The toolbar bell: unread count, the list, and a chime plus a clickable pop-up when a new
 * notification arrives. Like every screen, it re-fetches on ClaimUpdated (the server stores the
 * notification before it pushes) and polls while live updates are paused.
 */
@Component({
  selector: 'app-notification-bell',
  imports: [MatBadgeModule, MatButtonModule, MatDividerModule, MatIconModule, MatMenuModule],
  templateUrl: './notification-bell.html',
  styleUrl: './notification-bell.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotificationBell {
  private readonly auth = inject(AuthStore);
  private readonly commands = inject(Notifications);
  private readonly router = inject(Router);
  private readonly toast = inject(Toast);
  protected readonly sound = inject(NotificationSound);

  protected readonly list = httpResource<NotificationList>(() => '/api/notifications');
  private readonly data = computed(() => valueOr(this.list, undefined));
  protected readonly items = computed(() => this.data()?.items ?? []);
  protected readonly unread = computed(() => this.data()?.unreadCount ?? 0);

  /** Refreshed when the menu opens, so "3 min ago" is current without a timer. */
  protected readonly now = signal(new Date());

  private seenIds: Set<string> | null = null;

  constructor() {
    reloadOnClaimUpdates(this.list);
    effect(() => {
      const data = this.data();
      if (!data) return;
      const fresh = newUnread(this.seenIds, data.items);
      this.seenIds = new Set(data.items.map((n) => n.id));
      if (fresh.length > 0) untracked(() => this.announce(fresh));
    });
  }

  protected refreshNow(): void {
    this.now.set(new Date());
  }

  protected ago(iso: string): string {
    return timeAgo(iso, this.now());
  }

  /** Mark it read (if needed) and go to the claim. Navigating matters more than the read flag. */
  protected async open(n: AppNotification): Promise<void> {
    if (!n.isRead) {
      try {
        await this.commands.markRead(n.id);
      } catch {
        // Still take the user to the claim; it just stays unread.
      }
      this.list.reload();
    }
    await this.router.navigate(claimUrlFor(this.auth.role(), n.claimId));
  }

  protected async markAllRead(): Promise<void> {
    try {
      await this.commands.markAllRead();
      this.list.reload();
    } catch {
      this.toast.error("Couldn't mark notifications as read. Try again.");
    }
  }

  private announce(fresh: AppNotification[]): void {
    this.sound.play();
    const [latest] = fresh;
    const text = fresh.length === 1 ? latest.message : `${fresh.length} new notifications`;
    this.toast
      .info(text, 'Open')
      .onAction()
      .subscribe(() => void this.open(latest));
  }
}
