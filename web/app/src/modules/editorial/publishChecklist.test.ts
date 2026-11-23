import { describe, expect, test } from 'vitest';
import type { EditorialQuestionDto } from '@/api/generated';
import { setLanguage, t } from '@/i18n';
import { approvedAi, handWritten, questionsUpTo, versionOf } from './courseTestFixtures';
import { type ChecklistItem, publishChecklist } from './publishChecklist';

// What blocks publishing a version (#26), as the course version page lists it for a draft and a version in review:
// chapters, the blueprint's number of questions, every question approved, the version submitted, and, once this page's
// publish was refused by four eyes, why. Each item is met or open, with its catalog key and the values its text names.
// The blueprint's count may come as a string (int64 in the OpenAPI document).

/** As many approved questions as asked for. */
const approved = (count: number): EditorialQuestionDto[] =>
  questionsUpTo(count).map((question) => ({ ...question, review: 'Approved' }));

const item = (items: ChecklistItem[], name: ChecklistItem['name']) => items.find((each) => each.name === name);

describe('a draft', () => {
  test('lists chapters, questions, approval and submission, in that order, and only those', () => {
    const items = publishChecklist(versionOf('Draft'));

    expect(items.map(({ name }) => name)).toEqual(['chapters', 'questions', 'approved', 'submitted']);
  });

  test('with chapters has that met', () => {
    expect(item(publishChecklist(versionOf('Draft')), 'chapters')).toEqual({
      name: 'chapters',
      met: true,
      key: 'editorial.workflow.checklist.chapters',
      values: {},
    });
  });

  test('without chapters has that open', () => {
    expect(item(publishChecklist(versionOf('Draft', { chapters: [] })), 'chapters')).toEqual({
      name: 'chapters',
      met: false,
      key: 'editorial.workflow.checklist.chaptersMissing',
      values: {},
    });
  });

  test('with fewer questions than the blueprint asks for has the questions open, naming both counts', () => {
    expect(item(publishChecklist(versionOf('Draft')), 'questions')).toEqual({
      name: 'questions',
      met: false,
      key: 'editorial.workflow.checklist.questionsMissing',
      values: { held: 3, required: 10 },
    });
  });

  test('with as many questions as the blueprint asks for has the questions met', () => {
    const version = versionOf('Draft', { questions: questionsUpTo(10) });

    expect(item(publishChecklist(version), 'questions')).toEqual({
      name: 'questions',
      met: true,
      key: 'editorial.workflow.checklist.questions',
      values: { required: 10 },
    });
  });

  test('with more questions than the blueprint asks for has the questions open, naming both counts', () => {
    const version = versionOf('Draft', { questions: questionsUpTo(12) });

    expect(item(publishChecklist(version), 'questions')).toEqual({
      name: 'questions',
      met: false,
      key: 'editorial.workflow.checklist.questionsTooMany',
      values: { held: 12, required: 10 },
    });
  });

  test('counts against a blueprint whose count the host sent as a string', () => {
    const version = versionOf('Draft', {
      questions: questionsUpTo(2),
      blueprint: { questions: '2', passingScorePercent: '80', validityMonths: '12' },
    });

    expect(item(publishChecklist(version), 'questions')).toEqual({
      name: 'questions',
      met: true,
      key: 'editorial.workflow.checklist.questions',
      values: { required: 2 },
    });
  });

  test('without questions has the questions open and no approval met', () => {
    const items = publishChecklist(versionOf('Draft', { questions: [] }));

    expect(item(items, 'questions')).toEqual({
      name: 'questions',
      met: false,
      key: 'editorial.workflow.checklist.questionsMissing',
      values: { held: 0, required: 10 },
    });
    expect(item(items, 'approved')).toEqual({
      name: 'approved',
      met: false,
      key: 'editorial.workflow.checklist.approvedNone',
      values: {},
    });
  });

  test('with no question approved has the approval open', () => {
    const version = versionOf('Draft', { questions: [handWritten] });

    expect(item(publishChecklist(version), 'approved')).toEqual({
      name: 'approved',
      met: false,
      key: 'editorial.workflow.checklist.approvedNone',
      values: {},
    });
  });

  test('with some questions approved has the approval open, naming how many of how many', () => {
    expect(item(publishChecklist(versionOf('Draft')), 'approved')).toEqual({
      name: 'approved',
      met: false,
      key: 'editorial.workflow.checklist.approvedSome',
      values: { approved: 1, held: 3 },
    });
  });

  test('with every question approved has the approval met', () => {
    const version = versionOf('Draft', { questions: [approvedAi, ...approved(2)] });

    expect(item(publishChecklist(version), 'approved')).toEqual({
      name: 'approved',
      met: true,
      key: 'editorial.workflow.checklist.approved',
      values: {},
    });
  });

  test('has the submission open', () => {
    expect(item(publishChecklist(versionOf('Draft')), 'submitted')).toEqual({
      name: 'submitted',
      met: false,
      key: 'editorial.workflow.checklist.notSubmitted',
      values: {},
    });
  });
});

