import { describe, expect, test } from 'vitest';
import { ApiError } from '@/api/baseApi';
import { setLanguage, t } from '@/i18n';
import { type TeamAction, type TeamFailure, teamFailureKey } from './teamErrors';

// What the members and invitations page (#12) says when an action is refused or fails, shaped like editorial's
// workflowFailureKey: a module code the page knows gives its catalog key; the unique index's already-exists on an
// invitation says the address is already a member or invited, as the handler's own check does; another module code
// shows the host's own words (ApiError.serverMessage, ADR 0007); anything else, a network failure included, says the
// action failed, with the action's placeholders.

/** Problem details as the host sends them, with a code where it has one. */
const problem = (status: number, code?: string, detail = 'Words of the host, not for the user.') =>
  JSON.stringify({ title: 'Refused', status, detail, code });

const refusal = (status: number, code?: string, detail?: string) => () =>
  new ApiError(status, problem(status, code, detail));

const actions: TeamAction[] = ['invite', 'renew', 'revoke', 'change', 'end'];

/** The text the page shows for a failure: the catalog's, with its values, or the host's own message. */
function textOf(failure: TeamFailure): string {
  return 'message' in failure ? failure.message : t(failure.key, failure.values);
}

type Known = [code: string, status: number, key: string, german: string, english: string];

const known: Known[] = [
  [
    'organisation.last-manager',
    409,
    'organisation.team.errors.lastManager',
    'Die letzte Apothekenleitung kann nicht ausgetragen oder zum Mitarbeiter gemacht werden.',
    'The last pharmacy manager cannot be removed or made an employee.',
  ],
  [
    'organisation.already-member-or-invited',
    409,
    'organisation.team.errors.alreadyMemberOrInvited',
    'Diese Adresse ist schon Mitglied oder eingeladen.',
    'This address is already a member or invited.',
  ],
  [
    'organisation.no-open-invitation',
    404,
    'organisation.team.errors.noOpenInvitation',
    'Diese Einladung ist nicht mehr offen: Sie wurde angenommen oder zurückgezogen.',
    'This invitation is no longer open: it was accepted or revoked.',
  ],
  [
    'organisation.no-active-membership',
    404,
    'organisation.team.errors.noActiveMembership',
    'Diese Mitgliedschaft ist nicht mehr aktiv.',
    'This membership is no longer active.',
  ],
  [
    'organisation.capability-missing',
    403,
    'organisation.team.errors.capabilityMissing',
    'Ihre Rolle in dieser Apotheke erlaubt das nicht.',
    'Your role in this pharmacy does not allow this.',
  ],
  [
    'organisation.pharmacy-not-found',
    404,
    'organisation.team.errors.pharmacyNotFound',
    'Sie sind nicht mehr Mitglied dieser Apotheke.',
    'You are no longer a member of this pharmacy.',
  ],
  [
    'organisation.pharmacy-changed',
    409,
    'organisation.team.errors.pharmacyChanged',
    'Die Apotheke wurde gerade gleichzeitig geändert. Prüfen Sie die aktualisierte Liste und versuchen Sie es erneut.',
    'The pharmacy was changed at the same time. Check the updated list and try again.',
  ],
  [
    'organisation.concurrent-save',
    409,
    'organisation.team.errors.concurrentSave',
    'Gleichzeitig wurde eine andere Änderung an der Apotheke gespeichert; es wurde nichts gespeichert. Versuchen Sie es erneut.',
    'Another change to the pharmacy was saved at the same moment; nothing was saved. Please try again.',
  ],
  [
    'organisation.invalid-email',
    400,
    'organisation.team.errors.invalidEmail',
    'Die E-Mail-Adresse fehlt, ist zu lang oder ist keine.',
    'The email address is missing, too long or not an email address.',
  ],
  [
    'organisation.invalid-job-function',
    400,
    'organisation.team.errors.invalidJobFunction',
    'Die Tätigkeit fehlt oder ist unbekannt.',
    'The job function is missing or unknown.',
  ],
  [
    'organisation.invalid-membership-role',
    400,
    'organisation.team.errors.invalidMembershipRole',
    'Die Rolle fehlt oder ist unbekannt.',
    'The role is missing or unknown.',
  ],
  [
    'organisation.identity-incomplete',
    403,
    'organisation.team.errors.identityIncomplete',
    'Ihr Konto nennt keinen Namen oder keine gültige E-Mail-Adresse. Ergänzen Sie Ihr Profil.',
    'Your account gives no name or valid email address. Complete your profile.',
  ],
  // #158: the invitation-mail limits answer 429; the page keeps it simple and names no retry time.
  [
    'organisation.invitation-renewed-too-soon',
    429,
    'organisation.team.errors.invitationRenewedTooSoon',
    'Diese Einladung wurde gerade erst gesendet. Sie können sie in einigen Minuten erneut senden.',
    'This invitation was only just sent. You can send it again in a few minutes.',
  ],
  [
    'organisation.too-many-invitations-today',
    429,
    'organisation.team.errors.tooManyInvitationsToday',
    'Ihre Apotheke hat die Höchstzahl an Einladungs-E-Mails für heute erreicht. Versuchen Sie es später erneut.',
    'Your pharmacy has reached the maximum number of invitation emails for today. Try again later.',
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
  ['a 400 invalid-argument (a failed guard, English)', refusal(400, 'invalid-argument')],
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
    const failure = teamFailureKey(action, new ApiError(status, problem(status, code)));

    expect(failure).toEqual({ key, values: expect.any(Object) });
    expect(textOf(failure)).toBe(german);
  });

  test.each(fallbackCases)('%s on %s says the action failed', (_answer, action, values, error, key, german) => {
    const failure = teamFailureKey(action, error(), values);

    expect(failure).toEqual({ key, values });
    expect(textOf(failure)).toBe(german);
  });
});

describe('in English', () => {
  test.each(knownCases)('%s on %s says its own text', (code, action, status, key, _german, english) => {
    setLanguage('en-GB', { remember: false });
    const failure = teamFailureKey(action, new ApiError(status, problem(status, code)));

    expect(failure).toEqual({ key, values: expect.any(Object) });
    expect(textOf(failure)).toBe(english);
  });

  test.each(fallbackCases)(
    '%s on %s says the action failed',
    (_answer, action, values, error, key, _german, english) => {
      setLanguage('en-GB', { remember: false });
      const failure = teamFailureKey(action, error(), values);

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
    const failure = teamFailureKey('invite', refusal(409, 'already-exists')(), { email: 'ben@example.com' });

    expect(failure).toEqual({ key: 'organisation.team.errors.alreadyMemberOrInvited', values: expect.any(Object) });
    expect(textOf(failure)).toBe(t('organisation.team.errors.alreadyMemberOrInvited'));
  },
);

// ADR 0007: a module's own words are shown as the host sent them when the page has none of its own for its code.
test.each(actions)('a module code the page does not know shows the host’s own message on %s', (action) => {
  const failure = teamFailureKey(action, refusal(409, 'organisation.something-new', 'Die Apotheke ist gesperrt.')(), {
    email: 'ben@example.com',
    name: 'Anna Albers',
  });

  expect(failure).toEqual({ message: 'Die Apotheke ist gesperrt.' });
  expect(textOf(failure)).toBe('Die Apotheke ist gesperrt.');
});

test("the host's words are never what the page says for a code it knows", () => {
  for (const [code, action, status] of knownCases) {
    expect(textOf(teamFailureKey(action, new ApiError(status, problem(status, code))))).not.toContain(
      'Words of the host',
    );
  }
});
