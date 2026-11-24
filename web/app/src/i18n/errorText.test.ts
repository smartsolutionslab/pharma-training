import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { ApiError } from '@/api/baseApi';
import { codeMessage, errorMessage, errorText, messageText } from './errorText';
import { i18n, type Language, setLanguage, t } from './index';

// A refusal is shown by its code (#147, ADR 0007), in this order: the page's own text for the code (an override), the
// catalog's errors.<code>, the host's English words only for a module's code the catalog does not know (a newer host
// than this SPA; warned once per code in development), and else the action's own failed text.

/** Problem details as the host sends them: its words in the detail, which only an unknown code may show. */
const problem = (status: number, code?: string, detail = 'server words') =>
  JSON.stringify({ title: 'Refused', status, detail, code });

const refusal = (status: number, code?: string, detail?: string) => new ApiError(status, problem(status, code, detail));

// An unknown code warns in development; a test that asks about the warning spies on it again.
beforeEach(() => {
  vi.spyOn(console, 'warn').mockImplementation(() => undefined);
});

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllEnvs();
});

describe('a code the catalog knows', () => {
  test.each<[Language, string]>([
    ['de-DE', 'Der Name einer Apotheke hat 1 bis 120 Zeichen und enthält keine Steuer- oder Richtungszeichen.'],
    ['en-GB', "A pharmacy's name has 1 to 120 characters and contains no control or direction characters."],
  ])('reads its catalog text with its limit filled in, in %s, never the server words', (language, text) => {
    setLanguage(language, { remember: false });

    const shown = errorText(refusal(400, 'organisation.invalid-pharmacy-name'), 'organisation.register.failed');

    expect(shown).toBe(text);
    expect(shown).not.toContain('server words');
  });

  test('keeps its key and the values errorValues has for it, not the caller values meant for the fallback', () => {
    expect(
      errorMessage(refusal(400, 'organisation.invalid-pharmacy-name'), 'editorial.chapters.saveFailed', {
        values: { title: 'Hygiene' },
      }),
    ).toEqual({ key: 'errors.organisation.invalid-pharmacy-name', values: { max: 120 } });
  });

  test('is read from the code at any status, so a 502 of the host translates', () => {
    expect(errorMessage(refusal(502, 'editorial.ai-draft-malformed'), 'editorial.test.draft.errors.failed')).toEqual({
      key: 'errors.editorial.ai-draft-malformed',
      values: {},
    });
  });

  test('gives way to the page own text for that code, which gets the code values and the caller values', () => {
    const message = errorMessage(refusal(400, 'organisation.invalid-pharmacy-name'), 'organisation.register.failed', {
      overrides: { 'organisation.invalid-pharmacy-name': 'editorial.chapters.saveFailed' },
      values: { title: 'Hygiene' },
    });

    expect(message).toEqual({ key: 'editorial.chapters.saveFailed', values: { max: 120, title: 'Hygiene' } });
  });

  test('reads the override text over the catalog text', () => {
    const shown = errorText(refusal(403, 'organisation.email-not-verified'), 'organisation.register.failed', {
      overrides: { 'organisation.email-not-verified': 'organisation.register.emailNotVerified' },
    });

    expect(shown).toBe(t('organisation.register.emailNotVerified'));
    expect(shown).not.toBe(errorText(refusal(403, 'organisation.email-not-verified'), 'organisation.register.failed'));
  });

  test('warns about nothing', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);

    errorText(refusal(400, 'organisation.invalid-pharmacy-name'), 'organisation.register.failed');

    expect(warn).not.toHaveBeenCalled();
  });
});

describe("the host's own codes", () => {
  test.each<[string, Language]>([
    ['invalid-argument', 'de-DE'],
    ['invalid-argument', 'en-GB'],
    ['already-exists', 'de-DE'],
    ['already-exists', 'en-GB'],
  ])('%s has a text in %s', (code, language) => {
    expect(i18n.global.te(`errors.${code}`, language)).toBe(true);
  });

  test.each<[Language, string]>([
    ['de-DE', 'Eine Angabe ist ungültig. Prüfen Sie Ihre Eingaben und versuchen Sie es erneut.'],
    ['en-GB', 'A value is not valid. Check your entries and try again.'],
  ])('invalid-argument reads its catalog text in %s, not the guard detail', (language, text) => {
    setLanguage(language, { remember: false });

    expect(errorText(refusal(400, 'invalid-argument', 'Value cannot be null.'), 'organisation.register.failed')).toBe(
      text,
    );
  });

  test('already-exists reads its catalog text', () => {
    expect(errorMessage(refusal(409, 'already-exists'), 'organisation.register.failed')).toEqual({
      key: 'errors.already-exists',
      values: {},
    });
  });
});

