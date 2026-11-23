import { expect, test } from 'vitest';
import { ApiError } from '@/api/baseApi';
import { matrixFailure } from './matrixErrors';

// What the team matrix (#36) says when the host refuses it or fails, shaped like organisation's teamFailureKey: a code
// the page has words for gives its catalog key; another of the module's own codes shows the host's own words
// (ApiError.serverMessage, ADR 0007); anything else, a network failure included, says the matrix could not be loaded,
// and the page offers to try again.

/** Problem details as the host sends them, with a code where it has one. */
const problem = (status: number, code?: string, detail = 'Words of the host, not for the user.') =>
  JSON.stringify({ title: 'Refused', status, detail, code });

test.each([
  [403, 'compliance.capability-missing', 'compliance.matrix.notAllowed'],
  [404, 'compliance.pharmacy-not-found', 'compliance.matrix.pharmacyNotFound'],
  [403, 'compliance.identity-incomplete', 'compliance.matrix.identityIncomplete'],
])('%i %s says %s, not the host’s words', (status, code, key) => {
  expect(matrixFailure(new ApiError(status, problem(status, code)))).toEqual({ key });
});

test.each([
  [409, 'compliance.something-new', 'Etwas Neues.'],
  [400, 'compliance.invalid-job-function', 'Diese Tätigkeit gibt es nicht.'],
])('another of the module’s own refusals, %i %s, shows the host’s words', (status, code, detail) => {
  expect(matrixFailure(new ApiError(status, problem(status, code, detail)))).toEqual({ message: detail });
});

test.each([
  ['a failure of the host', () => new ApiError(500, '')],
  ['a failure of the host with a module code', () => new ApiError(500, problem(500, 'compliance.capability-missing'))],
  ['a bad request without a code', () => new ApiError(400, problem(400))],
  ['a failed guard', () => new ApiError(400, problem(400, 'invalid-argument'))],
  ['another module’s refusal', () => new ApiError(403, problem(403, 'organisation.capability-missing'))],
  ['no token', () => new ApiError(401, '')],
  ['the network', () => new TypeError('Failed to fetch')],
  ['anything else', () => new Error('Something broke.')],
])('%s says the matrix could not be loaded', (_name, failure) => {
  expect(matrixFailure(failure())).toEqual({ key: 'compliance.matrix.loadFailed' });
});
