import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // Route params arrive as component input() signals (e.g. claim id).
    provideRouter(routes, withComponentInputBinding()),
    // Interceptors (auth, ProblemDetails) are added in phase 2.
    provideHttpClient(withFetch(), withInterceptors([])),
  ],
};
