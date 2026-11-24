import { expect, test } from 'vitest';
import { coursePath } from './coursePath';

// Starting and continuing a course (#38) are links to the chapter player (#44, PR #262) on the version Compliance names
// (startCourseVersionId, ADR 0024). The address is built in this one place, so the link and the player's route cannot
// drift apart.

test('a course version opens in the player at /learning/courses/<id>', () => {
  expect(coursePath('0198c1a0-0000-7000-8000-0000000000a1')).toBe(
    '/learning/courses/0198c1a0-0000-7000-8000-0000000000a1',
  );
});

test('each version has an address of its own', () => {
  expect(coursePath('0198c1a0-0000-7000-8000-0000000000b2')).toBe(
    '/learning/courses/0198c1a0-0000-7000-8000-0000000000b2',
  );
});
