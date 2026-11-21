import type { Resource } from '@angular/core';

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