describe('a version in review', () => {
  test('has the submission met', () => {
    expect(item(publishChecklist(versionOf('InReview')), 'submitted')).toEqual({
      name: 'submitted',
      met: true,
      key: 'editorial.workflow.checklist.submitted',
      values: {},
    });
  });

  test('ready to publish has every item met', () => {
    const version = versionOf('InReview', { questions: approved(10) });

    expect(publishChecklist(version).every(({ met }) => met)).toBe(true);
  });

  test('whose publish four eyes refused because the editor submitted it adds that, open, last', () => {
    const items = publishChecklist(versionOf('InReview', { questions: approved(10) }), 'fourEyes');

    expect(items.map(({ name }) => name)).toEqual(['chapters', 'questions', 'approved', 'submitted', 'fourEyes']);
    expect(items.at(-1)).toEqual({
      name: 'fourEyes',
      met: false,
      key: 'editorial.workflow.checklist.fourEyes',
      values: {},
    });
  });

  test('whose publish four eyes refused because the editor approved questions adds that, open, last', () => {
    const items = publishChecklist(versionOf('InReview', { questions: approved(10) }), 'publisherApproved');

    expect(items.map(({ name }) => name)).toEqual([
      'chapters',
      'questions',
      'approved',
      'submitted',
      'publisherApproved',
    ]);
    expect(items.at(-1)).toEqual({
      name: 'publisherApproved',
      met: false,
      key: 'editorial.workflow.checklist.publisherApproved',
      values: {},
    });
  });
});

// The texts the keys stand for, with their values, in both languages.
const texts: [string, Record<string, number>, string, string][] = [
  ['editorial.workflow.checklist.title', {}, 'Checkliste zur Veröffentlichung', 'Publishing checklist'],
  ['editorial.workflow.checklist.done', {}, 'Erfüllt:', 'Done:'],
  ['editorial.workflow.checklist.open', {}, 'Offen:', 'Open:'],
  ['editorial.workflow.checklist.chapters', {}, 'Die Version hat Kapitel.', 'The version has chapters.'],
  [
    'editorial.workflow.checklist.chaptersMissing',
    {},
    'Die Version braucht mindestens ein Kapitel.',
    'The version needs at least one chapter.',
  ],
  [
    'editorial.workflow.checklist.questions',
    { required: 10 },
    'Der Test hat alle 10 Fragen.',
    'The test has all 10 questions.',
  ],
  [
    'editorial.workflow.checklist.questionsMissing',
    { held: 3, required: 10 },
    'Der Test hat 3 von 10 Fragen.',
    'The test has 3 of 10 questions.',
  ],
  [
    'editorial.workflow.checklist.questionsTooMany',
    { held: 12, required: 10 },
    'Der Test hat 12 Fragen, vorgegeben sind 10.',
    'The test has 12 questions; its blueprint asks for 10.',
  ],
  ['editorial.workflow.checklist.approved', {}, 'Alle Fragen sind freigegeben.', 'Every question is approved.'],
  [
    'editorial.workflow.checklist.approvedNone',
    {},
    'Noch keine Frage ist freigegeben.',
    'No question is approved yet.',
  ],
  [
    'editorial.workflow.checklist.approvedSome',
    { approved: 1, held: 3 },
    '1 von 3 Fragen sind freigegeben.',
    '1 of 3 questions are approved.',
  ],
  [
    'editorial.workflow.checklist.submitted',
    {},
    'Die Version ist zur Prüfung eingereicht.',
    'The version has been submitted for review.',
  ],
  [
    'editorial.workflow.checklist.notSubmitted',
    {},
    'Die Version muss noch zur Prüfung eingereicht werden.',
    'The version still has to be submitted for review.',
  ],
  [
    'editorial.workflow.checklist.fourEyes',
    {},
    'Sie haben diese Version eingereicht. Mit Vier-Augen-Freigabe muss eine andere Person sie veröffentlichen.',
    'You submitted this version. With four eyes, someone else must publish it.',
  ],
  [
    'editorial.workflow.checklist.publisherApproved',
    {},
    'Sie haben Fragen dieser Version freigegeben. Mit Vier-Augen-Freigabe muss eine andere Person sie veröffentlichen.',
    'You approved questions of this version. With four eyes, someone else must publish it.',
  ],
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
