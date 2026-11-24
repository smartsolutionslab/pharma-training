import { describe, expect, test } from 'vitest';
import { ApiError } from '@/api/baseApi';
import { setLanguage, t } from '@/i18n';
import { type WorkflowAction, workflowFailure, workflowFailureText } from './workflowErrors';

// What the course version page says when a step of the workflow (#26: submit, publish, return, retire) is refused or
// fails, as an ErrorMessage of src/i18n/errorText.ts (#147): the page's own key where its words depend on the step
// (not-a-draft, not-published, course-version-changed, the three defects of a question publishing finds, a return's
// refused reason, which states the host's whole rule for a reason), else the catalog's errors.<code>; a policy's 403
// without a code says the editor may not take the step. Only a module code the catalog does not know says the host's words; anything else, a network failure
// included, says the step failed.

/** Problem details as the host sends them, with a code where it has one. */
const problem = (status: number, code?: string) =>
  JSON.stringify({ title: 'Refused', status, detail: 'Words of the host, not for the user.', code });

const refusal = (status: number, code?: string) => () => new ApiError(status, problem(status, code));

const answers: [string, WorkflowAction, () => unknown, string, string, string][] = [
  [
    '403 identity-incomplete',
    'submit',
    refusal(403, 'editorial.identity-incomplete'),
    'errors.editorial.identity-incomplete',
    'Ihre Anmeldung nennt keine Person. Melden Sie sich erneut an.',
    'Your sign-in names no person. Please sign in again.',
  ],
  [
    '404 course-version-not-found',
    'retire',
    refusal(404, 'editorial.course-version-not-found'),
    'errors.editorial.course-version-not-found',
    'Diese Version gibt es nicht mehr.',
    'This version no longer exists.',
  ],
  [
    '409 course-version-changed',
    'publish',
    refusal(409, 'editorial.course-version-changed'),
    'editorial.workflow.errors.changed',
    'Die Version wurde inzwischen geändert. Sie sehen jetzt den aktuellen Stand; versuchen Sie es erneut.',
    'The version has changed in the meantime. You now see its current state; please try again.',
  ],
  [
    '409 concurrent-save',
    'publish',
    refusal(409, 'editorial.concurrent-save'),
    'errors.editorial.concurrent-save',
    'Gleichzeitig wurde eine andere Änderung an der Version gespeichert; es wurde nichts gespeichert. Versuchen Sie es erneut.',
    'Another change to the version was saved at the same moment; nothing was saved. Try again.',
  ],
  [
    '409 not-a-draft',
    'submit',
    refusal(409, 'editorial.not-a-draft'),
    'editorial.workflow.errors.notADraft',
    'Nur ein Entwurf kann eingereicht werden.',
    'Only a draft can be submitted.',
  ],
  [
    '409 no-chapters',
    'submit',
    refusal(409, 'editorial.no-chapters'),
    'errors.editorial.no-chapters',
    'Die Version braucht mindestens ein Kapitel.',
    'The version needs at least one chapter.',
  ],
  [
    '409 too-many-questions',
    'submit',
    refusal(409, 'editorial.too-many-questions'),
    'errors.editorial.too-many-questions',
    'Ein Test hat höchstens 50 Fragen.',
    'A test has at most 50 questions.',
  ],
  [
    '409 test-incomplete',
    'publish',
    refusal(409, 'editorial.test-incomplete'),
    'errors.editorial.test-incomplete',
    'Der Test hat nicht die vorgegebene Anzahl Fragen.',
    'The test does not have the number of questions its blueprint asks for.',
  ],
  [
    '409 not-in-review on a return',
    'return',
    refusal(409, 'editorial.not-in-review'),
    'errors.editorial.not-in-review',
    'Die Version ist nicht mehr in Prüfung.',
    'The version is no longer in review.',
  ],
  [
    '409 not-in-review on a publish',
    'publish',
    refusal(409, 'editorial.not-in-review'),
    'errors.editorial.not-in-review',
    'Die Version ist nicht mehr in Prüfung.',
    'The version is no longer in review.',
  ],
  [
    '403 four-eyes',
    'publish',
    refusal(403, 'editorial.four-eyes'),
    'errors.editorial.four-eyes',
    'Mit Vier-Augen-Freigabe darf nicht veröffentlichen, wer die Version eingereicht oder ihre Testvorgaben zuletzt geändert hat. Bitten Sie eine andere Person aus der Redaktion.',
    'Four-eyes approval does not let whoever submitted the version or last changed its test blueprint publish it. Ask another editor.',
  ],
  [
    '403 publisher-approved-questions',
    'publish',
    refusal(403, 'editorial.publisher-approved-questions'),
    'errors.editorial.publisher-approved-questions',
    'Mit Vier-Augen-Freigabe darf nicht veröffentlichen, wer Fragen dieser Version freigegeben hat. Bitten Sie eine andere Person aus der Redaktion.',
    'Four-eyes approval does not let whoever approved questions of this version publish it. Ask another editor.',
  ],
  [
    '409 question-not-approved',
    'publish',
    refusal(409, 'editorial.question-not-approved'),
    'errors.editorial.question-not-approved',
    'Jede Frage muss vor der Veröffentlichung freigegeben sein.',
    'Every question must be approved before publishing.',
  ],
  ...['too-few-options', 'single-choice-one-correct', 'multiple-choice-a-correct'].map(
    (code): [string, WorkflowAction, () => unknown, string, string, string] => [
      `422 ${code}`,
      'publish',
      refusal(422, `editorial.${code}`),
      'editorial.workflow.errors.questionDefect',
      'Eine Frage ist unvollständig. Prüfen Sie im Test ihre Antwortoptionen und richtigen Antworten.',
      'A question is incomplete. Check its answer options and correct answers in the test.',
    ],
  ),
  [
    '409 not-published',
    'retire',
    refusal(409, 'editorial.not-published'),
    'editorial.workflow.errors.notPublished',
    'Nur eine veröffentlichte Version kann zurückgezogen werden.',
    'Only a published version can be retired.',
  ],
  [
    '400 invalid-return-reason, the reason field’s message stating the host’s whole rule',
    'return',
    refusal(400, 'editorial.invalid-return-reason'),
    'editorial.workflow.returnForm.reasonRefused',
    'Der Grund muss ohne Leerraum am Anfang und Ende 1 bis 1.000 Zeichen lang sein und darf außer Zeilenumbrüchen und Tabulatoren keine Steuer- oder Formatzeichen enthalten.',
    'The reason must be 1 to 1,000 characters long without leading or trailing spaces, and may contain no control or format characters other than line breaks and tabs.',
  ],
  [
    '403 reviewer-name-missing, a code the page has no words of its own for',
    'publish',
    refusal(403, 'editorial.reviewer-name-missing'),
    'errors.editorial.reviewer-name-missing',
    'Ihr Konto nennt keinen anzeigbaren Namen. Ergänzen Sie Ihr Profil und melden Sie sich erneut an.',
    'Your account gives no name that can be shown. Complete your profile and sign in again.',
  ],
  [
    '400 invalid-status',
    'retire',
    refusal(400, 'editorial.invalid-status'),
    'errors.editorial.invalid-status',
    'Diesen Status gibt es nicht.',
    'This status does not exist.',
  ],
  ...(['submit', 'publish', 'return', 'retire'] as const).map(
    (action): [string, WorkflowAction, () => unknown, string, string, string] => [
      `a policy's 403 without a code on ${action}`,
      action,
      refusal(403),
      'editorial.workflow.errors.forbidden',
      'Sie dürfen diesen Schritt nicht ausführen.',
      'You are not allowed to take this step.',
    ],
  ),
];

