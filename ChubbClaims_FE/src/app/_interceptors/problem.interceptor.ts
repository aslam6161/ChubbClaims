import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { AppError } from '../_models/app-error';

/**
 * Turns every HTTP failure into an AppError (status, plain-words message, field errors), so
 * screens never parse ProblemDetails themselves. Registered FIRST (outermost): responses pass
 * through interceptors in reverse order, so the auth interceptor's 401 handling runs before this
 * and still sees the raw HttpErrorResponse.
 */
export const problemInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(
    catchError((error: unknown) =>
      throwError(() => (error instanceof HttpErrorResponse ? AppError.from(error) : error)),
    ),
  );
