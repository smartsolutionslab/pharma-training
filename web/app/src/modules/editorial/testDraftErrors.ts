import { ApiError } from '@/api/baseApi';
import { type MessageKey, t } from '@/i18n';

/** The page's text for a refusal of the draft a module names by its code (ADR 0008). */
const byCode: Record<string, MessageKey> = {
  'editorial.no-text-chapters': 'editorial.test.draft.noTextChapters',
  'editorial.nothing-to-draft': 'editorial.test.draft.nothingToDraft',
  'editorial.too-much-content-to-draft': 'editorial.test.draft.errors.tooMuchContent',
  'editorial.course-version-changed': 'editorial.test.draft.errors.changed',
  'editorial.not-a-draft': 'editorial.test.draft.errors.notADraft',
};

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
  return t(failureKey(error));
}

function failureKey(error: unknown): MessageKey {
  if (!(error instanceof ApiError)) {
    return error instanceof TypeError || error instanceof SyntaxError
      ? 'editorial.test.draft.errors.unknown'
      : 'editorial.test.draft.errors.failed';
  }
  switch (error.status) {
    case 503:
      return error.failureCode === 'model-not-configured'
        ? 'editorial.test.draft.errors.modelNotConfigured'
        : 'editorial.test.draft.errors.failed';
    case 502:
      return error.failureCode === 'editorial.ai-draft-malformed'
        ? 'editorial.test.draft.errors.malformed'
        : 'editorial.test.draft.errors.failed';
    case 504:
      return 'editorial.test.draft.errors.timedOut';
    case 429:
      return error.code === 'editorial.model-limit-reached'
        ? 'editorial.test.draft.errors.limitReached'
        : 'editorial.test.draft.errors.tooManyRequests';
    default:
      return (error.code === undefined ? undefined : byCode[error.code]) ?? 'editorial.test.draft.errors.failed';
  }
}
