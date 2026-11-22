import { inject, Service } from '@angular/core';
import { MatSnackBar, MatSnackBarRef, TextOnlySnackBar } from '@angular/material/snack-bar';

export type ToastKind = 'success' | 'info' | 'warning' | 'error';

/** How long each kind stays: problems stay longer so there's time to read them. */
const DURATION_MS: Record<ToastKind, number> = {
  success: 4000,
  info: 8000,
  warning: 7000,
  error: 8000,
};

/**
 * Short pop-up messages, coloured and iconed by meaning (styles.css, `.toast--*`), so success,
 * a conflict and a failure don't all look the same. The icon means colour isn't the only signal.
 *  - success: the server accepted what you did
 *  - info:    something happened that you may want to open (a new notification)
 *  - warning: nothing broke, but the data changed under you (409); the screen was refreshed
 *  - error:   it didn't work; try again
 */
@Service()
export class Toast {
  private readonly snackBar = inject(MatSnackBar);

  success(message: string): MatSnackBarRef<TextOnlySnackBar> {
    return this.show('success', message);
  }

  info(message: string, action = 'OK'): MatSnackBarRef<TextOnlySnackBar> {
    return this.show('info', message, action);
  }

  warning(message: string): MatSnackBarRef<TextOnlySnackBar> {
    return this.show('warning', message);
  }

  error(message: string): MatSnackBarRef<TextOnlySnackBar> {
    return this.show('error', message);
  }

  private show(kind: ToastKind, message: string, action = 'OK'): MatSnackBarRef<TextOnlySnackBar> {
    return this.snackBar.open(message, action, {
      duration: DURATION_MS[kind],
      panelClass: ['toast', `toast--${kind}`],
      // Screen readers interrupt for problems, and wait their turn for good news.
      politeness: kind === 'warning' || kind === 'error' ? 'assertive' : 'polite',
    });
  }
}
