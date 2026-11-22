import { Service, signal } from '@angular/core';

const STORAGE_KEY = 'chubbclaims.sound';

/**
 * A short two-tone chime for new notifications, generated with the Web Audio API (no audio file).
 * Browsers only allow sound after the user has interacted with the page; signing in is a click,
 * so it works from then on. After a full page reload it starts working at the user's first click.
 * The on/off choice is a per-browser preference, so it lives in localStorage.
 */
@Service()
export class NotificationSound {
  readonly enabled = signal(readPreference());
  private context: AudioContext | null = null;

  toggle(): void {
    this.enabled.update((on) => !on);
    try {
      localStorage.setItem(STORAGE_KEY, this.enabled() ? 'on' : 'off');
    } catch {
      // Storage blocked (private mode): the choice lasts until the page is closed.
    }
  }

  play(): void {
    if (!this.enabled() || typeof AudioContext === 'undefined') return;
    try {
      const ctx = (this.context ??= new AudioContext());
      if (ctx.state === 'suspended') void ctx.resume().catch(() => undefined);
      const t = ctx.currentTime;
      // Two soft sine notes (A5 then E6), each fading in and out quickly.
      for (const [frequency, start] of [
        [880, 0],
        [1318.5, 0.13],
      ] as const) {
        const tone = ctx.createOscillator();
        const volume = ctx.createGain();
        tone.type = 'sine';
        tone.frequency.value = frequency;
        volume.gain.setValueAtTime(0.0001, t + start);
        volume.gain.exponentialRampToValueAtTime(0.18, t + start + 0.02);
        volume.gain.exponentialRampToValueAtTime(0.0001, t + start + 0.45);
        tone.connect(volume).connect(ctx.destination);
        tone.start(t + start);
        tone.stop(t + start + 0.5);
      }
    } catch {
      // No audio device or the browser refused: notifications still show, just silently.
    }
  }
}

function readPreference(): boolean {
  try {
    return localStorage.getItem(STORAGE_KEY) !== 'off';
  } catch {
    return true;
  }
}
