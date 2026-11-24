import { describe, expect, test } from 'vitest';
import { ApiError } from '@/api/baseApi';
import { setLanguage } from '@/i18n';
import { type AssessmentAction, assessmentFailure, assessmentFailureText } from './assessmentErrors';

// What the test and result pages (#48) say when a read, a start, an answer or a submit is refused or fails, following
// workflowErrors.ts: a module code the page knows says a catalog key's text, chosen by ApiError.code (ADR 0007); a
// module's refusal with a code the page does not know says the host's words (ApiError.serverMessage); anything else, a
// network failure included, says the action failed. An answer's own failure names its question: { number }.
//
// assessmentFailure(action, error) answers { key } or { serverMessage }; assessmentFailureText(failure, values?) says
// it in the language of the moment.

/** Problem details as the host sends them, with a code where it has one. */
const problem = (status: number, code?: string) =>
  JSON.stringify({ title: 'Refused', status, detail: 'Words of the host, not for the user.', code });

const refusal = (status: number, code?: string) => () => new ApiError(status, problem(status, code));

const everyAction: AssessmentAction[] = ['read', 'result', 'start', 'answer', 'submit'];

/** One case per code the spec names, on each action where the host can answer it. */
const known: [string, number, AssessmentAction[], string, string, string][] = [
  [
    'assessment.examination-not-found',
    404,
    everyAction,
    'assessment.test.notUnlocked',
    'Der Test ist noch nicht freigeschaltet. Bearbeiten Sie zuerst alle Kapitel der Schulung; danach erscheint er hier nach wenigen Sekunden.',
    'The test is not unlocked yet. Work through every chapter of the course first; it then appears here within a few seconds.',
  ],
  [
    'assessment.identity-incomplete',
    403,
    everyAction,
    'assessment.test.noIdentity',
    'Ihr Konto ist unvollständig. Bitte melden Sie sich erneut an.',
    'Your account is incomplete. Please sign in again.',
  ],
  [
    'assessment.examination-not-unlocked',
    409,
    ['start'],
    'assessment.test.notUnlocked',
    'Der Test ist noch nicht freigeschaltet. Bearbeiten Sie zuerst alle Kapitel der Schulung; danach erscheint er hier nach wenigen Sekunden.',
    'The test is not unlocked yet. Work through every chapter of the course first; it then appears here within a few seconds.',
  ],
  [
    'assessment.test-definition-missing',
    409,
    ['start'],
    'assessment.test.notReady',
    'Der Test wird gerade bereitgestellt. Versuchen Sie es in einem Moment erneut.',
    'The test is being prepared. Please try again in a moment.',
  ],
  [
    'assessment.no-open-attempt',
    409,
    ['answer', 'submit'],
    'assessment.test.alreadySubmitted',
    'Dieser Testversuch ist bereits abgegeben.',
    'This attempt has already been submitted.',
  ],
  [
    'assessment.question-not-in-attempt',
    404,
    ['answer'],
    'assessment.test.save.reload',
    'Diese Antwort passt nicht zu Ihrem Testversuch. Laden Sie die Seite neu.',
    'This answer does not match your attempt. Please reload the page.',
  ],
  [
    'assessment.option-not-in-question',
    422,
    ['answer'],
    'assessment.test.save.reload',
    'Diese Antwort passt nicht zu Ihrem Testversuch. Laden Sie die Seite neu.',
    'This answer does not match your attempt. Please reload the page.',
  ],
  [
    'assessment.single-choice-takes-one-option',
    422,
    ['answer'],
    'assessment.test.save.singleChoice',
    'Wählen Sie bei dieser Frage genau eine Antwort.',
    'Choose exactly one answer to this question.',
  ],
  [
    'assessment.examination-changed',
    409,
    ['start', 'answer', 'submit'],
    'assessment.test.changed',
    'Der Test wurde gerade an anderer Stelle geändert. Versuchen Sie es erneut.',
    'The test was just changed elsewhere. Please try again.',
  ],
];

const knownCases = known.flatMap(([code, status, actions, key, german, english]) =>
  actions.map((action): [string, AssessmentAction, () => unknown, string, string, string] => [
    `${status} ${code}`,
    action,
    refusal(status, code),
    key,
    german,
    english,
  ]),
);

