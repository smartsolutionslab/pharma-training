import { expect, test } from 'vitest';
import { ApiError } from '@/api/baseApi';
import { setLanguage } from '@/i18n';
import { messageText } from '@/i18n/errorText';
import { matrixFailure, matrixRetryable } from './matrixErrors';

// What the team matrix (#36) says when the host refuses it or fails, as an ErrorMessage of src/i18n/errorText.ts
// (#147): the page's own words for capability-missing, else the catalog's errors.<code>; a module code the catalog does
// not know shows the host's own words; anything else, a network failure included, says the matrix could not be loaded,
// and the page offers to try again.

/** Problem details as the host sends them, with a code where it has one. */
const problem = (status: number, code?: string, detail = 'Words of the host, not for the user.') =>
  JSON.stringify({ title: 'Refused', status, detail, code });

test.each([
  [403, 'compliance.capability-missing', 'compliance.matrix.notAllowed'],
  [404, 'compliance.pharmacy-not-found', 'errors.compliance.pharmacy-not-found'],
  [403, 'compliance.identity-incomplete', 'errors.compliance.identity-incomplete'],
  [403, 'compliance.own-assignment', 'errors.compliance.own-assignment'],
  [400, 'compliance.invalid-job-function', 'errors.compliance.invalid-job-function'],
  [400, 'invalid-argument', 'errors.invalid-argument'],
  [403, 'organisation.capability-missing', 'errors.organisation.capability-missing'],
])('%i %s says %s, not the host’s words', (status, code, key) => {
  const failure = matrixFailure(new ApiError(status, problem(status, code)));

  expect(failure).toEqual({ key, values: expect.any(Object) });
  expect(messageText(failure)).not.toContain('Words of the host');
});

test.each([
  ['de-DE', 'Ihre eigene Zuweisung kann nur ein anderer Apothekenleiter ändern oder zurückziehen.'],
  ['en-GB', 'Only another pharmacy manager can change or withdraw your own assignment.'],
] as const)('own-assignment with the host words says the catalog text in %s', (language, text) => {
  setLanguage(language, { remember: false });

  const shown = messageText(
    matrixFailure(new ApiError(403, problem(403, 'compliance.own-assignment', 'SERVER WORDS'))),
  );

  expect(shown).toBe(text);
  expect(shown).not.toContain('SERVER WORDS');
});

test('a module code the catalog does not know shows the host’s words', () => {
  expect(matrixFailure(new ApiError(409, problem(409, 'compliance.something-new', 'Etwas Neues.')))).toEqual({
    serverMessage: 'Etwas Neues.',
  });
});

test.each([
  ['a failure of the host', () => new ApiError(500, '')],
  ['a failure of the host with a module code', () => new ApiError(500, problem(500, 'compliance.something-new'))],
  ["an edge's 502", () => new ApiError(502, 'Bad Gateway')],
  ['a bad request without a code', () => new ApiError(400, problem(400))],
  ['no token', () => new ApiError(401, '')],
  ['the network', () => new TypeError('Failed to fetch')],
  ['anything else', () => new Error('Something broke.')],
])('%s says the matrix could not be loaded', (_name, failure) => {
  expect(matrixFailure(failure())).toMatchObject({ key: 'compliance.matrix.loadFailed' });
});

// Reading again helps unless the member may not see the matrix or is no member any more (#147).
test.each([
  ['capability-missing', false, () => new ApiError(403, problem(403, 'compliance.capability-missing'))],
  ['pharmacy-not-found', false, () => new ApiError(404, problem(404, 'compliance.pharmacy-not-found'))],
  ['identity-incomplete', true, () => new ApiError(403, problem(403, 'compliance.identity-incomplete'))],
  ['invalid-argument', true, () => new ApiError(400, problem(400, 'invalid-argument'))],
  ["another module's code", true, () => new ApiError(403, problem(403, 'organisation.capability-missing'))],
  ['a code the catalog does not know', true, () => new ApiError(409, problem(409, 'compliance.something-new'))],
  ['a failure of the host', true, () => new ApiError(500, '')],
  ['a bad request without a code', true, () => new ApiError(400, problem(400))],
  ['the network', true, () => new TypeError('Failed to fetch')],
])('%s is worth reading again: %s', (_name, retryable, failure) => {
  expect(matrixRetryable(failure())).toBe(retryable);
});
