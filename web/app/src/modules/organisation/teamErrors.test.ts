import { describe, expect, test } from 'vitest';
import { ApiError } from '@/api/baseApi';
import { setLanguage, t } from '@/i18n';
import { type ErrorMessage, messageText } from '@/i18n/errorText';
import { type TeamAction, teamFailureMessage, teamLoadFailureMessage } from './teamErrors';

// What the members and invitations page (#12) says when an action is refused or fails, as an ErrorMessage of
// src/i18n/errorText.ts (#147): a code the catalog knows gives its errors.<code> text; the unique index's
// already-exists on an invitation says the address is already a member or invited, as the handler's own check does
// (the page's one override); a module code the catalog does not know shows the host's own words; anything else, a
// network failure included, says the action failed, with the action's placeholders.

/** Problem details as the host sends them, with a code where it has one. */
const problem = (status: number, code?: string, detail = 'Words of the host, not for the user.') =>
  JSON.stringify({ title: 'Refused', status, detail, code });

const refusal = (status: number, code?: string, detail?: string) => () =>
  new ApiError(status, problem(status, code, detail));

const actions: TeamAction[] = ['invite', 'renew', 'revoke', 'change', 'end'];

/** The text the page shows for a failure: the catalog's, with its values, or the host's own words. */
const textOf = (failure: ErrorMessage): string => messageText(failure);

type Known = [code: string, status: number, key: string, german: string, english: string];

const known: Known[] = [
  [
    'organisation.last-manager',
    409,
    'errors.organisation.last-manager',
    'Der letzte Apothekenleiter kann nicht ausgetragen oder zum Mitarbeiter gemacht werden.',
    'The last pharmacy manager cannot be removed or made an employee.',
  ],
  [
    'organisation.already-member-or-invited',
    409,
    'errors.organisation.already-member-or-invited',
    'Diese Adresse ist schon Mitglied oder eingeladen.',
    'This address is already a member or invited.',
  ],
  [
    'organisation.no-open-invitation',
    404,
    'errors.organisation.no-open-invitation',
    'Diese Einladung ist nicht mehr offen: Sie wurde angenommen oder zurückgezogen.',
    'This invitation is no longer open: it was accepted or revoked.',
  ],
  [
    'organisation.no-active-membership',
    404,
    'errors.organisation.no-active-membership',
    'Diese Mitgliedschaft ist nicht mehr aktiv.',
    'This membership is no longer active.',
  ],
  [
    'organisation.capability-missing',
    403,
    'errors.organisation.capability-missing',
    'Ihre Rolle in dieser Apotheke erlaubt das nicht.',
    'Your role in this pharmacy does not allow this.',
  ],
  [
    'organisation.pharmacy-not-found',
    404,
    'errors.organisation.pharmacy-not-found',
    'Sie sind nicht mehr Mitglied dieser Apotheke.',
    'You are no longer a member of this pharmacy.',
  ],
  [
    'organisation.pharmacy-changed',
    409,
    'errors.organisation.pharmacy-changed',
    'Die Apotheke wurde gerade gleichzeitig geändert. Prüfen Sie den aktuellen Stand und versuchen Sie es erneut.',
    'The pharmacy was changed at the same time. Check its current state and try again.',
  ],
  [
    'organisation.concurrent-save',
    409,
    'errors.organisation.concurrent-save',
    'Gleichzeitig wurde eine andere Anfrage gespeichert; es wurde nichts gespeichert. Versuchen Sie es erneut.',
    'Another request was being saved at the same moment; nothing was saved. Try again.',
  ],
  [
    'organisation.invalid-email',
    400,
    'errors.organisation.invalid-email',
    'Die E-Mail-Adresse fehlt, ist zu lang oder ist keine.',
    'The email address is missing, too long or not an email address.',
  ],
  [
    'organisation.invalid-job-function',
    400,
    'errors.organisation.invalid-job-function',
    'Die Tätigkeit fehlt oder ist unbekannt.',
    'The job function is missing or unknown.',
  ],
  [
    'organisation.invalid-membership-role',
    400,
    'errors.organisation.invalid-membership-role',
    'Die Rolle fehlt oder ist unbekannt.',
    'The role is missing or unknown.',
  ],
  [
    'organisation.identity-incomplete',
    403,
    'errors.organisation.identity-incomplete',
    'Ihr Konto nennt keinen Namen oder keine gültige E-Mail-Adresse. Ergänzen Sie Ihr Profil.',
    'Your account gives no name or valid email address. Complete your profile.',
  ],
  // #158: the invitation-mail limits answer 429; their catalog texts name no retry time.
  [
    'organisation.invitation-renewed-too-soon',
    429,
    'errors.organisation.invitation-renewed-too-soon',
    'Diese Einladung wurde gerade erst gesendet. Sie können sie in einigen Minuten erneut senden.',
    'This invitation was only just sent. You can send it again in a few minutes.',
  ],
  [
    'organisation.too-many-invitations-today',
    429,
    'errors.organisation.too-many-invitations-today',
    'Ihre Apotheke hat die Höchstzahl an Einladungs-E-Mails für heute erreicht. Versuchen Sie es morgen erneut.',
    'Your pharmacy has reached the maximum number of invitation emails for today. Try again tomorrow.',
  ],
  // A code the page had no words of its own for, which showed the host's words until #147.
  [
    'organisation.invitation-changed',
    409,
    'errors.organisation.invitation-changed',
    'Die Einladung wurde gerade gleichzeitig geändert. Versuchen Sie es erneut.',
    'The invitation was changed at the same time. Try again.',
  ],
  // The host's own code for a failed guard: its catalog text, no longer the action's.
  [
    'invalid-argument',
    400,
    'errors.invalid-argument',
    'Eine Angabe ist ungültig. Prüfen Sie Ihre Eingaben und versuchen Sie es erneut.',
    'A value is not valid. Check your entries and try again.',
  ],
];

