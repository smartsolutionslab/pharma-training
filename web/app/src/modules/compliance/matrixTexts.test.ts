import { describe, expect, test } from 'vitest';
import { setLanguage, t } from '@/i18n';

// The team matrix's texts (#36, ADR 0007: "Sie", the glossary), in both languages: its shell and refusals, the rate,
// the filters, the table, the statuses and the person's sheet. The page's tests find its elements by these keys; here
// each says what it says. A plural gives its count as the third argument, as the page does.

type Text = [key: string, values: Record<string, unknown>, plural: number | undefined, german: string, english: string];

const texts: Text[] = [
  ['modules.compliance.title', {}, undefined, 'Team-Matrix', 'Team matrix'],
  [
    'compliance.matrix.intro',
    { pharmacy: 'Apotheke am Markt' },
    undefined,
    'Wer in Apotheke am Markt welche Schulung bis wann abschließen muss.',
    'Who in Apotheke am Markt has to complete which course by when.',
  ],
  [
    'compliance.matrix.notAllowed',
    {},
    undefined,
    'Sie dürfen den Schulungsstand des Teams in dieser Apotheke nicht sehen.',
    "You may not view the team's training status in this pharmacy.",
  ],
  // pharmacy-not-found and identity-incomplete fold into the catalog's errors.compliance.* (#147).
  [
    'errors.compliance.pharmacy-not-found',
    {},
    undefined,
    'Sie sind nicht mehr Mitglied dieser Apotheke.',
    'You are no longer a member of this pharmacy.',
  ],
  [
    'errors.compliance.identity-incomplete',
    {},
    undefined,
    'Ihr Konto nennt keinen Namen oder keine gültige E-Mail-Adresse. Ergänzen Sie Ihr Profil.',
    'Your account gives no name or valid email address. Complete your profile.',
  ],
  [
    'compliance.matrix.loadFailed',
    {},
    undefined,
    'Die Team-Matrix konnte nicht geladen werden.',
    'The team matrix could not be loaded.',
  ],
  ['compliance.matrix.retry', {}, undefined, 'Erneut versuchen', 'Try again'],
  [
    'compliance.matrix.table.label',
    {},
    undefined,
    'Team-Matrix: Mitglieder nach Schulungen',
    'Team matrix: members by course',
  ],
  ['compliance.matrix.filters.label', {}, undefined, 'Filter', 'Filters'],
  ['compliance.matrix.filters.course', {}, undefined, 'Schulung', 'Course'],
  ['compliance.matrix.filters.allCourses', {}, undefined, 'Alle Schulungen', 'All courses'],
  ['compliance.matrix.filters.jobFunction', {}, undefined, 'Tätigkeit', 'Job function'],
  ['compliance.matrix.filters.allJobFunctions', {}, undefined, 'Alle Tätigkeiten', 'All job functions'],
  ['compliance.matrix.filters.reset', {}, undefined, 'Filter zurücksetzen', 'Reset filters'],
  ['compliance.matrix.rate.title', {}, undefined, 'Compliance-Quote', 'Compliance rate'],
  ['compliance.matrix.rate.noValue', {}, undefined, '–', '–'],
  [
    'compliance.matrix.rate.none',
    {},
    undefined,
    'Noch keine Zuweisungen, daher keine Quote.',
    'No assignments yet, so no rate.',
  ],
  [
    'compliance.matrix.rate.summary',
    { compliant: 4, assignments: 5 },
    undefined,
    'Fristgerecht: 4 von 5',
    'On track: 4 of 5',
  ],
  ['compliance.matrix.rate.overdue', { count: 0 }, 0, 'Keine ist überfällig.', 'None is overdue.'],
  ['compliance.matrix.rate.overdue', { count: 1 }, 1, 'Eine ist überfällig.', 'One is overdue.'],
  ['compliance.matrix.rate.overdue', { count: 3 }, 3, '3 sind überfällig.', '3 are overdue.'],
  [
    'compliance.matrix.rate.scope',
    { course: 'Alle Schulungen', jobFunction: 'Alle Tätigkeiten' },
    undefined,
    'Bezogen auf: Alle Schulungen; Alle Tätigkeiten',
    'Covers: Alle Schulungen; Alle Tätigkeiten',
  ],
  [
    'compliance.matrix.rate.explanation',
    {},
    undefined,
    'Fristgerecht ist eine Zuweisung, solange sie nicht überfällig ist und ihr Abschluss noch gilt.',
    'An assignment is on track while it is not overdue and its completion is still valid.',
  ],
  ['compliance.matrix.table.title', {}, undefined, 'Team', 'Team'],
  ['compliance.matrix.table.legend', {}, undefined, 'Legende', 'Key'],
  ['compliance.matrix.table.member', {}, undefined, 'Mitglied', 'Member'],
  ['compliance.matrix.table.courseRate', { rate: '50 %' }, undefined, 'Quote 50 %', 'Rate 50 %'],
  ['compliance.matrix.table.notAssigned', {}, undefined, 'Nicht zugewiesen', 'Not assigned'],
  ['compliance.matrix.table.due', { date: '22.11.2026' }, undefined, 'fällig 22.11.2026', 'due 22.11.2026'],
  [
    'compliance.matrix.table.validUntil',
    { date: '22.11.2026' },
    undefined,
    'gültig bis 22.11.2026',
    'valid until 22.11.2026',
  ],
  [
    'compliance.matrix.table.openPerson',
    { name: 'Berta Braun' },
    undefined,
    'Berta Braun, Schulungsstand anzeigen',
    'Berta Braun, show training status',
  ],
  ['compliance.matrix.jobFunctionShort.Pharmacist', {}, undefined, 'Apotheker', 'Pharmacist'],
  ['compliance.matrix.jobFunctionShort.Pta', {}, undefined, 'PTA', 'PTA'],
  ['compliance.matrix.jobFunctionShort.Pka', {}, undefined, 'PKA', 'PKA'],
  ['compliance.matrix.jobFunctionShort.Trainee', {}, undefined, 'Azubi', 'Trainee'],
  ['compliance.matrix.jobFunctionShort.Other', {}, undefined, 'Sonstige', 'Other'],
  [
    'compliance.matrix.empty.noCourses',
    {},
    undefined,
    'In dieser Apotheke ist noch keine Schulung zugewiesen.',
    'No course has been assigned in this pharmacy yet.',
  ],
  [
    'compliance.matrix.empty.noMembers',
    { jobFunction: 'PTA' },
    undefined,
    'Niemand in dieser Apotheke hat die Tätigkeit PTA.',
    'Nobody in this pharmacy has the job function PTA.',
  ],
  ['compliance.matrix.person.count', { count: 0 }, 0, 'Keine Schulung zugewiesen', 'No course assigned'],
  ['compliance.matrix.person.count', { count: 1 }, 1, 'Eine Schulung zugewiesen', 'One course assigned'],
  ['compliance.matrix.person.count', { count: 3 }, 3, '3 Schulungen zugewiesen', '3 courses assigned'],
  ['compliance.matrix.person.due', {}, undefined, 'Fällig am', 'Due'],
  ['compliance.matrix.person.lastCompleted', {}, undefined, 'Zuletzt abgeschlossen', 'Last completed'],
  ['compliance.matrix.person.validUntil', {}, undefined, 'Gültig bis', 'Valid until'],
  ['compliance.matrix.person.never', {}, undefined, 'Noch nie', 'Never'],
  [
    'compliance.matrix.person.none',
    {},
    undefined,
    'Diesem Mitglied ist hier keine Schulung zugewiesen.',
    'No course is assigned to this member here.',
  ],
  ['compliance.status.Open', {}, undefined, 'Offen', 'Open'],
  ['compliance.status.InProgress', {}, undefined, 'In Bearbeitung', 'In progress'],
  ['compliance.status.Locked', {}, undefined, 'Gesperrt', 'Locked'],
  ['compliance.status.Overdue', {}, undefined, 'Überfällig', 'Overdue'],
  ['compliance.status.Completed', {}, undefined, 'Abgeschlossen', 'Completed'],
  ['compliance.status.CompletedLate', {}, undefined, 'Verspätet abgeschlossen', 'Completed late'],
  ['compliance.status.Expired', {}, undefined, 'Abgelaufen', 'Expired'],
];

const say = (key: string, values: Record<string, unknown>, plural: number | undefined) =>
  plural === undefined ? t(key, values) : t(key, values, plural);

describe('says', () => {
  test.each(texts)('%s (%o) in German', (key, values, plural, german) => {
    expect(say(key, values, plural)).toBe(german);
  });

  test.each(texts)('%s (%o) in English', (key, values, plural, _german, english) => {
    setLanguage('en-GB', { remember: false });

    expect(say(key, values, plural)).toBe(english);
  });
});
