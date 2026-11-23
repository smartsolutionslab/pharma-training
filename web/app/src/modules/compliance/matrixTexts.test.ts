import { describe, expect, test } from 'vitest';
import { setLanguage, t } from '@/i18n';

// The team matrix's texts (#36, ADR 0007: "Sie", the glossary), in both languages, as far as its shell and its
// refusals go. The page's tests find its elements by these keys; here each says what it says.

type Text = [key: string, values: Record<string, unknown>, german: string, english: string];

const texts: Text[] = [
  ['modules.compliance.title', {}, 'Team-Matrix', 'Team matrix'],
  [
    'compliance.matrix.intro',
    { pharmacy: 'Apotheke am Markt' },
    'Wer in Apotheke am Markt welche Schulung bis wann abschließen muss.',
    'Who in Apotheke am Markt has to complete which course by when.',
  ],
  [
    'compliance.matrix.notAllowed',
    {},
    'Sie dürfen den Schulungsstand des Teams in dieser Apotheke nicht sehen.',
    "You may not view the team's training status in this pharmacy.",
  ],
  [
    'compliance.matrix.pharmacyNotFound',
    {},
    'Sie sind nicht mehr Mitglied dieser Apotheke.',
    'You are no longer a member of this pharmacy.',
  ],
  [
    'compliance.matrix.identityIncomplete',
    {},
    'Ihr Konto nennt keinen Namen oder keine gültige E-Mail-Adresse. Ergänzen Sie Ihr Profil.',
    'Your account gives no name or valid email address. Complete your profile.',
  ],
  [
    'compliance.matrix.loadFailed',
    {},
    'Die Team-Matrix konnte nicht geladen werden.',
    'The team matrix could not be loaded.',
  ],
  ['compliance.matrix.retry', {}, 'Erneut versuchen', 'Try again'],
  ['compliance.matrix.table.label', {}, 'Team-Matrix: Mitglieder nach Schulungen', 'Team matrix: members by course'],
];

describe('says', () => {
  test.each(texts)('%s (%o) in German', (key, values, german) => {
    expect(t(key, values)).toBe(german);
  });

  test.each(texts)('%s (%o) in English', (key, values, _german, english) => {
    setLanguage('en-GB', { remember: false });

    expect(t(key, values)).toBe(english);
  });
});
