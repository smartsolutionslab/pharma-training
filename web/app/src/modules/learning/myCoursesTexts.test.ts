import { describe, expect, test } from 'vitest';
import { setLanguage, t } from '@/i18n';

// The texts my courses (#38, ADR 0007: "Sie", the glossary) shows, in both languages: the page's own (its intro, the
// list, each course's due line, Start and Continue, a locked or retired course, the empty list), the refusals
// myCoursesFailure names and the offer to try again. A text with placeholders is said with them filled in.

type Text = [key: string, values: Record<string, string>, german: string, english: string];

const pharmacy = { pharmacy: 'Apotheke am Markt' };
const date = { date: '20.11.2026' };
const title = { title: 'Hygiene in der Apotheke' };

const texts: Text[] = [
  [
    'learning.myCourses.intro',
    pharmacy,
    'Ihre Schulungen in Apotheke am Markt: Überfällige zuerst, dann nach Fälligkeit.',
    'Your courses in Apotheke am Markt: overdue first, then by due date.',
  ],
  ['learning.myCourses.list.title', {}, 'Zugewiesene Schulungen', 'Assigned courses'],
  ['learning.myCourses.due', date, 'Fällig am 20.11.2026', 'Due 20.11.2026'],
  ['learning.myCourses.dueOverdue', date, 'War fällig am 20.11.2026', 'Was due 20.11.2026'],
  ['learning.myCourses.start', {}, 'Starten', 'Start'],
  ['learning.myCourses.continue', {}, 'Fortsetzen', 'Continue'],
  ['learning.myCourses.startCourse', title, 'Starten: Hygiene in der Apotheke', 'Start: Hygiene in der Apotheke'],
  [
    'learning.myCourses.continueCourse',
    title,
    'Fortsetzen: Hygiene in der Apotheke',
    'Continue: Hygiene in der Apotheke',
  ],
  [
    'learning.myCourses.locked',
    {},
    'Der Test ist gesperrt. Arbeiten Sie die Inhalte erneut durch, um ihn wieder freizuschalten.',
    'The test is locked. Work through the content again to unlock it.',
  ],
  [
    'learning.myCourses.notStartable',
    {},
    'Diese Schulung kann derzeit nicht gestartet werden: ihre Version ist zurückgezogen.',
    'This course cannot be started at the moment: its version has been retired.',
  ],
  [
    'learning.myCourses.empty',
    pharmacy,
    'Ihnen ist in Apotheke am Markt derzeit keine Schulung zugewiesen.',
    'No course is currently assigned to you in Apotheke am Markt.',
  ],
  [
    'learning.myCourses.pharmacyNotFound',
    {},
    'Sie sind nicht mehr Mitglied dieser Apotheke.',
    'You are no longer a member of this pharmacy.',
  ],
  [
    'learning.myCourses.identityIncomplete',
    {},
    'Ihr Konto nennt keinen Namen oder keine gültige E-Mail-Adresse. Ergänzen Sie Ihr Profil.',
    'Your account gives no name or valid email address. Complete your profile.',
  ],
  [
    'learning.myCourses.loadFailed',
    {},
    'Ihre Schulungen konnten nicht geladen werden.',
    'Your courses could not be loaded.',
  ],
  ['learning.myCourses.retry', {}, 'Erneut versuchen', 'Try again'],
];

describe('says', () => {
  test.each(texts)('%s in German', (key, values, german) => {
    expect(t(key, values)).toBe(german);
  });

  test.each(texts)('%s in English', (key, values, _german, english) => {
    setLanguage('en-GB', { remember: false });

    expect(t(key, values)).toBe(english);
  });
});
