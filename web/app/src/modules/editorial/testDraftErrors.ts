import { ApiError } from '@/api/baseApi';
import type { MessageKey } from '@/i18n';
import { codeMessage, type ErrorMessage, messageText } from '@/i18n/errorText';

/** The page's own text for a refusal of the draft, where its words depend on the draft (ADR 0008, #147). */
const overrides: Record<string, MessageKey> = {
  'editorial.no-text-chapters': 'editorial.test.draft.noTextChapters',
  'editorial.nothing-to-draft': 'editorial.test.draft.nothingToDraft',
  'editorial.course-version-changed': 'editorial.test.draft.errors.changed',
  'editorial.not-a-draft': 'editorial.test.draft.errors.notADraft',
};

/** The message for a code: the page's own text, else the catalog's errors.<code>, else the draft failed. */
const byCode = (code: string | undefined): ErrorMessage =>
  codeMessage(code, 'editorial.test.draft.errors.failed', { overrides });

/**
 * What the page says when an AI draft is refused or fails, in the language it speaks: never the host's words. The
 * missing model (503 model-not-configured) and the model's unusable answer (502 editorial.ai-draft-malformed) are told
 * apart from an edge's 503 or 502 by the problem's code at any status (ApiError.failureCode); an edge's failed. The
 * edge's timeout (504) may have been saved after all; 429 is the per-editor limit by its code,
 * editorial.model-limit-reached (ADR 0020), and any other 429 says too many requests; a module's refusal below 500 by
 * its code. A draft whose answer the page never read (a network failure, a body that is not
 * JSON) may have been applied: its outcome is unknown. Anything else failed.
 */
export function draftFailure(error: unknown): string {
  return messageText(failureMessage(error));
}

/** The draft's message: never the host's words, so no serverMessage reaches codeMessage. */
function failureMessage(error: unknown): ErrorMessage {
  if (!(error instanceof ApiError)) {
    return error instanceof TypeError || error instanceof SyntaxError
      ? { key: 'editorial.test.draft.errors.unknown', values: {} }
      : byCode(undefined);
  }
  switch (error.status) {
    case 503:
      return byCode(error.failureCode === 'model-not-configured' ? error.failureCode : undefined);
    case 502:
      return byCode(error.failureCode === 'editorial.ai-draft-malformed' ? error.failureCode : undefined);
    case 504:
      return { key: 'editorial.test.draft.errors.timedOut', values: {} };
    case 429:
      return error.code === 'editorial.model-limit-reached'
        ? byCode(error.code)
        : { key: 'editorial.test.draft.errors.tooManyRequests', values: {} };
    default:
      return byCode(error.failureCode);
  }
}
