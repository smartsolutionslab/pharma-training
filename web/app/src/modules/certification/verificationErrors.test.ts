import { expect, test } from 'vitest';
import { ApiError } from '@/api/baseApi';
import { verificationOutcome } from './verificationErrors';

// What the verification page (#54) shows when the host does not verify a code, by the spec's "Refusal → UI" table. An
// unknown code is the host's 404 certification.certificate-not-verifiable: the page says so as its content, the same
// words it shows for a malformed code, never as an alert. The shared rate limit (ADR 0026) is an alert with its own
// words. Another of the module's own refusals shows its catalog text, the host's words only for a code the catalog does
// not know (#147, ADR 0007); anything else, the network included, says the certificate could not be verified.

/** Problem details as the host sends them, with a code where it has one. */
const problem = (status: number, code?: string, detail = 'Words of the host, not for the user.') =>
  JSON.stringify({ title: 'Refused', status, detail, code });

test('404 certification.certificate-not-verifiable says the code cannot be verified, as content', () => {
  expect(verificationOutcome(new ApiError(404, problem(404, 'certification.certificate-not-verifiable')))).toEqual({
    kind: 'notVerifiable',
  });
});

test('429 certification.verification-limit-reached is an alert with the page’s own words', () => {
  expect(verificationOutcome(new ApiError(429, problem(429, 'certification.verification-limit-reached')))).toEqual({
    kind: 'alert',
    message: { key: 'certification.verify.limitReached', values: {} },
  });
});

test('another of the module’s own refusals the catalog knows is an alert in the catalog’s text', () => {
  expect(verificationOutcome(new ApiError(404, problem(404, 'certification.sample-not-found')))).toEqual({
    kind: 'alert',
    message: { key: 'errors.certification.sample-not-found', values: {} },
  });
});

test.each([
  [409, 'certification.something-new', 'Etwas Neues.'],
  [400, 'certification.invalid-code', 'Dieser Code ist ungültig.'],
])('a module refusal the catalog does not know, %i %s, is an alert in the host’s words', (status, code, detail) => {
  expect(verificationOutcome(new ApiError(status, problem(status, code, detail)))).toEqual({
    kind: 'alert',
    message: { serverMessage: detail },
  });
});

test.each([
  ['a failure of the host', () => new ApiError(500, '')],
  [
    'a failure of the host with the module’s code',
    () => new ApiError(500, problem(500, 'certification.certificate-not-verifiable')),
  ],
  ['a bad request without a code', () => new ApiError(400, problem(400))],
  ['a failed guard', () => new ApiError(400, problem(400, 'invalid-argument'))],
  ['a 404 without a code, not the host’s answer', () => new ApiError(404, '')],
  ['a 429 without a code, not the host’s answer', () => new ApiError(429, '')],
  ['another module’s refusal', () => new ApiError(404, problem(404, 'organisation.x'))],
  ['the network', () => new TypeError('Failed to fetch')],
  ['anything else', () => new Error('Something broke.')],
])('%s says the certificate could not be verified', (_name, failure) => {
  expect(verificationOutcome(failure())).toEqual({
    kind: 'alert',
    message: { key: 'certification.verify.loadFailed', values: {} },
  });
});
