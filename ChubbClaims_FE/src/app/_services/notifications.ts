import { HttpClient } from '@angular/common/http';
import { inject, Service } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** Notification commands. The list itself is an httpResource in the bell. */
@Service()
export class Notifications {
  private readonly http = inject(HttpClient);

  markRead(id: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`/api/notifications/${id}/read`, null));
  }

  markAllRead(): Promise<void> {
    return firstValueFrom(this.http.post<void>('/api/notifications/read-all', null));
  }
}
