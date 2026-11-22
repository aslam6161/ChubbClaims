import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Toast, ToastKind } from './toast';

describe('Toast', () => {
  it.each<[ToastKind, string]>([
    ['success', 'polite'],
    ['info', 'polite'],
    ['warning', 'assertive'],
    ['error', 'assertive'],
  ])('%s gets its own style, and screen readers hear it %s', (kind, politeness) => {
    const open = vi.fn();
    TestBed.configureTestingModule({ providers: [{ provide: MatSnackBar, useValue: { open } }] });

    TestBed.inject(Toast)[kind]('Message');

    expect(open).toHaveBeenCalledWith(
      'Message',
      'OK',
      expect.objectContaining({ panelClass: ['toast', `toast--${kind}`], politeness }),
    );
  });

  it('info can carry its own action (e.g. "Open" on a new notification)', () => {
    const open = vi.fn();
    TestBed.configureTestingModule({ providers: [{ provide: MatSnackBar, useValue: { open } }] });

    TestBed.inject(Toast).info('New claim', 'Open');

    expect(open).toHaveBeenCalledWith('New claim', 'Open', expect.anything());
  });
});
