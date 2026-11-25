import { afterEach, describe, expect, test } from 'vitest';
import { ApiError } from '@/api/baseApi';
import { setLanguage } from '@/i18n';
import { messageText } from '@/i18n/errorText';
import { retentionRefusal } from './retentionErrors';

// What the retention period section shows when the host refuses a period (#419, T2; ADR 0027), by the refusal's code:
// the period's own refusal at the field, in the catalog's words with the bounds filled in; the refusals of the route
// (#418) in a toast, in their catalog's words, a pharmacy no longer found also asking for the memberships again; and
// anything else (the network, a 5xx, a 400 without a code) as the save's own failure.

/** Problem details as the host sends a module's refusal: its English message as the title, and its code. */
const problem = (status: number, code?: string, title = 'Words of the host, not for the user.') =>
  JSON.stringify({ title, status, code });

afterEach(() => {
  setLanguage('de-DE', { remember: false });
});

describe('the period’s refusal', () => {
  const invalid = () =>
    new ApiError(
      400,
      problem(400, 'certification.invalid-retention-period', 'The retention period is from 2 to 30 years.'),
    );

  test('shows at the field', () => {
    expect(retentionRefusal(invalid()).kind).toBe('field');
  });

  test('reads the catalog’s text with the bounds', () => {
    expect(messageText(retentionRefusal(invalid()).message)).toBe(
      'Die Aufbewahrungsfrist ist eine ganze Zahl von 2 bis 30 Jahren.',
    );
  });

  test('reads the English catalog’s text in English', () => {
    const refusal = retentionRefusal(invalid());
    setLanguage('en-GB', { remember: false });

    expect(messageText(refusal.message)).toBe('The retention period is a whole number of years from 2 to 30.');
  });
});

describe('a refusal of the route', () => {
  test.each([
    ['capability-missing', 403, 'Ihre Rolle in dieser Apotheke erlaubt das nicht.'],
    ['identity-incomplete', 403, 'Ihre Anmeldung nennt keine Person. Melden Sie sich erneut an.'],
  ])('%s shows its catalog text in a toast, the pharmacy still there', (code, status, text) => {
    const refusal = retentionRefusal(new ApiError(status, problem(status, `certification.${code}`)));

    expect(refusal).toMatchObject({ kind: 'toast', pharmacyGone: false });
    expect(messageText(refusal.message)).toBe(text);
  });

  test('pharmacy-not-found shows its catalog text in a toast, the pharmacy gone', () => {
    const refusal = retentionRefusal(new ApiError(404, problem(404, 'certification.pharmacy-not-found')));

    expect(refusal).toMatchObject({ kind: 'toast', pharmacyGone: true });
    expect(messageText(refusal.message)).toBe('Sie sind nicht Mitglied dieser Apotheke, oder es gibt sie nicht.');
  });
});

describe('anything else', () => {
  test.each([
    ['the network', () => new TypeError('Failed to fetch')],
    ['a failure of the host', () => new ApiError(500, '')],
    ['a 400 without a code', () => new ApiError(400, JSON.stringify({ title: 'Bad Request', status: 400 }))],
  ])('%s reads that the period could not be saved, in a toast', (_name, failure) => {
    const refusal = retentionRefusal(failure());

    expect(refusal).toEqual({
      kind: 'toast',
      message: { key: 'certification.retention.failed', values: {} },
      pharmacyGone: false,
    });
    expect(messageText(refusal.message)).toBe(
      'Die Aufbewahrungsfrist konnte nicht gespeichert werden. Versuchen Sie es erneut.',
    );
  });
});