describe('a module code the catalog does not know', () => {
  test('shows the server words and warns once in development, naming the code, however often it is shown', () => {
    vi.stubEnv('DEV', true);
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);

    const first = errorText(refusal(409, 'editorial.zzz'), 'editorial.version.newVersionFailed');
    const second = errorText(refusal(409, 'editorial.zzz'), 'editorial.version.newVersionFailed');

    expect([first, second]).toEqual(['server words', 'server words']);
    expect(warn).toHaveBeenCalledOnce();
    expect(warn.mock.calls[0]?.map(String).join(' ')).toContain('editorial.zzz');
  });

  test('keeps the server words as the message', () => {
    expect(errorMessage(refusal(409, 'editorial.yyy'), 'editorial.version.newVersionFailed')).toEqual({
      serverMessage: 'server words',
    });
  });

  test('warns about nothing outside development', () => {
    vi.stubEnv('DEV', false);
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);

    expect(errorText(refusal(409, 'editorial.xxx'), 'editorial.version.newVersionFailed')).toBe('server words');
    expect(warn).not.toHaveBeenCalled();
  });

  test('without words of the host reads the fallback', () => {
    expect(errorMessage(refusal(409, 'editorial.www', ' '), 'editorial.version.newVersionFailed')).toEqual({
      key: 'editorial.version.newVersionFailed',
      values: {},
    });
  });
});

describe('the fallback', () => {
  const values = { title: 'Hygiene' };
  const fallbackText = () => t('editorial.chapters.saveFailed', values);

  test.each<[string, () => unknown]>([
    ['a 502 without a code (an edge)', () => refusal(502)],
    ['a 503 without a code (an edge)', () => refusal(503)],
    ['a 500', () => refusal(500, undefined, 'Internal Server Error')],
    ['a 400 without a code', () => refusal(400)],
    ['a host code the catalog does not know', () => refusal(409, 'some-host-code')],
    ['a network failure', () => new TypeError('Failed to fetch')],
    ['an error that is no ApiError', () => new Error('organisation.invalid-pharmacy-name')],
    ['something that is no error', () => ({ failureCode: 'organisation.invalid-pharmacy-name' })],
    ['nothing', () => undefined],
  ])('is the action text with the caller values for %s', (_, error) => {
    expect(errorMessage(error(), 'editorial.chapters.saveFailed', { values })).toEqual({
      key: 'editorial.chapters.saveFailed',
      values,
    });
    expect(errorText(error(), 'editorial.chapters.saveFailed', { values })).toBe(fallbackText());
    expect(fallbackText()).toContain('Hygiene');
  });

  test('has no values when the caller gives none', () => {
    expect(errorMessage(new TypeError('Failed to fetch'), 'editorial.version.newVersionFailed')).toEqual({
      key: 'editorial.version.newVersionFailed',
      values: {},
    });
  });
});

describe('codeMessage', () => {
  test('resolves a known code without an ApiError', () => {
    expect(codeMessage('organisation.invalid-pharmacy-name', 'organisation.register.failed')).toEqual({
      key: 'errors.organisation.invalid-pharmacy-name',
      values: { max: 120 },
    });
  });

  test('shows the server words it is given for an unknown module code', () => {
    expect(codeMessage('compliance.zzz', 'organisation.register.failed', { serverMessage: 'server words' })).toEqual({
      serverMessage: 'server words',
    });
  });

  test("never shows the words it is given for a host code, which are the framework's", () => {
    expect(codeMessage('some-host-code', 'organisation.register.failed', { serverMessage: 'Conflict' })).toEqual({
      key: 'organisation.register.failed',
      values: {},
    });
  });

  test('reads the fallback without a code', () => {
    expect(codeMessage(undefined, 'editorial.chapters.saveFailed', { values: { title: 'Hygiene' } })).toEqual({
      key: 'editorial.chapters.saveFailed',
      values: { title: 'Hygiene' },
    });
  });

  test('lets an override win for its code', () => {
    expect(
      codeMessage('compliance.capability-missing', 'compliance.assign.failed', {
        overrides: { 'compliance.capability-missing': 'compliance.assign.notAllowed' },
      }),
    ).toEqual({ key: 'compliance.assign.notAllowed', values: {} });
  });
});

describe('messageText', () => {
  test('of a kept message follows a switch of the language', () => {
    const message = errorMessage(refusal(400, 'organisation.invalid-pharmacy-name'), 'organisation.register.failed');
    const german = messageText(message);

    setLanguage('en-GB', { remember: false });

    expect(messageText(message)).toBe(
      "A pharmacy's name has 1 to 120 characters and contains no control or direction characters.",
    );
    expect(messageText(message)).not.toBe(german);
  });

  test('of the server words is the server words in either language', () => {
    setLanguage('en-GB', { remember: false });

    expect(messageText({ serverMessage: 'server words' })).toBe('server words');
  });

  test('fills in the values of a key', () => {
    expect(messageText({ key: 'editorial.chapters.saveFailed', values: { title: 'Hygiene' } })).toBe(
      'Das Kapitel „Hygiene“ konnte nicht gespeichert werden.',
    );
  });
});