/** Every known code on every action: the code decides, not the action. */
const knownCases = known.flatMap(([code, status, key, german, english]) =>
  actions.map((action): [string, TeamAction, number, string, string, string] => [
    code,
    action,
    status,
    key,
    german,
    english,
  ]),
);

/** Each action's text when it failed for a reason the page has no words of its own for, with its placeholders. */
type Failed = [action: TeamAction, values: Record<string, string>, key: string, german: string, english: string];

const failed: Failed[] = [
  [
    'invite',
    { email: 'ben@example.com' },
    'organisation.team.invite.failed',
    'Die Einladung an ben@example.com konnte nicht gesendet werden.',
    'The invitation to ben@example.com could not be sent.',
  ],
  [
    'renew',
    { email: 'ben@example.com' },
    'organisation.team.invitations.renewFailed',
    'Die Einladung an ben@example.com konnte nicht neu gesendet werden.',
    'The invitation to ben@example.com could not be sent again.',
  ],
  [
    'revoke',
    { email: 'ben@example.com' },
    'organisation.team.revoke.failed',
    'Die Einladung an ben@example.com konnte nicht zurückgezogen werden.',
    'The invitation to ben@example.com could not be revoked.',
  ],
  [
    'change',
    { name: 'Anna Albers' },
    'organisation.team.change.failed',
    'Tätigkeit und Rolle von Anna Albers konnten nicht geändert werden.',
    'The job function and role of Anna Albers could not be changed.',
  ],
  [
    'end',
    { name: 'Anna Albers' },
    'organisation.team.end.failed',
    'Anna Albers konnte nicht ausgetragen werden.',
    'The membership of Anna Albers could not be ended.',
  ],
];

/** Failures the page has no words of its own for, and the host none for the user either. */
const fallbacks: [string, () => unknown][] = [
  ['a network failure (TypeError)', () => new TypeError('Failed to fetch')],
  ['a 500 with problem details', () => new ApiError(500, problem(500, 'organisation.something-else'))],
  ['a 500 without a body', () => new ApiError(500, '')],
  ['a 401', () => new ApiError(401, '')],
  ["a policy's 403 without a code", refusal(403)],
  ["an edge's 502", () => new ApiError(502, 'Bad Gateway')],
  [
    'a module code the page does not know, without words',
    () => new ApiError(409, JSON.stringify({ status: 409, code: 'organisation.something-else' })),
  ],
  ['an error that is no error at all', () => 'nothing'],
];

const fallbackCases = failed.flatMap(([action, values, key, german, english]) =>
  fallbacks.map(
    ([answer, error]): [string, TeamAction, Record<string, string>, () => unknown, string, string, string] => [
      answer,
      action,
      values,
      error,
      key,
      german,
      english,
    ],
  ),
);

