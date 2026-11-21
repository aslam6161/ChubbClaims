import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthStore } from '../_services/auth-store';

/** Only our own API gets the token: never attach it to a third-party URL. */
const API_PREFIXES = ['/api/', '/hubs/'];

/**
 * Adds `Authorization: Bearer <token>` to API calls. A 401 means the token is missing, expired or
 * rejected: sign out and go to the login page instead of leaving the user on a broken screen.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthStore);
  const router = inject(Router);

  const isApi = API_PREFIXES.some((prefix) => req.url.startsWith(prefix));
  const token = auth.token();
  const request =
    isApi && token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(request).pipe(
    catchError((error: unknown) => {
      if (isApi && error instanceof HttpErrorResponse && error.status === 401 && auth.token()) {
        auth.signOut();
        void router.navigate(['/login'], { queryParams: { expired: true } });
      }
      return throwError(() => error);
    }),
  );
};
