import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';

/** Waits for a pause in typing before searching, so each keystroke isn't a request. */
export const SEARCH_DEBOUNCE_MS = 300;

/** A search field that emits the trimmed text after a short pause (or at once when cleared). */
@Component({
  selector: 'app-search-box',
  imports: [MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule],
  template: `
    <mat-form-field appearance="outline" subscriptSizing="dynamic" class="search">
      <mat-icon matPrefix>search</mat-icon>
      <mat-label>{{ label() }}</mat-label>
      <input
        matInput
        type="search"
        maxlength="100"
        [value]="text()"
        (input)="onInput($any($event.target).value)"
        (keydown.escape)="clear()"
      />
      @if (text()) {
        <button mat-icon-button matSuffix type="button" aria-label="Clear search" (click)="clear()">
          <mat-icon>close</mat-icon>
        </button>
      }
    </mat-form-field>
  `,
  styles: `
    .search {
      width: 100%;
      max-width: 26rem;
      /* Slimmer than the default 56 px field (Material density -3); the label still floats. */
      --mat-form-field-container-height: 44px;
      --mat-form-field-container-vertical-padding: 10px;
      /* The clear button would otherwise stretch the field back up while there is text. */
      --mat-icon-button-state-layer-size: 36px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SearchBox {
  readonly label = input('Search');
  readonly search = output<string>();

  protected readonly text = signal('');
  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
  }

  protected onInput(value: string): void {
    this.text.set(value);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.search.emit(value.trim()), SEARCH_DEBOUNCE_MS);
  }

  protected clear(): void {
    clearTimeout(this.timer);
    this.text.set('');
    this.search.emit('');
  }
}
