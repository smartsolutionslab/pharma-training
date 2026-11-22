import { expect, test } from 'vitest';
import type { LocationQuery } from 'vue-router';
import { parseInboxQuery } from './inboxQuery';

// The inbox keeps its state in the address, ?status=Draft|InReview|Published&page=n, read with a Zod schema at the edge:
// whatever the address holds, the page asks the host for one of the inbox's three statuses and a page the host takes.
// Each part that is missing or invalid falls back to its default on its own, Draft and 1.

test('an address without a query is the first page of the drafts', () => {
  expect(parseInboxQuery({})).toEqual({ status: 'Draft', page: 1 });
});

test.each([
  [{ status: 'Draft' }, { status: 'Draft', page: 1 }],
  [{ status: 'InReview' }, { status: 'InReview', page: 1 }],
  [{ status: 'Published' }, { status: 'Published', page: 1 }],
  [
    { status: 'Published', page: '3' },
    { status: 'Published', page: 3 },
  ],
  [{ page: '2' }, { status: 'Draft', page: 2 }],
  [
    { status: 'InReview', page: '21474837' },
    { status: 'InReview', page: 21474837 },
  ],
] satisfies [LocationQuery, unknown][])('the query %o is %o', (query, expected) => {
  expect(parseInboxQuery(query)).toEqual(expected);
});

// The inbox names three statuses; Superseded and Retired versions are not in it. A comma list, a repeated parameter or
// an unknown value would get a 4xx from the host: it never reaches it.
test.each<[LocationQuery[string]]>([
  ['Bogus'],
  ['Superseded'],
  ['Retired'],
  ['inreview'],
  ['InReview,Published'],
  [''],
  [null],
  [['InReview', 'Published']],
])('the status %o falls back to Draft', (status) => {
  expect(parseInboxQuery({ status })).toEqual({ status: 'Draft', page: 1 });
});

// The host takes a page from 1 to 21,474,837 (GET /editorial/course-versions in openapi.json), a whole number.
test.each<[LocationQuery[string]]>([
  ['0'],
  ['-1'],
  ['abc'],
  ['1.5'],
  ['2e3'],
  [''],
  ['21474838'],
  [null],
  [['2', '3']],
])('the page %o falls back to 1', (page) => {
  expect(parseInboxQuery({ status: 'Published', page })).toEqual({ status: 'Published', page: 1 });
});

test('an invalid status keeps a valid page, and an invalid page a valid status', () => {
  expect(parseInboxQuery({ status: 'Bogus', page: '2' })).toEqual({ status: 'Draft', page: 2 });
  expect(parseInboxQuery({ status: 'InReview', page: 'x' })).toEqual({ status: 'InReview', page: 1 });
});
