import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AppError } from '../_models/app-error';
import { AuthStore } from '../_services/auth-store';
import { loginResponse } from '../_testing';
import { httpInterceptors } from '../app.config';

describe('HTTP interceptors (registered in app order)', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let auth: AuthStore;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors(httpInterceptors)),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthStore);
  });

  afterEach(() => backend.verify());

  /** Starts the request, answers it with an error, and returns what the caller receives. */
  async function failWith(url: string, status: number, body: object | null): Promise<unknown> {
    const result = firstValueFrom(http.get(url)).catch((e: unknown) => e);
    backend.expectOne(url).flush(body, { status, statusText: 'Error' });
    return result;
  }

  it('adds the bearer token to API calls only', async () => {
    auth.signIn(loginResponse('Claimant'));

    const api = firstValueFrom(http.get('/api/claims/mine'));
    const external = firstValueFrom(http.get('https://example.com/data'));

    const apiReq = backend.expectOne('/api/claims/mine');
    const externalReq = backend.expectOne('https://example.com/data');
    expect(apiReq.request.headers.get('Authorization')).toBe('Bearer token-Claimant');
    expect(externalReq.request.headers.has('Authorization')).toBe(false);
    apiReq.flush([]);
    externalReq.flush([]);
    await Promise.all([api, external]);
  });

  it('on 401 signs out and goes to login (so the auth interceptor saw the raw error: order is right)', async () => {
    auth.signIn(loginResponse('ClaimsOfficer'));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    const error = await failWith('/api/work-queue', 401, null);

    expect(auth.user()).toBeNull();
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { expired: true } });
    expect(error).toBeInstanceOf(AppError);
  });

  it('maps a 409 ProblemDetails to an AppError with the server explanation', async () => {
    const error = (await failWith('/api/claims/1/pick-up', 409, {
      status: 409,
      title: 'Claim was changed by someone else',
      detail: 'This claim was changed by someone else. Reload it and try again.',
      correlationId: 'abc123',
    })) as AppError;

    expect(error.status).toBe(409);
    expect(error.userMessage).toBe(
      'This claim was changed by someone else. Reload it and try again.',
    );
    expect(error.correlationId).toBe('abc123');
  });

  it('maps 400 validation errors to camelCase field errors', async () => {
    const error = (await failWith('/api/claims', 400, {
      title: 'One or more validation errors occurred.',
      errors: {
        IncidentDate: ['The IncidentDate field is required.'],
        '$.estimatedLossAmount': ['Not a number.'],
      },
    })) as AppError;

    expect(error.fieldErrors).toEqual({
      incidentDate: ['The IncidentDate field is required.'],
      estimatedLossAmount: ['Not a number.'],
    });
  });

  it('explains a network failure in plain words', async () => {
    const result = firstValueFrom(http.get('/api/me')).catch((e: unknown) => e);
    backend.expectOne('/api/me').error(new ProgressEvent('error'), { status: 0 });

    expect(((await result) as AppError).title).toBe('Cannot reach the server');
  });
});
