import { signal } from '@angular/core';
import type { PageEvent } from '@angular/material/paginator';

export const PAGE_SIZES = [10, 20, 50] as const;

/**
 * Search text plus page for a server-paged list. Changing the search or the page size goes back
 * to page 1 (page 3 of the old results means nothing for the new ones).
 */
export function createListQuery(pageSize = 20) {
  const search = signal('');
  const page = signal(1); // 1-based, like the API
  const size = signal(pageSize);

  return {
    search: search.asReadonly(),
    page: page.asReadonly(),
    pageSize: size.asReadonly(),
    setSearch(text: string): void {
      if (text === search()) return;
      search.set(text);
      page.set(1);
    },
    onPage(event: PageEvent): void {
      if (event.pageSize !== size()) {
        size.set(event.pageSize);
        page.set(1);
      } else {
        page.set(event.pageIndex + 1);
      }
    },
    resetPage(): void {
      page.set(1);
    },
    /** Query parameters for the API (search only when there is some). */
    params(): Record<string, string | number> {
      return { page: page(), pageSize: size(), ...(search() ? { search: search() } : {}) };
    },
  };
}

export type ListQuery = ReturnType<typeof createListQuery>;