/** Each action's own text when its step failed for a reason the page has no words of its own for. */
const failed: [WorkflowAction, string, string, string][] = [
  [
    'submit',
    'editorial.workflow.errors.submitFailed',
    'Die Version konnte nicht eingereicht werden. Versuchen Sie es erneut.',
    'The version could not be submitted. Please try again.',
  ],
  [
    'publish',
    'editorial.workflow.errors.publishFailed',
    'Die Version konnte nicht veröffentlicht werden. Versuchen Sie es erneut.',
    'The version could not be published. Please try again.',
  ],
  [
    'return',
    'editorial.workflow.errors.returnFailed',
    'Die Version konnte nicht zurückgegeben werden. Versuchen Sie es erneut.',
    'The version could not be returned. Please try again.',
  ],
  [
    'retire',
    'editorial.workflow.errors.retireFailed',
    'Die Version konnte nicht zurückgezogen werden. Versuchen Sie es erneut.',
    'The version could not be retired. Please try again.',
  ],
];

/** Failures the page has no words of its own for. */
const fallbacks: [string, () => unknown][] = [
  ['a network failure (TypeError)', () => new TypeError('Failed to fetch')],
  ['a 500', () => new ApiError(500, problem(500))],
  ['a 401', () => new ApiError(401, '')],
  ['a 413 without a code', () => new ApiError(413, '')],
  ['a 502 with a module code', refusal(502, 'editorial.something-else')],
  // A step creates nothing: the catalog's already-exists text, that something was created, would be wrong here.
  ["the unique index's 409 already-exists", refusal(409, 'already-exists')],
  [
    'a 409 with a module code the page does not know but no words',
    () => new ApiError(409, JSON.stringify({ status: 409, code: 'editorial.something-new' })),
  ],
  ['an error that is no error at all', () => 'nothing'],
];

