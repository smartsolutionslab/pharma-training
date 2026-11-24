import { expect, test } from 'vitest';
import type { LocationQuery } from 'vue-router';
import { parseMatrixQuery, toLocationQuery } from './matrixQuery';

// The team matrix (#36) keeps its filters and its page in the address, /compliance?course=<uuid>&jobFunction=Pta&page=2,
// read with a Zod schema at the edge as the editorial inbox does: reload, Back and a shared link keep what the manager
// looked at, and whatever the address holds, the page asks the host only for what it takes. Each part that is missing
// or invalid falls back on its own: no course, no job function, the first page.

const hygiene = '0198c1a0-0000-7000-8000-0000000000c1';

test('an address without a query is the first page, with no course and no job function', () => {
  expect(parseMatrixQuery({})).toEqual({ page: 1 });
});

test.each([
  [{ course: hygiene }, { course: hygiene, page: 1 }],
  [{ jobFunction: 'Pta' }, { jobFunction: 'Pta', page: 1 }],
  [{ page: '2' }, { page: 2 }],
  [
    { course: hygiene, jobFunction: 'Trainee', page: '3' },
    { course: hygiene, jobFunction: 'Trainee', page: 3 },
  ],
  [{ page: '21474837' }, { page: 21474837 }],
] satisfies [LocationQuery, unknown][])('the query %o is %o', (query, expected) => {
  expect(parseMatrixQuery(query)).toEqual(expected);
});

test.each(['Pharmacist', 'Pta', 'Pka', 'Trainee', 'Other'])(
  'the job function %s is read by its name',
  (jobFunction) => {
    expect(parseMatrixQuery({ jobFunction })).toEqual({ jobFunction, page: 1 });
  },
);

// A valid query goes back into the address as it came, without the first page and without a filter that is not set,
// so the plain address /compliance is the matrix as it first shows.
test.each([
  [{ page: 1 }, {}],
  [{ page: 2 }, { page: '2' }],
  [{ course: hygiene, page: 1 }, { course: hygiene }],
  [{ jobFunction: 'Pka' as const, page: 1 }, { jobFunction: 'Pka' }],
  [
    { course: hygiene, jobFunction: 'Pta' as const, page: 4 },
    { course: hygiene, jobFunction: 'Pta', page: '4' },
  ],
])('the query %o is the address %o', (query, address) => {
  expect(toLocationQuery(query)).toEqual(address);
});

test.each<LocationQuery>([
  { course: hygiene, jobFunction: 'Pta', page: '2' },
  { jobFunction: 'Other' },
  { course: hygiene },
  { page: '7' },
  {},
])('the address %o round-trips', (query) => {
  expect(toLocationQuery(parseMatrixQuery(query))).toEqual(query);
});

// The course is the page's own filter, by the course's id: anything that is not a uuid is no course.
test.each<[LocationQuery[string]]>([['abc'], [''], [null], ['0198c1a0-0000-7000-8000'], [[hygiene, hygiene]]])(
  'the course %o falls back to none',
  (course) => {
    expect(parseMatrixQuery({ course })).toEqual({ page: 1 });
  },
);

// The host takes a job function by its name (ComplianceJobFunction): another name, a number or a list would get a 400.
test.each<[LocationQuery[string]]>([['Chef'], ['pta'], ['1'], [''], [null], [['Pta', 'Pka']]])(
  'the job function %o falls back to none',
  (jobFunction) => {
    expect(parseMatrixQuery({ jobFunction })).toEqual({ page: 1 });
  },
);

// The host takes a page from 1 to 21,474,837 (GET /compliance/pharmacies/{id}/matrix in openapi.json), a whole number.
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
  expect(parseMatrixQuery({ page })).toEqual({ page: 1 });
});

test('each invalid part falls back on its own and keeps the valid ones', () => {
  expect(parseMatrixQuery({ course: 'abc', jobFunction: 'Pta', page: '2' })).toEqual({ jobFunction: 'Pta', page: 2 });
  expect(parseMatrixQuery({ course: hygiene, jobFunction: 'Chef', page: '2' })).toEqual({ course: hygiene, page: 2 });
  expect(parseMatrixQuery({ course: hygiene, jobFunction: 'Pta', page: '0' })).toEqual({
    course: hygiene,
    jobFunction: 'Pta',
    page: 1,
  });
  expect(parseMatrixQuery({ course: hygiene, jobFunction: 'Pta', page: '99999999' })).toEqual({
    course: hygiene,
    jobFunction: 'Pta',
    page: 1,
  });
});
