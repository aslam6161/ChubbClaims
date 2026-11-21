import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { AppError } from '../../_models/app-error';

/**
 * The four states every data screen has (FRONTEND.md §7): loading, error, empty, data.
 * Content is projected only when there is data, so no screen is ever blank.
 */
@Component({
  selector: 'app-page-state',
  imports: [MatButtonModule, MatIconModule, MatProgressBarModule],
  templateUrl: './page-state.html',
  styleUrl: './page-state.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PageState {
  readonly loading = input(false);
  readonly error = input<unknown>(undefined);
  readonly empty = input(false);
  readonly emptyMessage = input('Nothing here yet.');
  readonly retry = output();

  protected readonly appError = computed(() => (this.error() ? AppError.from(this.error()) : null));
}
