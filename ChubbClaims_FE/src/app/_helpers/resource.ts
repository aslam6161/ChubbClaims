import { linkedSignal, type Resource, type Signal } from '@angular/core';

/**
 * A resource's value, or `fallback` while it is loading or has failed.
 *
 * Use this instead of `resource.value()` in screens: in Angular 22 `value()` THROWS while the
 * resource is in an error state, which would crash the template instead of letting PageState
 * show the error and a retry button.
 */
export function valueOr<T, F>(resource: Resource<T | undefined>, fallback: F): T | F {
  return resource.hasValue() ? (resource.value() as T) : fallback;
}

/**
 * The resource's value, but while a NEW request is loading (next page, new search) it keeps
 * showing the previous one instead of going blank: an httpResource drops its value when its
 * request changes, which would empty the table and make the page jump. Errors still clear it.
 */
export function keepWhileLoading<T>(resource: Resource<T | undefined>): Signal<T | undefined> {
  return linkedSignal<{ value: T | undefined; loading: boolean }, T | undefined>({
    source: () => ({ value: valueOr(resource, undefined), loading: resource.isLoading() }),
    computation: (now, previous) => now.value ?? (now.loading ? previous?.value : undefined),
  });
}
