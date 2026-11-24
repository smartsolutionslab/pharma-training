import { expect, test } from 'vitest';
import type { LocationQuery } from 'vue-router';
import { parseMyCoursesQuery, toLocationQuery } from './myCoursesQuery';

// My courses (#38) keeps only its page in the address, /learning?page=2, read with a Zod schema at the edge as the team
// matrix does: reload and Back keep the page, and whatever the address holds, the page asks the host only for a page it
// takes (1 to 21,474,837, GET /compliance/me/assignments in openapi.json). Anything else is the first page.

test('an address without a query is the first page', () => {
  expect(parseMyCoursesQuery({})).toEqual({ page: 1 });
});

test.each([
  [{ page: '2' }, { page: 2 }],
  [{ page: '3' }, { page: 3 }],
  [{ page: '21474837' }, { page: 21474837 }],
] satisfies [LocationQuery, unknown][])('the query %o is %o', (query, expected) => {
  expect(parseMyCoursesQuery(query)).toEqual(expected);
});

// The first page is the plain address /learning, so the page as it first shows has no query.
test.each([
  [{ page: 1 }, {}],
  [{ page: 2 }, { page: '2' }],
  [{ page: 3 }, { page: '3' }],
])('the query %o is the address %o', (query, address) => {
  expect(toLocationQuery(query)).toEqual(address);
});

test.each<LocationQuery>([{ page: '3' }, { page: '21474837' }, {}])('the address %o round-trips', (query) => {
  expect(toLocationQuery(parseMyCoursesQuery(query))).toEqual(query);
});

test.each<[LocationQuery[string]]>([
  ['0'],
  ['-1'],
  ['x'],
  ['1.5'],
  ['2e3'],
  [''],
  ['99999999'],
  ['21474838'],
  [null],
  [['2', '3']],
])('the page %o falls back to 1', (page) => {
  expect(parseMyCoursesQuery({ page })).toEqual({ page: 1 });
});

// Only the page belongs to the address: whatever else it holds (the matrix's filters, a stale parameter) is not read.
test('the address keeps nothing but the page', () => {
  expect(
    parseMyCoursesQuery({ page: '2', course: '0198c1a0-0000-7000-8000-0000000000c1', jobFunction: 'Pta' }),
  ).toEqual({ page: 2 });
});
