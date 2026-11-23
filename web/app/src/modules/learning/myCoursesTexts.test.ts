import { describe, expect, test } from 'vitest';
import { setLanguage, t } from '@/i18n';

// The texts my courses (#38, ADR 0007: "Sie", the glossary) shows when it cannot list the courses, in both languages:
// the refusals myCoursesFailure names and the offer to try again. The page's other texts come with the page.

type Text = [key: string, german: string, english: string];

const texts: Text[] = [
  [
    'learning.myCourses.pharmacyNotFound',
    'Sie sind nicht mehr Mitglied dieser Apotheke.',
    'You are no longer a member of this pharmacy.',
  ],
  [
    'learning.myCourses.identityIncomplete',
    'Ihr Konto nennt keinen Namen oder keine gültige E-Mail-Adresse. Ergänzen Sie Ihr Profil.',
    'Your account gives no name or valid email address. Complete your profile.',
  ],
  [
    'learning.myCourses.loadFailed',
    'Ihre Schulungen konnten nicht geladen werden.',
    'Your courses could not be loaded.',
  ],
  ['learning.myCourses.retry', 'Erneut versuchen', 'Try again'],
];

describe('says', () => {
  test.each(texts)('%s in German', (key, german) => {
    expect(t(key)).toBe(german);
  });

  test.each(texts)('%s in English', (key, _german, english) => {
    setLanguage('en-GB', { remember: false });

    expect(t(key)).toBe(english);
  });
});