const fallbackCases = failed.flatMap(([action, key, german, english]) =>
  fallbacks.map(([answer, error]): [string, WorkflowAction, () => unknown, string, string, string] => [
    answer,
    action,
    error,
    key,
    german,
    english,
  ]),
);

describe('in German', () => {
  test.each(answers)('%s on %s says its own text', (_answer, action, error, key, german) => {
    expect(workflowFailure(action, error())).toMatchObject({ key });
    expect(workflowFailureText(workflowFailure(action, error()))).toBe(german);
  });

  test.each(fallbackCases)('%s on %s says the step failed', (_answer, action, error, key, german) => {
    expect(workflowFailure(action, error())).toMatchObject({ key });
    expect(workflowFailureText(workflowFailure(action, error()))).toBe(german);
  });
});

describe('in English', () => {
  test.each(answers)('%s on %s says its own text', (_answer, action, error, key, _german, english) => {
    setLanguage('en-GB', { remember: false });

    expect(workflowFailure(action, error())).toMatchObject({ key });
    expect(workflowFailureText(workflowFailure(action, error()))).toBe(english);
  });

  test.each(fallbackCases)('%s on %s says the step failed', (_answer, action, error, key, _german, english) => {
    setLanguage('en-GB', { remember: false });

    expect(workflowFailure(action, error())).toMatchObject({ key });
    expect(workflowFailureText(workflowFailure(action, error()))).toBe(english);
  });
});

/** A module's refusals with a code the page has no words of its own for: the host's words, as ADR 0007 says. */
const unknownRefusals: [string, () => unknown, string][] = [
  [
    'a 409 editorial.something-new with a title',
    () =>
      new ApiError(
        409,
        JSON.stringify({ title: 'Die Version ist gesperrt.', status: 409, code: 'editorial.something-new' }),
      ),
    'Die Version ist gesperrt.',
  ],
  [
    'a 422 editorial.something-new with a detail',
    () =>
      new ApiError(
        422,
        JSON.stringify({
          title: 'Unprocessable',
          detail: 'Kapitel 3 ist leer.',
          status: 422,
          code: 'editorial.something-new',
        }),
      ),
    'Kapitel 3 ist leer.',
  ],
  [
    'a 403 editorial.something-new with a title',
    () => new ApiError(403, JSON.stringify({ title: 'Gesperrt.', status: 403, code: 'editorial.something-new' })),
    'Gesperrt.',
  ],
];

