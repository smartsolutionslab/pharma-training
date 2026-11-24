import { expect, test } from 'vitest';
import { ApiError } from '@/api/baseApi';
import { setLanguage } from '@/i18n';
import { messageText } from '@/i18n/errorText';
import { myCoursesFailure, myCoursesRetryable } from './myCoursesErrors';

// What my courses (#38) says when GET /compliance/me/assignments is refused or fails, as an ErrorMessage of
// src/i18n/errorText.ts (#147, ADR 0007), shaped like the team matrix's matrixFailure: the page's own words for
// pharmacy-not-found and identity-incomplete, else the catalog's errors.<code>; a module code the catalog does not know
// shows the host's own words; anything else, a network failure included, says the courses could not be loaded. The page
// offers to try again for every failure but those two final refusals.

/** Problem details as the host sends them, with a code where it has one. */
const problem = (status: number, code?: string, detail = 'Words of the host, not for the user.') =>
  JSON.stringify({ title: 'Refused', status, detail, code });

test.each([
  [404, 'compliance.pharmacy-not-found', 'learning.myCourses.pharmacyNotFound'],
  [403, 'compliance.identity-incomplete', 'learning.myCourses.identityIncomplete'],
  [400, 'invalid-argument', 'errors.invalid-argument'],
  [403, 'organisation.capability-missing', 'errors.organisation.capability-missing'],
])('%i %s says %s, not the host’s words', (status, code, key) => {
  const failure = myCoursesFailure(new ApiError(status, problem(status, code)));

  expect(failure).toEqual({ key, values: expect.any(Object) });
  expect(messageText(failure)).not.toContain('Words of the host');
});

test.each([
  ['de-DE', 'Sie sind nicht mehr Mitglied dieser Apotheke.'],
  ['en-GB', 'You are no longer a member of this pharmacy.'],
] as const)('pharmacy-not-found with the host’s words says the page’s text in %s', (language, text) => {
  setLanguage(language, { remember: false });

  const shown = messageText(
    myCoursesFailure(new ApiError(404, problem(404, 'compliance.pharmacy-not-found', 'SERVER WORDS'))),
  );

  expect(shown).toBe(text);
  expect(shown).not.toContain('SERVER WORDS');
});

test.each([
  [400, 'compliance.not-in-the-catalogs', 'Es wurde keine Apotheke angegeben.'],
  [409, 'compliance.something-new', 'Etwas Neues.'],
])('a module code the catalog does not know, %i %s, shows the host’s words', (status, code, detail) => {
  expect(myCoursesFailure(new ApiError(status, problem(status, code, detail)))).toEqual({ serverMessage: detail });
});

test.each([
  ['a bad request without a code', () => new ApiError(400, problem(400))],
  ['a failure of the host', () => new ApiError(500, '')],
  ['a failure of the host with a module code', () => new ApiError(500, problem(500, 'compliance.something-new'))],
  ["an edge's 502", () => new ApiError(502, 'Bad Gateway')],
  ['no token', () => new ApiError(401, '')],
  ['the network', () => new TypeError('Failed to fetch')],
  ['anything else', () => new Error('Something broke.')],
])('%s says the courses could not be loaded', (_name, failure) => {
  expect(myCoursesFailure(failure())).toMatchObject({ key: 'learning.myCourses.loadFailed' });
});

// Reading again helps unless the caller is no member any more or their account names no person.
test.each([
  ['pharmacy-not-found', false, () => new ApiError(404, problem(404, 'compliance.pharmacy-not-found'))],
  ['identity-incomplete', false, () => new ApiError(403, problem(403, 'compliance.identity-incomplete'))],
  ['pharmacy-missing', true, () => new ApiError(400, problem(400, 'compliance.pharmacy-missing'))],
  ['invalid-argument', true, () => new ApiError(400, problem(400, 'invalid-argument'))],
  ["another module's code", true, () => new ApiError(403, problem(403, 'organisation.capability-missing'))],
  ['a code the catalog does not know', true, () => new ApiError(409, problem(409, 'compliance.something-new'))],
  ['a failure of the host', true, () => new ApiError(500, '')],
  ['a bad request without a code', true, () => new ApiError(400, problem(400))],
  ['the network', true, () => new TypeError('Failed to fetch')],
])('%s is worth reading again: %s', (_name, retryable, failure) => {
  expect(myCoursesRetryable(failure())).toBe(retryable);
});
