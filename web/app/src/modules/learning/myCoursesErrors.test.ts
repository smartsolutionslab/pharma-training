import { expect, test } from 'vitest';
import { ApiError } from '@/api/baseApi';
import { myCoursesFailure } from './myCoursesErrors';

// What my courses (#38) says when GET /compliance/me/assignments is refused or fails, shaped like the team matrix's
// matrixFailure: a code the page has words for gives its catalog key; another of Compliance's own codes shows the host's
// own words (ApiError.serverMessage, ADR 0007); anything else, a network failure included, says the courses could not
// be loaded, and the page offers to try again.

/** Problem details as the host sends them, with a code where it has one. */
const problem = (status: number, code?: string, detail = 'Words of the host, not for the user.') =>
  JSON.stringify({ title: 'Refused', status, detail, code });

test.each([
  [404, 'compliance.pharmacy-not-found', 'learning.myCourses.pharmacyNotFound'],
  [403, 'compliance.identity-incomplete', 'learning.myCourses.identityIncomplete'],
])('%i %s says %s, not the host’s words', (status, code, key) => {
  expect(myCoursesFailure(new ApiError(status, problem(status, code)))).toEqual({ key });
});

test.each([
  [400, 'compliance.pharmacy-missing', 'Es wurde keine Apotheke angegeben.'],
  [409, 'compliance.something-new', 'Etwas Neues.'],
])('another of Compliance’s own refusals, %i %s, shows the host’s words', (status, code, detail) => {
  expect(myCoursesFailure(new ApiError(status, problem(status, code, detail)))).toEqual({ message: detail });
});

test.each([
  ['a bad request without a code', () => new ApiError(400, problem(400))],
  ['a failed guard', () => new ApiError(400, problem(400, 'invalid-argument'))],
  ['a failure of the host', () => new ApiError(500, '')],
  ['a failure of the host with a module code', () => new ApiError(500, problem(500, 'compliance.pharmacy-not-found'))],
  ['another module’s refusal', () => new ApiError(403, problem(403, 'organisation.x'))],
  ['no token', () => new ApiError(401, '')],
  ['the network', () => new TypeError('Failed to fetch')],
  ['anything else', () => new Error('Something broke.')],
])('%s says the courses could not be loaded', (_name, failure) => {
  expect(myCoursesFailure(failure())).toEqual({ key: 'learning.myCourses.loadFailed' });
});
