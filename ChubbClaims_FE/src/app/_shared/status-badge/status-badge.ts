import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { statusLabel, statusTone } from '../../_helpers/claim-status';
import type { ClaimStatus } from '../../_models/api-types';

@Component({
  selector: 'app-status-badge',
  template: '<span class="badge" [attr.data-tone]="tone()">{{ label() }}</span>',
  styleUrl: './status-badge.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatusBadge {
  readonly status = input.required<ClaimStatus>();

  protected readonly label = computed(() => statusLabel(this.status()));
  protected readonly tone = computed(() => statusTone(this.status()));
}
