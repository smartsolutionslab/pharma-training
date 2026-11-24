import { expect, test } from 'vitest';
import { ApiError } from '@/api/baseApi';
import { certificatesFailure, documentFailure } from './certificateErrors';

// What My certificates (#296) says when the host refuses, by the refusal's code (src/i18n/errorText.ts, #147): the
// page's own words where they depend on the action, the catalog's errors.<code> for the module's other refusals, the
// host's words only for a module code the catalog does not know, and else that the action failed.

/** Problem details as the host sends them, with a code where it has one. */
const problem = (status: number, code?: string, detail = 'Words of the host, not for the user.') =>
  JSON.stringify({ title: 'Refused', status, detail, code });

const course = 'Hygiene in der Apotheke';

test('a list refused for a token without a subject reads the catalog’s text', () => {
  expect(certificatesFailure(new ApiError(403, problem(403, 'certification.identity-incomplete')))).toEqual({
    key: 'errors.certification.identity-incomplete',
    values: {},
  });
});

test('a list refused with a module code the catalog does not know reads the host’s words', () => {
  expect(certificatesFailure(new ApiError(409, problem(409, 'certification.something-new', 'Etwas Neues.')))).toEqual({
    serverMessage: 'Etwas Neues.',
  });
});

test.each([
  ['a failure of the host', () => new ApiError(500, '')],
  ['another module’s refusal', () => new ApiError(404, problem(404, 'organisation.x'))],
  ['the network', () => new TypeError('Failed to fetch')],
])('%s reads that the certificates could not be loaded', (_name, failure) => {
  expect(certificatesFailure(failure())).toEqual({ key: 'certification.mine.loadFailed', values: {} });
});

test('a PDF not stored yet reads the page’s own words, naming the course', () => {
  expect(documentFailure(new ApiError(409, problem(409, 'certification.document-not-ready')), course)).toEqual({
    key: 'certification.mine.documentNotReady',
    values: { course },
  });
});

test.each(['certification.identity-incomplete', 'certification.certificate-not-found'])(
  'a PDF refused with %s reads the catalog’s text',
  (code) => {
    expect(documentFailure(new ApiError(404, problem(404, code)), course)).toEqual({
      key: `errors.${code}`,
      values: {},
    });
  },
);

test.each([
  ['a failure of the host', () => new ApiError(502, '')],
  ['another module’s refusal', () => new ApiError(404, problem(404, 'organisation.x'))],
  ['the network', () => new TypeError('Failed to fetch')],
])('%s reads that the PDF could not be opened, naming the course', (_name, failure) => {
  expect(documentFailure(failure(), course)).toEqual({ key: 'certification.mine.documentFailed', values: { course } });
});
