import { expect, test } from 'vitest';
import { startsWithKey } from './queryKeys';

// Whether a cached query's key falls under a generated key function's key (#426): compared element by element, an
// object by its contents, so a query is found by the generated key itself and never by a position in it.

test('a key falls under its own prefix and under itself', () => {
  expect(
    startsWithKey(
      ['editorial', 'course-versions', 'a', 'returns', { page: 2 }],
      ['editorial', 'course-versions', 'a', 'returns'],
    ),
  ).toBe(true);
  expect(startsWithKey(['editorial', 'course-versions', 'a'], ['editorial', 'course-versions', 'a'])).toBe(true);
});

test("another id's key does not, nor a shorter key", () => {
  expect(
    startsWithKey(['editorial', 'course-versions', 'b', 'returns'], ['editorial', 'course-versions', 'a', 'returns']),
  ).toBe(false);
  expect(startsWithKey(['editorial', 'course-versions'], ['editorial', 'course-versions', 'a'])).toBe(false);
});

test('an object in the prefix is compared by its contents, whatever the order of its fields', () => {
  expect(startsWithKey(['x', { page: 1, pageSize: 20 }], ['x', { pageSize: 20, page: 1 }])).toBe(true);
  expect(startsWithKey(['x', { page: 1 }], ['x', { page: 2 }])).toBe(false);
});

test('a missing key falls under nothing', () => {
  expect(startsWithKey(undefined, ['x'])).toBe(false);
});
