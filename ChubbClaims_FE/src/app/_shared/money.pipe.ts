import { Pipe, PipeTransform } from '@angular/core';

/**
 * Formats an amount in its own currency ("MYR 5,800.00", "IDR 20,000,000.00").
 * Every amount in this app belongs to one claim or one currency row: amounts in different
 * currencies are never added together (no FX in scope).
 */
@Pipe({ name: 'money' })
export class MoneyPipe implements PipeTransform {
  transform(amount: number | null | undefined, currency: string): string {
    if (amount === null || amount === undefined) return '—';
    try {
      return new Intl.NumberFormat('en', {
        style: 'currency',
        currency,
        currencyDisplay: 'code',
        // Always 2 decimals, like the API (decimal(18,2)). Left to Intl, the minor units come from
        // the runtime's ICU data, which differs: Chromium printed "IDR 20,000,000.00" while
        // Node printed "IDR 20,000,000" for the same call.
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
      }).format(amount);
    } catch {
      return `${currency} ${amount.toFixed(2)}`; // unknown currency code
    }
  }
}
