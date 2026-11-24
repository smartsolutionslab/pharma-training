import { describe, expect, test } from 'vitest';
import { ApiError } from '@/api/baseApi';
import { setLanguage, t } from '@/i18n';
import { draftFailure } from './testDraftErrors';

// What the page says when an AI draft is refused or fails (#25, S8): the text is chosen by the problem's code where the
// host sends one (the page's own key where its words depend on the draft, else the catalog's errors.<code>, #147), by
// the status otherwise, never the host's words, and in the language the page speaks. The 503
// (model-not-configured, raised before any handler) and the 502 (ai-draft-malformed) are told apart from an edge's 503
// or 502 by the problem's code at any status (ApiError.failureCode, baseApi.test.ts); an edge's says the draft failed.
// A draft that never got an answer the page can read (a network failure, a body that is not JSON) may have been
// applied all the same: its outcome is unknown.

/** Problem details as the host sends them, with a code where it has one. */
const problem = (status: number, code?: string) =>
  JSON.stringify({ title: 'Refused', status, detail: 'Words of the host, not for the user.', code });

const answers: [string, () => unknown, Parameters<typeof t>[0], string, string][] = [
  [
    '503 model-not-configured',
    () => new ApiError(503, problem(503, 'model-not-configured')),
    'errors.model-not-configured',
    'Es ist kein Sprachmodell eingerichtet; KI-Funktionen stehen nicht zur Verfügung. Wenden Sie sich an den Betrieb der Plattform.',
    "No language model is set up, so AI features are not available. Contact the platform's operators.",
  ],
  [
    '429 editorial.model-limit-reached',
    () => new ApiError(429, problem(429, 'editorial.model-limit-reached')),
    'errors.editorial.model-limit-reached',
    'Sie haben Ihr Limit für KI-Anfragen erreicht. Versuchen Sie es später erneut.',
    'You have reached your limit for AI requests. Try again later.',
  ],
  [
    "429 without a code (an edge's)",
    () => new ApiError(429, problem(429)),
    'editorial.test.draft.errors.tooManyRequests',
    'Sie haben in kurzer Zeit zu viele KI-Entwürfe angefordert. Versuchen Sie es später erneut.',
    'You have asked for too many AI drafts in a short time. Try again later.',
  ],
  [
    '429 with a code the page does not know',
    () => new ApiError(429, problem(429, 'editorial.too-many-drafts')),
    'editorial.test.draft.errors.tooManyRequests',
    'Sie haben in kurzer Zeit zu viele KI-Entwürfe angefordert. Versuchen Sie es später erneut.',
    'You have asked for too many AI drafts in a short time. Try again later.',
  ],
  [
    '502 ai-draft-malformed',
    () => new ApiError(502, problem(502, 'editorial.ai-draft-malformed')),
    'errors.editorial.ai-draft-malformed',
    'Die Antwort der KI war unbrauchbar; es wurde nichts geändert. Versuchen Sie es erneut.',
    "The AI's answer could not be used; nothing was changed. Try again.",
  ],
  [
    '409 no-text-chapters',
    () => new ApiError(409, problem(409, 'editorial.no-text-chapters')),
    'editorial.test.draft.noTextChapters',
    'Die KI entwirft nur aus Kapiteln mit Text: aus Textkapiteln und aus PDF-Kapiteln, deren Text sich lesen lässt. Legen Sie zuerst ein solches Kapitel an.',
    'The AI drafts only from chapters with text: text chapters and PDF chapters whose text can be read. Add such a chapter first.',
  ],
  [
    '409 nothing-to-draft',
    () => new ApiError(409, problem(409, 'editorial.nothing-to-draft')),
    'editorial.test.draft.nothingToDraft',
    'Die geprüften Fragen erfüllen die Vorgaben bereits; es gibt nichts zu entwerfen.',
    'The approved questions already meet the blueprint; there is nothing to draft.',
  ],
  [
    '422 too-much-content-to-draft',
    () => new ApiError(422, problem(422, 'editorial.too-much-content-to-draft')),
    'errors.editorial.too-much-content-to-draft',
    'Die Textkapitel sind zusammen zu lang für einen KI-Entwurf; PDF-Kapitel werden dafür gekürzt, Textkapitel nicht. Kürzen Sie die Textkapitel oder schreiben Sie die Fragen von Hand.',
    'The text chapters together are too long for an AI draft; PDF chapters are cut to fit, text chapters are not. Shorten the text chapters or write the questions by hand.',
  ],
  [
    '409 course-version-changed',
    () => new ApiError(409, problem(409, 'editorial.course-version-changed')),
    'editorial.test.draft.errors.changed',
    'Die Version wurde inzwischen geändert; der Entwurf wurde verworfen. Versuchen Sie es erneut.',
    'The version changed in the meantime; the draft was discarded. Try again.',
  ],
  [
    '409 concurrent-save',
    () => new ApiError(409, problem(409, 'editorial.concurrent-save')),
    'errors.editorial.concurrent-save',
    'Gleichzeitig wurde eine andere Änderung an der Version gespeichert; es wurde nichts gespeichert. Versuchen Sie es erneut.',
    'Another change to the version was saved at the same moment; nothing was saved. Try again.',
  ],
  [
    '409 not-a-draft',
    () => new ApiError(409, problem(409, 'editorial.not-a-draft')),
    'editorial.test.draft.errors.notADraft',
    'Diese Version ist kein Entwurf mehr; ihr Test kann nicht neu entworfen werden.',
    'This version is no longer a draft; its test cannot be drafted again.',
  ],
  [
    '403 identity-incomplete, a code the page has no words of its own for',
    () => new ApiError(403, problem(403, 'editorial.identity-incomplete')),
    'errors.editorial.identity-incomplete',
    'Ihre Anmeldung nennt keine Person. Melden Sie sich erneut an.',
    'Your sign-in names no person. Please sign in again.',
  ],
  [
    '400 invalid-argument of the host',
    () => new ApiError(400, problem(400, 'invalid-argument')),
    'errors.invalid-argument',
    'Eine Angabe ist ungültig. Prüfen Sie Ihre Eingaben und versuchen Sie es erneut.',
    'A value is not valid. Check your entries and try again.',
  ],
  [
    '504 from the edge',
    () => new ApiError(504, ''),
    'editorial.test.draft.errors.timedOut',
    'Der KI-Entwurf hat zu lange gedauert. Die Fragen werden neu geladen; prüfen Sie, ob er übernommen wurde.',
    'The AI draft took too long. The questions are reloaded; check whether it was applied.',
  ],
  [
    '500 provider failure',
    () => new ApiError(500, problem(500)),
    'editorial.test.draft.errors.failed',
    'Der KI-Entwurf ist fehlgeschlagen. Versuchen Sie es erneut.',
    'The AI draft failed. Try again.',
  ],
  [
    '503 from the edge, without a code',
    () => new ApiError(503, ''),
    'editorial.test.draft.errors.failed',
    'Der KI-Entwurf ist fehlgeschlagen. Versuchen Sie es erneut.',
    'The AI draft failed. Try again.',
  ],
  [
    '503 from the edge, problem details without a code',
    () => new ApiError(503, problem(503)),
    'editorial.test.draft.errors.failed',
    'Der KI-Entwurf ist fehlgeschlagen. Versuchen Sie es erneut.',
    'The AI draft failed. Try again.',
  ],
  [
    '502 from the edge, without a code',
    () => new ApiError(502, 'Bad Gateway'),
    'editorial.test.draft.errors.failed',
    'Der KI-Entwurf ist fehlgeschlagen. Versuchen Sie es erneut.',
    'The AI draft failed. Try again.',
  ],
  [
    '502 with a code of another kind',
    () => new ApiError(502, problem(502, 'model-not-configured')),
    'editorial.test.draft.errors.failed',
    'Der KI-Entwurf ist fehlgeschlagen. Versuchen Sie es erneut.',
    'The AI draft failed. Try again.',
  ],
  [
    'a network failure: whether the host drafted is not known',
    () => new TypeError('Failed to fetch'),
    'editorial.test.draft.errors.unknown',
    'Ob der KI-Entwurf übernommen wurde, ist nicht bekannt. Die Fragen werden neu geladen; prüfen Sie sie, bevor Sie erneut einen Entwurf erstellen.',
    'It is not known whether the AI draft was applied. The questions are reloaded; check them before you create another draft.',
  ],
  [
    "a 200 whose body is not JSON (apiFetch's SyntaxError)",
    () => new SyntaxError('Unexpected token < in JSON at position 0'),
    'editorial.test.draft.errors.unknown',
    'Ob der KI-Entwurf übernommen wurde, ist nicht bekannt. Die Fragen werden neu geladen; prüfen Sie sie, bevor Sie erneut einen Entwurf erstellen.',
    'It is not known whether the AI draft was applied. The questions are reloaded; check them before you create another draft.',
  ],
  [
    'a 409 with a code the page does not know',
    () => new ApiError(409, problem(409, 'editorial.something-else')),
    'editorial.test.draft.errors.failed',
    'Der KI-Entwurf ist fehlgeschlagen. Versuchen Sie es erneut.',
    'The AI draft failed. Try again.',
  ],
];

describe('in German', () => {
  test.each(answers)('%s says its own text', (_answer, error, key, german) => {
    expect(draftFailure(error())).toBe(t(key));
    expect(draftFailure(error())).toBe(german);
  });
});

describe('in English', () => {
  test.each(answers)('%s says its own text', (_answer, error, key, _german, english) => {
    setLanguage('en-GB', { remember: false });

    expect(draftFailure(error())).toBe(t(key));
    expect(draftFailure(error())).toBe(english);
  });
});

test("the host's words are never what the page says", () => {
  for (const [, error] of answers) {
    expect(draftFailure(error())).not.toContain('Words of the host');
  }
});
