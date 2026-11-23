import { describe, expect, test } from 'vitest';
import { calendarDate } from '@/i18n/formats';
import { i18n, setLanguage, t } from '@/i18n';

// The assignment wizard's texts (#37, ADR 0007), in both languages. The page's tests find its elements by these keys;
// here each says what it says. A plural gives its count as the third argument, as the page does.

type Text = [key: string, values: Record<string, unknown>, plural: number | undefined, german: string, english: string];

const texts: Text[] = [
  ['modules.compliance.pages.assign.title', {}, undefined, 'Schulung zuweisen', 'Assign a course'],
  [
    'compliance.assign.notAllowed',
    {},
    undefined,
    'Sie dürfen in dieser Apotheke keine Schulungen zuweisen.',
    'You may not assign courses in this pharmacy.',
  ],
  ['compliance.assign.steps.label', {}, undefined, 'Schritte', 'Steps'],
  ['compliance.assign.steps.course', {}, undefined, 'Schulung', 'Course'],
  ['compliance.assign.steps.learners', {}, undefined, 'Teilnehmende', 'Learners'],
  ['compliance.assign.steps.due', {}, undefined, 'Fälligkeit', 'Due date'],
  ['compliance.assign.steps.review', {}, undefined, 'Prüfen', 'Review'],
  ['compliance.assign.next', {}, undefined, 'Weiter', 'Next'],
  ['compliance.assign.back', {}, undefined, 'Zurück', 'Back'],
  ['compliance.assign.submit', {}, undefined, 'Zuweisen', 'Assign'],
  ['compliance.assign.retry', {}, undefined, 'Erneut versuchen', 'Try again'],
  ['compliance.assign.course.title', {}, undefined, 'Schulung wählen', 'Choose a course'],
  ['compliance.assign.course.columns.title', {}, undefined, 'Titel', 'Title'],
  ['compliance.assign.course.columns.version', {}, undefined, 'Version', 'Version'],
  ['compliance.assign.course.columns.validity', {}, undefined, 'Gültigkeit', 'Validity'],
  ['compliance.assign.course.months', { count: 1 }, 1, 'ein Monat', 'one month'],
  ['compliance.assign.course.months', { count: 12 }, 12, '12 Monate', '12 months'],
  ['compliance.assign.course.choose', { title: 'Hygiene' }, undefined, 'Hygiene wählen', 'Choose Hygiene'],
  ['compliance.assign.course.required', {}, undefined, 'Wählen Sie eine Schulung.', 'Choose a course.'],
  [
    'compliance.assign.course.empty',
    {},
    undefined,
    'Es gibt noch keine veröffentlichten Schulungen.',
    'There are no published courses yet.',
  ],
  [
    'compliance.assign.course.loadFailed',
    {},
    undefined,
    'Die Schulungen konnten nicht geladen werden.',
    'The courses could not be loaded.',
  ],
  [
    'compliance.assign.course.gone',
    {},
    undefined,
    'Diese Schulung kann nicht mehr zugewiesen werden. Wählen Sie eine andere.',
    'This course can no longer be assigned. Choose another one.',
  ],
  ['compliance.assign.learners.title', {}, undefined, 'Teilnehmende wählen', 'Choose learners'],
  ['compliance.assign.learners.columns.name', {}, undefined, 'Name', 'Name'],
  ['compliance.assign.learners.columns.jobFunction', {}, undefined, 'Tätigkeit', 'Job function'],
  ['compliance.assign.learners.select', { name: 'Ben Becker' }, undefined, 'Ben Becker auswählen', 'Select Ben Becker'],
  [
    'compliance.assign.learners.selectJobFunction',
    { jobFunction: 'Azubi' },
    undefined,
    'Alle mit der Tätigkeit Azubi auswählen',
    'Select everyone who is Azubi',
  ],
  ['compliance.assign.learners.clear', {}, undefined, 'Auswahl aufheben', 'Clear selection'],
  ['compliance.assign.learners.count', { count: 0 }, 0, 'Niemand ausgewählt', 'Nobody selected'],
  ['compliance.assign.learners.count', { count: 1 }, 1, 'Eine Person ausgewählt', 'One person selected'],
  ['compliance.assign.learners.count', { count: 2 }, 2, '2 Personen ausgewählt', '2 people selected'],
  [
    'compliance.assign.learners.required',
    {},
    undefined,
    'Wählen Sie mindestens eine Person.',
    'Choose at least one person.',
  ],
  [
    'compliance.assign.learners.tooMany',
    { max: 100 },
    undefined,
    'Sie können höchstens 100 Personen auf einmal zuweisen.',
    'You can assign at most 100 people at a time.',
  ],
  [
    'compliance.assign.learners.empty',
    {},
    undefined,
    'Die Apotheke hat noch keine aktiven Mitglieder.',
    'The pharmacy has no active members yet.',
  ],
  ['compliance.assign.learners.invite', {}, undefined, 'Mitglieder einladen', 'Invite members'],
  ['compliance.assign.due.title', {}, undefined, 'Fälligkeit festlegen', 'Set the due date'],
  ['compliance.assign.due.date', {}, undefined, 'Fällig am', 'Due on'],
  ['compliance.assign.due.repeat', {}, undefined, 'Jährlich wiederholen', 'Repeat yearly'],
  ['compliance.assign.due.required', {}, undefined, 'Geben Sie ein Fälligkeitsdatum an.', 'Enter a due date.'],
  [
    'compliance.assign.due.inThePast',
    {},
    undefined,
    'Das Fälligkeitsdatum darf nicht in der Vergangenheit liegen.',
    'The due date cannot be in the past.',
  ],
  [
    'compliance.assign.due.tooFar',
    {},
    undefined,
    'Das Fälligkeitsdatum darf höchstens 10 Jahre in der Zukunft liegen.',
    'The due date can be at most 10 years ahead.',
  ],
  ['compliance.assign.review.title', {}, undefined, 'Zuweisung prüfen', 'Review the assignment'],
  ['compliance.assign.review.course', {}, undefined, 'Schulung', 'Course'],
  ['compliance.assign.review.learners', { count: 2 }, undefined, 'Teilnehmende (2)', 'Learners (2)'],
  ['compliance.assign.review.due', {}, undefined, 'Fällig am', 'Due on'],
  ['compliance.assign.review.recurrence', {}, undefined, 'Wiederholung', 'Recurrence'],
  ['compliance.assign.review.yearly', {}, undefined, 'Jährlich', 'Yearly'],
  ['compliance.assign.review.once', {}, undefined, 'Keine', 'None'],
  ['compliance.assign.result.title', {}, undefined, 'Ergebnis', 'Result'],
  [
    'compliance.assign.result.assigned',
    { count: 1 },
    1,
    'Die Schulung wurde einer Person zugewiesen.',
    'The course was assigned to one person.',
  ],
  [
    'compliance.assign.result.assigned',
    { count: 3 },
    3,
    'Die Schulung wurde 3 Personen zugewiesen.',
    'The course was assigned to 3 people.',
  ],
  [
    'compliance.assign.result.noneAssigned',
    {},
    undefined,
    'Die Schulung wurde niemandem zugewiesen.',
    'The course was assigned to nobody.',
  ],
  ['compliance.assign.result.refused', {}, undefined, 'Nicht zugewiesen', 'Not assigned'],
  [
    'compliance.assign.result.alreadyActive',
    { name: 'Ben Becker' },
    undefined,
    'Ben Becker: Diese Schulung ist bereits zugewiesen.',
    'Ben Becker: this course is already assigned.',
  ],
  [
    'compliance.assign.result.notAMember',
    { name: 'Ben Becker' },
    undefined,
    'Ben Becker: nicht mehr Mitglied der Apotheke.',
    'Ben Becker: no longer a member of the pharmacy.',
  ],
  [
    'compliance.assign.result.other',
    { name: 'Ben Becker', message: 'Etwas Neues.' },
    undefined,
    'Ben Becker: Etwas Neues.',
    'Ben Becker: Etwas Neues.',
  ],
  ['compliance.assign.result.again', {}, undefined, 'Weitere Schulung zuweisen', 'Assign another course'],
  ['compliance.assign.result.home', {}, undefined, 'Zur Startseite', 'Go to the start page'],
  [
    'compliance.assign.changed',
    {},
    undefined,
    'Die Zuweisungen wurden inzwischen geändert. Prüfen Sie die Angaben und weisen Sie erneut zu.',
    'The assignments have changed in the meantime. Check the details and assign again.',
  ],
  [
    'compliance.assign.failed',
    {},
    undefined,
    'Die Schulung konnte nicht zugewiesen werden. Versuchen Sie es erneut.',
    'The course could not be assigned. Please try again.',
  ],
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

// The review gives the due date in full (d(calendarDate(due), 'long')), the day's month by name.
test('the long date format names the month, in German and in English', () => {
  expect(i18n.global.d(calendarDate('2027-03-15'), 'long')).toBe('15. März 2027');
  setLanguage('en-GB', { remember: false });
  expect(i18n.global.d(calendarDate('2027-03-15'), 'long')).toBe('15 March 2027');
});
