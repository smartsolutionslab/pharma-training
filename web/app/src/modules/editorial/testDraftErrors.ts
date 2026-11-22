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
 * What the page says when an AI draft is refused or fails, in the language it speaks: never the host's words. A 5xx
 * carries no code (ApiError), so the missing model (503), the model's unusable answer (502) and the edge's timeout
 * (504) are told apart by their status; 429 is the per-editor limit, whatever its code; a module's refusal below 500 by
 * its code. Anything else, a network failure included, failed.
 */
export function draftFailure(error: unknown): string {
  return t(failureKey(error));
}

function failureKey(error: unknown): MessageKey {
  if (!(error instanceof ApiError)) {
    return 'editorial.test.draft.errors.failed';
  }
  switch (error.status) {
    case 503:
      return 'editorial.test.draft.errors.modelNotConfigured';
    case 502:
      return 'editorial.test.draft.errors.malformed';
    case 504:
      return 'editorial.test.draft.errors.timedOut';
    case 429:
      return 'editorial.test.draft.errors.tooManyRequests';
    default:
      return (error.code === undefined ? undefined : byCode[error.code]) ?? 'editorial.test.draft.errors.failed';
  }
}
