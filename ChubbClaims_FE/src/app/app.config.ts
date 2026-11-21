import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { MAT_ICON_DEFAULT_OPTIONS } from '@angular/material/icon';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { authInterceptor } from './_interceptors/auth.interceptor';
import { problemInterceptor } from './_interceptors/problem.interceptor';
import { routes } from './app.routes';

/** Order matters: responses flow back through interceptors in reverse, see problem.interceptor. */
export const httpInterceptors = [problemInterceptor, authInterceptor];

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // Route params arrive as component input() signals (e.g. claim id).
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withFetch(), withInterceptors(httpInterceptors)),
    // index.html loads the Material Symbols font, not the older Material Icons one.
    { provide: MAT_ICON_DEFAULT_OPTIONS, useValue: { fontSet: 'material-symbols-outlined' } },
  ],
};
