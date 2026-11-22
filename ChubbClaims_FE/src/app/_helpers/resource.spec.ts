import { httpResource, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Injector, runInInjectionContext, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { keepWhileLoading } from './resource';

describe('keepWhileLoading', () => {
  function setup() {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const page = signal(1);
    const resource = runInInjectionContext(TestBed.inject(Injector), () =>
      httpResource<{ page: number }>(() => `/api/list?page=${page()}`),
    );
    const kept = runInInjectionContext(TestBed.inject(Injector), () => keepWhileLoading(resource));
    const backend = TestBed.inject(HttpTestingController);
    const respond = async (url: string, body: { page: number } | null, status = 200) => {
      TestBed.tick();
      const request = backend.expectOne(url);
      if (status === 200) request.flush(body);
      else request.flush(null, { status, statusText: 'Error' });
      await new Promise((resolve) => setTimeout(resolve)); // the response is applied asynchronously
      TestBed.tick();
    };
    return { page, resource, kept, respond };
  }

  it('keeps showing the current page while the next one loads, then switches', async () => {
    const { page, kept, respond } = setup();
    await respond('/api/list?page=1', { page: 1 });
    expect(kept()).toEqual({ page: 1 });

    page.set(2);
    TestBed.tick(); // request for page 2 is now in flight
    expect(kept()).toEqual({ page: 1 }); // not blank

    await respond('/api/list?page=2', { page: 2 });
    expect(kept()).toEqual({ page: 2 });
  });

  it('an error clears it, so the error state shows instead of stale rows', async () => {
    const { page, kept, respond } = setup();
    await respond('/api/list?page=1', { page: 1 });

    page.set(2);
    await respond('/api/list?page=2', null, 500);
    expect(kept()).toBeUndefined();
  });
});