describe.each(['de-DE', 'en-GB'] as const)('in %s', (language) => {
  test.each(
    unknownRefusals.flatMap(([answer, error, words]) =>
      (['submit', 'publish', 'return', 'retire'] as const).map(
        (action): [string, WorkflowAction, () => unknown, string] => [answer, action, error, words],
      ),
    ),
  )('%s on %s says what the host said', (_answer, action, error, words) => {
    setLanguage(language, { remember: false });

    expect(workflowFailure(action, error())).toEqual({ serverMessage: words });
    expect(workflowFailureText(workflowFailure(action, error()))).toBe(words);
  });
});

test("the host's words are never what the page says for a code it knows or a failure without a module's words", () => {
  for (const [, action, error] of [...answers, ...fallbackCases]) {
    expect(workflowFailureText(workflowFailure(action, error()))).not.toContain('Words of the host');
  }
});

// no-chapters folds into the catalog's errors.editorial.no-chapters, with the checklist's text.
test('a refused submit without chapters says what the checklist says', () => {
  expect(t('errors.editorial.no-chapters')).toBe(t('editorial.workflow.checklist.chaptersMissing'));
});

// The host refuses a reason for more than a control character (ADR 0013): missing, empty or over 1000 characters once
// trimmed, or holding a control or format character other than a line break or a tab. The page cannot tell which, so
// the reason field states the whole rule (#201); reasonInvalid stays the client's own check for such a character.
describe('a return refused for its reason (400 invalid-return-reason)', () => {
  const reasonRefused = 'editorial.workflow.returnForm.reasonRefused';
  const rule = {
    de: 'Der Grund muss ohne Leerraum am Anfang und Ende 1 bis 1000 Zeichen lang sein und darf außer Zeilenumbrüchen und Tabulatoren keine Steuer- oder Formatzeichen enthalten.',
    en: 'The reason must be 1 to 1000 characters long without leading or trailing spaces, and may contain no control or format characters other than line breaks and tabs.',
  };

  test("is the reason field's message stating the host's whole rule", () => {
    expect(workflowFailure('return', refusal(400, 'editorial.invalid-return-reason')())).toEqual({
      key: reasonRefused,
      values: { max: 1000 },
    });
  });

  test('states the rule with its maximum in German', () => {
    const key: string = reasonRefused;

    expect(t(key, { max: 1000 })).toBe(rule.de);
  });

  test('states the rule with its maximum in English', () => {
    setLanguage('en-GB', { remember: false });
    const key: string = reasonRefused;

    expect(t(key, { max: 1000 })).toBe(rule.en);
  });

  test("keeps reasonInvalid for the client's own check of a control or format character", () => {
    const key: string = 'editorial.workflow.returnForm.reasonInvalid';

    expect(t(key)).toBe('Der Grund enthält unzulässige Steuer- oder Formatzeichen.');
  });
});

// A failure is an ErrorMessage of src/i18n/errorText.ts: its key with the values errorValues has for the code.
test('a refused step is the catalog key of its code with the limit it names', () => {
  expect(workflowFailure('submit', refusal(409, 'editorial.too-many-questions')())).toEqual({
    key: 'errors.editorial.too-many-questions',
    values: { max: 50 },
  });
});

// The host's words of a code the catalog knows never show, in either language (#147).
test.each(['de-DE', 'en-GB'] as const)(
  'a 403 reviewer-name-missing with the host words says the catalog text in %s, never the host words',
  (language) => {
    setLanguage(language, { remember: false });
    const error = new ApiError(
      403,
      JSON.stringify({
        title: 'Forbidden',
        status: 403,
        detail: 'SERVER WORDS',
        code: 'editorial.reviewer-name-missing',
      }),
    );

    const text = workflowFailureText(workflowFailure('publish', error));

    expect(text).toBe(t('errors.editorial.reviewer-name-missing'));
    expect(text).not.toContain('SERVER WORDS');
  },
);