describe('in German', () => {
  test.each(knownCases)('%s on %s says its own text', (code, action, status, key, german) => {
    const failure = teamFailureMessage(action, new ApiError(status, problem(status, code)));

    expect(failure).toEqual({ key, values: expect.any(Object) });
    expect(textOf(failure)).toBe(german);
  });

  test.each(fallbackCases)('%s on %s says the action failed', (_answer, action, values, error, key, german) => {
    const failure = teamFailureMessage(action, error(), values);

    expect(failure).toEqual({ key, values });
    expect(textOf(failure)).toBe(german);
  });
});

describe('in English', () => {
  test.each(knownCases)('%s on %s says its own text', (code, action, status, key, _german, english) => {
    setLanguage('en-GB', { remember: false });
    const failure = teamFailureMessage(action, new ApiError(status, problem(status, code)));

    expect(failure).toEqual({ key, values: expect.any(Object) });
    expect(textOf(failure)).toBe(english);
  });

  test.each(fallbackCases)(
    '%s on %s says the action failed',
    (_answer, action, values, error, key, _german, english) => {
      setLanguage('en-GB', { remember: false });
      const failure = teamFailureMessage(action, error(), values);

      expect(failure).toEqual({ key, values });
      expect(textOf(failure)).toBe(english);
    },
  );
});

// The unique index on pharmacy and address refused the same address another writer invited first: the fact is the
// one the handler's own check (already-member-or-invited) reports, so the page says the same.
test.each(['de-DE', 'en-GB'] as const)(
  "the unique index's 409 already-exists says the address is already a member or invited, in %s",
  (language) => {
    setLanguage(language, { remember: false });
    const failure = teamFailureMessage('invite', refusal(409, 'already-exists')(), { email: 'ben@example.com' });

    expect(failure).toEqual({ key: 'errors.organisation.already-member-or-invited', values: expect.any(Object) });
    expect(textOf(failure)).toBe(t('errors.organisation.already-member-or-invited'));
  },
);

// #147: a module's own words are shown as the host sent them only for a code the catalog does not know.
test.each(actions)('a module code the catalog does not know shows the host’s own message on %s', (action) => {
  const failure = teamFailureMessage(
    action,
    refusal(409, 'organisation.something-new', 'Die Apotheke ist gesperrt.')(),
    {
      email: 'ben@example.com',
      name: 'Anna Albers',
    },
  );

  expect(failure).toEqual({ serverMessage: 'Die Apotheke ist gesperrt.' });
  expect(textOf(failure)).toBe('Die Apotheke ist gesperrt.');
});

test("the host's words are never what the page says for a code it knows", () => {
  for (const [code, action, status] of knownCases) {
    expect(textOf(teamFailureMessage(action, new ApiError(status, problem(status, code))))).not.toContain(
      'Words of the host',
    );
  }
});

test.each<['de-DE' | 'en-GB', string]>([
  ['de-DE', 'Die Einladung wurde gerade gleichzeitig geändert. Versuchen Sie es erneut.'],
  ['en-GB', 'The invitation was changed at the same time. Try again.'],
])('a renewal refused with invitation-changed and the host words says the catalog text in %s', (language, text) => {
  setLanguage(language, { remember: false });

  const shown = textOf(
    teamFailureMessage('renew', refusal(409, 'organisation.invitation-changed', 'SERVER WORDS')(), {
      email: 'ben@example.com',
    }),
  );

  expect(shown).toBe(text);
  expect(shown).not.toContain('SERVER WORDS');
});

// A list that could not be read says why where the catalog knows the code, else that it failed (#147).
test.each([
  [refusal(404, 'organisation.pharmacy-not-found'), 'errors.organisation.pharmacy-not-found'],
  [refusal(403, 'organisation.capability-missing'), 'errors.organisation.capability-missing'],
  [() => new ApiError(500, ''), 'organisation.team.members.loadFailed'],
  [() => new TypeError('Failed to fetch'), 'organisation.team.members.loadFailed'],
])('a list read refused or failed says the message of its code, else that it failed (%#)', (error, key) => {
  expect(teamLoadFailureMessage(error(), 'organisation.team.members.loadFailed')).toEqual({
    key,
    values: expect.any(Object),
  });
});
