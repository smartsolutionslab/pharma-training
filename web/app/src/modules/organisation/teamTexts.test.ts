import { describe, expect, test } from 'vitest';
import { setLanguage, t } from '@/i18n';

// The members and invitations page's texts (#12, ADR 0007: "Sie", the glossary), in both languages. The page's tests
// find its elements by these keys; here each says what it says.

type Text = [key: string, values: Record<string, unknown>, german: string, english: string];

const pharmacy = { pharmacy: 'Apotheke am Markt' };
const anna = { name: 'Anna Albers' };
const ben = { email: 'ben@example.com' };

const texts: Text[] = [
  ['modules.organisation.title', {}, 'Mitglieder', 'Members'],
  [
    'organisation.team.intro',
    pharmacy,
    'Laden Sie Ihr Team ein und legen Sie fest, wer in Apotheke am Markt welche Tätigkeit und Rolle hat.',
    'Invite your team and decide who has which job function and role in Apotheke am Markt.',
  ],
  [
    'organisation.team.notAllowed',
    pharmacy,
    'Ihre Rolle in Apotheke am Markt erlaubt es nicht, Mitglieder zu verwalten.',
    'Your role in Apotheke am Markt does not allow you to manage members.',
  ],
  ['organisation.team.cancel', {}, 'Abbrechen', 'Cancel'],
  ['organisation.team.columns.name', {}, 'Name', 'Name'],
  ['organisation.team.columns.jobFunction', {}, 'Tätigkeit', 'Job function'],
  ['organisation.team.columns.role', {}, 'Rolle', 'Role'],
  ['organisation.team.columns.since', {}, 'Mitglied seit', 'Member since'],
  ['organisation.team.columns.email', {}, 'E-Mail-Adresse', 'Email address'],
  ['organisation.team.columns.expires', {}, 'Gültig bis', 'Valid until'],
  ['organisation.team.columns.status', {}, 'Status', 'Status'],

  ['organisation.team.members.title', {}, 'Aktive Mitglieder', 'Active members'],
  ['organisation.team.members.you', { name: 'Local Developer' }, 'Local Developer (Sie)', 'Local Developer (you)'],
  [
    'organisation.team.members.loadFailed',
    {},
    'Die Mitglieder konnten nicht geladen werden.',
    'The members could not be loaded.',
  ],
  ['organisation.team.members.empty', {}, 'Noch keine aktiven Mitglieder.', 'No active members yet.'],
  ['organisation.team.members.change', {}, 'Ändern', 'Change'],
  [
    'organisation.team.members.changeOf',
    anna,
    'Tätigkeit und Rolle von Anna Albers ändern',
    'Change the job function and role of Anna Albers',
  ],
  ['organisation.team.members.end', {}, 'Austragen', 'End membership'],
  ['organisation.team.members.endOf', anna, 'Anna Albers austragen', 'End the membership of Anna Albers'],

  ['organisation.team.invitations.title', {}, 'Einladungen', 'Invitations'],
  ['organisation.team.invitations.open', {}, 'Offen', 'Open'],
  ['organisation.team.invitations.expired', {}, 'Abgelaufen', 'Expired'],
  [
    'organisation.team.invitations.loadFailed',
    {},
    'Die Einladungen konnten nicht geladen werden.',
    'The invitations could not be loaded.',
  ],
  ['organisation.team.invitations.empty', {}, 'Keine offenen Einladungen.', 'No open invitations.'],
  ['organisation.team.invitations.inviteFirst', {}, 'Mitglied einladen', 'Invite a member'],
  ['organisation.team.invitations.renew', {}, 'Neu senden', 'Resend'],
  [
    'organisation.team.invitations.renewOf',
    ben,
    'Einladung an ben@example.com neu senden',
    'Resend the invitation to ben@example.com',
  ],
  [
    'organisation.team.invitations.renewed',
    ben,
    'Die Einladung an ben@example.com wurde neu gesendet und gilt wieder 14 Tage.',
    'The invitation to ben@example.com was sent again and is valid for another 14 days.',
  ],
  [
    'organisation.team.invitations.renewFailed',
    ben,
    'Die Einladung an ben@example.com konnte nicht neu gesendet werden.',
    'The invitation to ben@example.com could not be sent again.',
  ],
  ['organisation.team.invitations.revoke', {}, 'Zurückziehen', 'Revoke'],
  [
    'organisation.team.invitations.revokeOf',
    ben,
    'Einladung an ben@example.com zurückziehen',
    'Revoke the invitation to ben@example.com',
  ],

  [
    'organisation.team.revoke.title',
    ben,
    'Einladung an ben@example.com zurückziehen?',
    'Revoke the invitation to ben@example.com?',
  ],
  [
    'organisation.team.revoke.description',
    {},
    'Der Link in der Einladung gilt danach nicht mehr. Sie können die Adresse erneut einladen.',
    'The link in the invitation stops working. You can invite the address again.',
  ],
  ['organisation.team.revoke.confirm', {}, 'Zurückziehen', 'Revoke'],
  [
    'organisation.team.revoke.revoked',
    ben,
    'Die Einladung an ben@example.com wurde zurückgezogen.',
    'The invitation to ben@example.com was revoked.',
  ],
  [
    'organisation.team.revoke.failed',
    ben,
    'Die Einladung an ben@example.com konnte nicht zurückgezogen werden.',
    'The invitation to ben@example.com could not be revoked.',
  ],

  ['organisation.team.invite.open', {}, 'Einladen', 'Invite'],
  ['organisation.team.invite.title', {}, 'Mitglied einladen', 'Invite a member'],
  [
    'organisation.team.invite.description',
    {},
    'Die Einladung kommt per E-Mail, mit einem Link, der 14 Tage gilt.',
    'The invitation arrives by email, with a link that is valid for 14 days.',
  ],
  ['organisation.team.invite.email', {}, 'E-Mail-Adresse', 'Email address'],
  ['organisation.team.invite.jobFunction', {}, 'Tätigkeit', 'Job function'],
  ['organisation.team.invite.role', {}, 'Rolle', 'Role'],
  ['organisation.team.invite.choose', {}, 'Bitte wählen…', 'Choose…'],
  ['organisation.team.invite.submit', {}, 'Einladung senden', 'Send invitation'],
  ['organisation.team.invite.emailRequired', {}, 'Geben Sie die E-Mail-Adresse an.', 'Enter the email address.'],
  [
    'organisation.team.invite.emailTooLong',
    {},
    'Eine E-Mail-Adresse hat höchstens 254 Zeichen.',
    'An email address is at most 254 characters.',
  ],
  [
    'organisation.team.invite.emailInvalid',
    {},
    'Geben Sie eine gültige E-Mail-Adresse an.',
    'Enter a valid email address.',
  ],
  ['organisation.team.invite.jobFunctionRequired', {}, 'Wählen Sie die Tätigkeit.', 'Choose the job function.'],
  ['organisation.team.invite.roleRequired', {}, 'Wählen Sie die Rolle.', 'Choose the role.'],
  [
    'organisation.team.invite.invited',
    ben,
    'Die Einladung an ben@example.com wurde gesendet.',
    'The invitation to ben@example.com was sent.',
  ],
  [
    'organisation.team.invite.failed',
    ben,
    'Die Einladung an ben@example.com konnte nicht gesendet werden.',
    'The invitation to ben@example.com could not be sent.',
  ],

  ['organisation.team.change.title', {}, 'Tätigkeit und Rolle ändern', 'Change job function and role'],
  [
    'organisation.team.change.description',
    { ...anna, ...pharmacy },
    'Anna Albers in Apotheke am Markt',
    'Anna Albers in Apotheke am Markt',
  ],
  ['organisation.team.change.submit', {}, 'Speichern', 'Save'],
  [
    'organisation.team.change.changed',
    anna,
    'Tätigkeit und Rolle von Anna Albers wurden geändert.',
    'The job function and role of Anna Albers were changed.',
  ],
  [
    'organisation.team.change.failed',
    anna,
    'Tätigkeit und Rolle von Anna Albers konnten nicht geändert werden.',
    'The job function and role of Anna Albers could not be changed.',
  ],

  ['organisation.team.end.title', anna, 'Anna Albers austragen?', 'End the membership of Anna Albers?'],
  [
    'organisation.team.end.description',
    { ...anna, ...pharmacy },
    'Anna Albers ist danach kein Mitglied von Apotheke am Markt mehr. Sie können Anna Albers später erneut einladen.',
    'Anna Albers will no longer be a member of Apotheke am Markt. You can invite Anna Albers again later.',
  ],
  [
    'organisation.team.end.descriptionSelf',
    pharmacy,
    'Sie sind danach kein Mitglied von Apotheke am Markt mehr und können die Apotheke nicht mehr öffnen.',
    'You will no longer be a member of Apotheke am Markt and can no longer open it.',
  ],
  ['organisation.team.end.confirm', {}, 'Austragen', 'End membership'],
  ['organisation.team.end.ended', anna, 'Anna Albers wurde ausgetragen.', 'The membership of Anna Albers was ended.'],
  [
    'organisation.team.end.endedSelf',
    pharmacy,
    'Sie sind nicht mehr Mitglied von Apotheke am Markt.',
    'You are no longer a member of Apotheke am Markt.',
  ],
  [
    'organisation.team.end.failed',
    anna,
    'Anna Albers konnte nicht ausgetragen werden.',
    'The membership of Anna Albers could not be ended.',
  ],
];

describe('in German', () => {
  test.each(texts)('%s says its text', (key, values, german) => {
    expect(t(key, values)).toBe(german);
  });
});

describe('in English', () => {
  test.each(texts)('%s says its text', (key, values, _german, english) => {
    setLanguage('en-GB', { remember: false });

    expect(t(key, values)).toBe(english);
  });
});