/** Each action's own text when it failed for a reason the page has no words of its own for. */
const failed: [AssessmentAction, string, string, string][] = [
  ['read', 'assessment.test.loadFailed', 'Der Test konnte nicht geladen werden.', 'The test could not be loaded.'],
  [
    'result',
    'assessment.result.loadFailed',
    'Das Ergebnis konnte nicht geladen werden.',
    'The result could not be loaded.',
  ],
  ['start', 'assessment.test.startFailed', 'Der Test konnte nicht begonnen werden.', 'The test could not be started.'],
  [
    'answer',
    'assessment.test.save.failed',
    'Ihre Antwort auf Frage 3 konnte nicht gespeichert werden.',
    'Your answer to question 3 could not be saved.',
  ],
  [
    'submit',
    'assessment.test.submitFailed',
    'Der Test konnte nicht abgegeben werden.',
    'The test could not be submitted.',
  ],
];

/** Failures the page has no words of its own for. */
const fallbacks: [string, () => unknown][] = [
  ['a network failure (TypeError)', () => new TypeError('Failed to fetch')],
  ['a 500', () => new ApiError(500, problem(500))],
  ['a 502 with a module code', refusal(502, 'assessment.examination-changed')],
  ['a 401', () => new ApiError(401, '')],
  ["a policy's 403 without a code", refusal(403)],
  // The page never sends an empty or duplicate set; if it did, the guard's 400 is no module's code.
  ['a 400 invalid-argument', refusal(400, 'invalid-argument')],
  ["the unique index's 409 already-exists", refusal(409, 'already-exists')],
  [
    'a 409 with a module code the page does not know but no words',
    () => new ApiError(409, JSON.stringify({ status: 409, code: 'assessment.something-new' })),
  ],
  ['an error that is no error at all', () => 'nothing'],
];

const fallbackCases = failed.flatMap(([action, key, german, english]) =>
  fallbacks.map(([answer, error]): [string, AssessmentAction, () => unknown, string, string, string] => [
    answer,
    action,
    error,
    key,
    german,
    english,
  ]),
);

/** The question an answer's failure names. */
const values = { number: 3 };

describe('in German', () => {
  test.each(knownCases)('%s on %s says its own text', (_answer, action, error, key, german) => {
    expect(assessmentFailure(action, error())).toEqual({ key });
    expect(assessmentFailureText(assessmentFailure(action, error()), values)).toBe(german);
  });

  test.each(fallbackCases)('%s on %s says the action failed', (_answer, action, error, key, german) => {
    expect(assessmentFailure(action, error())).toEqual({ key });
    expect(assessmentFailureText(assessmentFailure(action, error()), values)).toBe(german);
  });
});

describe('in English', () => {
  test.each(knownCases)('%s on %s says its own text', (_answer, action, error, key, _german, english) => {
    setLanguage('en-GB', { remember: false });

    expect(assessmentFailure(action, error())).toEqual({ key });
    expect(assessmentFailureText(assessmentFailure(action, error()), values)).toBe(english);
  });

  test.each(fallbackCases)('%s on %s says the action failed', (_answer, action, error, key, _german, english) => {
    setLanguage('en-GB', { remember: false });

    expect(assessmentFailure(action, error())).toEqual({ key });
    expect(assessmentFailureText(assessmentFailure(action, error()), values)).toBe(english);
  });
});

/** A module's refusals with a code the page has no words of its own for: the host's words, as ADR 0007 says. */
const unknownRefusals: [string, () => unknown, string][] = [
  [
    'a 409 assessment.something-new with a title',
    () =>
      new ApiError(
        409,
        JSON.stringify({ title: 'Der Test ist gesperrt.', status: 409, code: 'assessment.something-new' }),
      ),
    'Der Test ist gesperrt.',
  ],
  [
    'a 422 assessment.something-new with a detail',
    () =>
      new ApiError(
        422,
        JSON.stringify({
          title: 'Unprocessable',
          detail: 'Frage 3 ist zu lang.',
          status: 422,
          code: 'assessment.something-new',
        }),
      ),
    'Frage 3 ist zu lang.',
  ],
];

describe.each(['de-DE', 'en-GB'] as const)('in %s', (language) => {
  test.each(
    unknownRefusals.flatMap(([answer, error, words]) =>
      everyAction.map((action): [string, AssessmentAction, () => unknown, string] => [answer, action, error, words]),
    ),
  )('%s on %s says what the host said', (_answer, action, error, words) => {
    setLanguage(language, { remember: false });

    expect(assessmentFailure(action, error())).toEqual({ serverMessage: words });
    expect(assessmentFailureText(assessmentFailure(action, error()), values)).toBe(words);
  });
});

test("the host's words are never what the page says for a code it knows or a failure without a module's words", () => {
  for (const [, action, error] of [...knownCases, ...fallbackCases]) {
    expect(assessmentFailureText(assessmentFailure(action, error()), values)).not.toContain('Words of the host');
  }
});
