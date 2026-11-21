import { HttpErrorResponse } from '@angular/common/http';

/**
 * Every failed API call becomes an AppError (see problem.interceptor), so screens only deal with
 * one error shape: a status, a plain-words message, and per-field messages for forms.
 * The backend returns RFC 9457 ProblemDetails: { status, title, detail, errors?, correlationId }.
 */
export class AppError extends Error {
  constructor(
    readonly status: number,
    readonly title: string,
    readonly detail: string | null,
    /** Field name (camelCase, as in the request) → messages. From 400 ValidationProblemDetails. */
    readonly fieldErrors: Readonly<Record<string, readonly string[]>> = {},
    /** Quote this to support: it ties the error to the server logs. */
    readonly correlationId: string | null = null,
  ) {
    super(detail ?? title);
    this.name = 'AppError';
  }

  /** What to show the user: the server's explanation when there is one, otherwise the title. */
  get userMessage(): string {
    return this.detail ?? this.title;
  }

  static from(error: unknown): AppError {
    if (error instanceof AppError) return error;

    if (error instanceof HttpErrorResponse) {
      if (error.status === 0) {
        return new AppError(0, 'Cannot reach the server', 'Check your connection and try again.');
      }
      const body = isRecord(error.error) ? error.error : {};
      return new AppError(
        error.status,
        stringOr(body['title'], error.statusText || 'Request failed'),
        stringOr(body['detail'], null),
        fieldErrors(body['errors']),
        stringOr(body['correlationId'], null),
      );
    }

    return new AppError(-1, 'Something went wrong', error instanceof Error ? error.message : null);
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function stringOr<T extends string | null>(value: unknown, fallback: T): string | T {
  return typeof value === 'string' && value.length > 0 ? value : fallback;
}

/** ASP.NET keys errors by property path ("Description", "Motor.VehicleRegistration"); normalise to camelCase. */
function fieldErrors(value: unknown): Record<string, string[]> {
  if (!isRecord(value)) return {};
  const result: Record<string, string[]> = {};
  for (const [key, messages] of Object.entries(value)) {
    if (!Array.isArray(messages)) continue;
    const name = key
      .replace(/^\$\./, '')
      .split('.')
      .map((part) => part.charAt(0).toLowerCase() + part.slice(1))
      .join('.');
    result[name] = messages.filter((m): m is string => typeof m === 'string');
  }
  return result;
}
