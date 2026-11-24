import { describe, expect, test } from 'vitest';
import { de } from '@/i18n/de';
import { en } from '@/i18n/en';
import { i18n, setLanguage, t } from '@/i18n';

// Every text of the learner's test and result pages (#48), word for word as catalog-keys.md gives them, in German
// (formal Sie, the glossary's Test, Testversuch, Kapitel, Teilnahmebestätigung) and English. Each is a key in both
// catalogs; the pages say nothing that is not one.

/** The message under a dotted key path in a catalog, as the catalog writes it; undefined without one. */
function messageOf(catalog: object, key: string): unknown {
  let node: unknown = catalog;
  for (const segment of key.split('.')) {
    if (typeof node !== 'object' || node === null) {
      return undefined;
    }
    node = (node as Record<string, unknown>)[segment];
  }
  return node;
}

const texts: [string, string, string][] = [
  ['modules.assessment.details.test.title', 'Test', 'Test'],
  ['modules.assessment.details.result.title', 'Ergebnis', 'Result'],
  ['learning.player.toTest', 'Zum Test', 'Go to the test'],
  ['assessment.test.heading', 'Test: {title}', 'Test: {title}'],
  ['assessment.test.headingFallback', 'Test', 'Test'],
  ['assessment.test.loadFailed', 'Der Test konnte nicht geladen werden.', 'The test could not be loaded.'],
  ['assessment.test.notFound', 'Diesen Test gibt es nicht.', 'This test does not exist.'],
  [
    'assessment.test.noIdentity',
    'Ihr Konto ist unvollständig. Bitte melden Sie sich erneut an.',
    'Your account is incomplete. Please sign in again.',
  ],
  [
    'assessment.test.notUnlocked',
    'Der Test ist noch nicht freigeschaltet. Bearbeiten Sie zuerst alle Kapitel der Schulung; danach erscheint er hier nach wenigen Sekunden.',
    'The test is not unlocked yet. Work through every chapter of the course first; it then appears here within a few seconds.',
  ],
  ['assessment.test.toCourses', 'Zu meinen Schulungen', 'To my courses'],
  [
    'assessment.test.notReady',
    'Der Test wird gerade bereitgestellt. Versuchen Sie es in einem Moment erneut.',
    'The test is being prepared. Please try again in a moment.',
  ],
  [
    'assessment.test.intro',
    'Versuch {number} von {max}. Es gibt kein Zeitlimit; jede Antwort wird sofort gespeichert.',
    'Attempt {number} of {max}. There is no time limit; every answer is saved at once.',
  ],
  ['assessment.test.start', 'Test beginnen', 'Start the test'],
  ['assessment.test.startFailed', 'Der Test konnte nicht begonnen werden.', 'The test could not be started.'],
  ['assessment.test.questions', 'Fragen', 'Questions'],
  ['assessment.test.question', 'Frage {number} von {total}', 'Question {number} of {total}'],
  ['assessment.test.singleHint', 'Wählen Sie eine Antwort.', 'Choose one answer.'],
  ['assessment.test.multipleHint', 'Wählen Sie alle zutreffenden Antworten.', 'Choose every answer that applies.'],
  ['assessment.test.answered', '{answered} von {total} Fragen beantwortet', '{answered} of {total} questions answered'],
  ['assessment.test.save.saving', 'Wird gespeichert …', 'Saving …'],
  ['assessment.test.save.saved', 'Gespeichert', 'Saved'],
  ['assessment.test.save.announced', 'Antwort auf Frage {number} gespeichert.', 'Answer to question {number} saved.'],
  [
    'assessment.test.save.failed',
    'Ihre Antwort auf Frage {number} konnte nicht gespeichert werden.',
    'Your answer to question {number} could not be saved.',
  ],
  ['assessment.test.save.retry', 'Erneut speichern', 'Save again'],
  [
    'assessment.test.save.keepOne',
    'Wählen Sie mindestens eine Antwort. Gespeichert bleibt Ihre vorige Auswahl.',
    'Choose at least one answer. Your previous choice stays saved.',
  ],
  [
    'assessment.test.save.reload',
    'Diese Antwort passt nicht zu Ihrem Testversuch. Laden Sie die Seite neu.',
    'This answer does not match your attempt. Please reload the page.',
  ],
  [
    'assessment.test.save.singleChoice',
    'Wählen Sie bei dieser Frage genau eine Antwort.',
    'Choose exactly one answer to this question.',
  ],
  ['assessment.test.submit', 'Test abgeben', 'Submit the test'],
  ['assessment.test.confirm.title', 'Test abgeben?', 'Submit the test?'],
  [
    'assessment.test.confirm.text',
    'Sie haben {answered} von {total} Fragen beantwortet. Nach der Abgabe können Sie Ihre Antworten nicht mehr ändern.',
    'You have answered {answered} of {total} questions. After submitting you can no longer change your answers.',
  ],
  [
    'assessment.test.confirm.unanswered',
    'Unbeantwortete Fragen werden als falsch gewertet.',
    'Unanswered questions count as wrong.',
  ],
  ['assessment.test.confirm.confirm', 'Abgeben', 'Submit'],
  ['assessment.test.confirm.cancel', 'Weiter bearbeiten', 'Keep working'],
  ['assessment.test.submitFailed', 'Der Test konnte nicht abgegeben werden.', 'The test could not be submitted.'],
  [
    'assessment.test.changed',
    'Der Test wurde gerade an anderer Stelle geändert. Versuchen Sie es erneut.',
    'The test was just changed elsewhere. Please try again.',
  ],
  [
    'assessment.test.unsaved',
    'Nicht alle Antworten sind gespeichert. Speichern Sie sie erneut, bevor Sie abgeben.',
    'Not every answer is saved. Save them again before you submit.',
  ],
  [
    'assessment.test.alreadySubmitted',
    'Dieser Testversuch ist bereits abgegeben.',
    'This attempt has already been submitted.',
  ],
  ['assessment.test.passed', 'Sie haben diesen Test bestanden.', 'You have passed this test.'],
  [
    'assessment.test.exhausted',
    'Der Test ist gesperrt, bis Sie die genannten Kapitel erneut bearbeitet haben.',
    'The test is locked until you have worked through the chapters named again.',
  ],
  ['assessment.test.toResult', 'Zum Ergebnis', 'See the result'],
  ['assessment.result.heading', 'Ergebnis: {title}', 'Result: {title}'],
  ['assessment.result.loadFailed', 'Das Ergebnis konnte nicht geladen werden.', 'The result could not be loaded.'],
  ['assessment.result.none', 'Sie haben noch keinen Testversuch abgegeben.', 'You have not submitted an attempt yet.'],
  ['assessment.result.toTest', 'Zum Test', 'Go to the test'],
  ['assessment.result.outcome.Passed', 'Bestanden', 'Passed'],
  ['assessment.result.outcome.Failed', 'Nicht bestanden', 'Failed'],
  [
    'assessment.result.score',
    'Ihr Ergebnis: {score}; zum Bestehen nötig: {passing}',
    'Your score: {score}; needed to pass: {passing}',
  ],
  ['assessment.result.attempt', 'Versuch {number}', 'Attempt {number}'],
  [
    'assessment.result.attemptsLeft',
    'Noch ein Versuch | Noch {count} Versuche',
    'One attempt left | {count} attempts left',
  ],
  ['assessment.result.retry', 'Nächsten Versuch beginnen', 'Start the next attempt'],
  [
    'assessment.result.repeatRecommended',
    'Wiederholen Sie vor dem nächsten Versuch diese Kapitel:',
    'Before your next attempt, go over these chapters:',
  ],
  [
    'assessment.result.locked',
    'Sie haben alle Versuche aufgebraucht; der Test ist gesperrt. Bearbeiten Sie diese Kapitel erneut, dann wird er wieder freigeschaltet:',
    'You have used every attempt; the test is locked. Work through these chapters again and it unlocks:',
  ],
  [
    'assessment.result.chaptersFailed',
    'Die Kapitel konnten nicht geladen werden.',
    'The chapters could not be loaded.',
  ],
  ['assessment.result.unknownChapter', 'Ein Kapitel dieser Schulung', 'A chapter of this course'],
  ['assessment.result.passedNext', 'Ihre Teilnahmebestätigung wird ausgestellt.', 'Your certificate is being issued.'],
];

test.each(texts)('%s says its German text', (key, german) => {
  expect(messageOf(de, key)).toBe(german);
});

test.each(texts)('%s says its English text', (key, _german, english) => {
  expect(messageOf(en, key)).toBe(english);
});

// The attempts left are a plural: "Noch ein Versuch" for one, "Noch {count} Versuche" for more.
describe.each([
  ['de-DE', 'Noch ein Versuch', 'Noch 2 Versuche'],
  ['en-GB', 'One attempt left', '2 attempts left'],
] as const)('in %s', (language, one, two) => {
  test('the attempts left are counted', () => {
    setLanguage(language, { remember: false });

    expect(t('assessment.result.attemptsLeft', { count: 1 }, 1)).toBe(one);
    expect(t('assessment.result.attemptsLeft', { count: 2 }, 2)).toBe(two);
  });
});

// Percentages are numbers the language formats (ADR 0007): the score says them through n(x / 100, 'percent').
test('the score names both percentages as the language writes them', () => {
  const score = i18n.global.n(0.5, 'percent');
  const passing = i18n.global.n(0.8, 'percent');

  expect(t('assessment.result.score', { score, passing })).toBe(
    `Ihr Ergebnis: ${score}; zum Bestehen nötig: ${passing}`,
  );
});
