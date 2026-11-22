import { TestBed } from '@angular/core/testing';
import { SearchBox, SEARCH_DEBOUNCE_MS } from '../_shared/search-box/search-box';
import { createListQuery } from './paging';

describe('createListQuery', () => {
  it('starts at page 1 and only sends search when there is some', () => {
    const query = createListQuery();
    expect(query.params()).toEqual({ page: 1, pageSize: 20 });

    query.setSearch('ampang');
    expect(query.params()).toEqual({ page: 1, pageSize: 20, search: 'ampang' });
  });

  it('a new search goes back to page 1 (page 3 of old results means nothing)', () => {
    const query = createListQuery();
    query.onPage({ pageIndex: 2, pageSize: 20, length: 100 });
    expect(query.page()).toBe(3);

    query.setSearch('ampang');
    expect(query.page()).toBe(1);
  });

  it('the same search again keeps the page', () => {
    const query = createListQuery();
    query.setSearch('ampang');
    query.onPage({ pageIndex: 1, pageSize: 20, length: 100 });

    query.setSearch('ampang');
    expect(query.page()).toBe(2);
  });

  it('a new page size goes back to page 1', () => {
    const query = createListQuery();
    query.onPage({ pageIndex: 3, pageSize: 20, length: 100 });

    query.onPage({ pageIndex: 3, pageSize: 50, length: 100 });
    expect([query.page(), query.pageSize()]).toEqual([1, 50]);
  });
});

describe('SearchBox', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  function render() {
    const fixture = TestBed.createComponent(SearchBox);
    const emitted: string[] = [];
    fixture.componentInstance.search.subscribe((text) => emitted.push(text));
    fixture.detectChanges();
    const input = (fixture.nativeElement as HTMLElement).querySelector('input')!;
    const type = (text: string) => {
      input.value = text;
      input.dispatchEvent(new Event('input'));
    };
    return { fixture, emitted, type };
  }

  it('waits for a pause in typing, then emits the trimmed text once', () => {
    const { emitted, type } = render();

    type('a');
    type('amp');
    type(' ampang ');
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS - 1);
    expect(emitted).toEqual([]);

    vi.advanceTimersByTime(1);
    expect(emitted).toEqual(['ampang']);
  });

  it('clearing searches for everything at once', () => {
    const { fixture, emitted, type } = render();
    type('ampang');
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('button[aria-label="Clear search"]')!
      .click();
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);

    expect(emitted).toEqual(['']); // the pending "ampang" never went out
  });
});
