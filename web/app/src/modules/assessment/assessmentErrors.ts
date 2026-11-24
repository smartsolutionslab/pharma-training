import { ApiError } from '@/api/baseApi';
import type { MessageKey } from '@/i18n';
import { type ErrorMessage, errorMessage, messageText } from '@/i18n/errorText';

/** What the learner's test and result pages (#48) ask of the host. */
export type AssessmentAction = 'read' | 'result' | 'start' | 'answer' | 'submit';

/** The page's text for a refusal a module names by its code (ADR 0007). */
const byCode: Record<string, MessageKey> = {
  'assessment.examination-not-found': 'assessment.test.notUnlocked',
  'assessment.identity-incomplete': 'assessment.test.noIdentity',
  'assessment.examination-not-unlocked': 'assessment.test.notUnlocked',
  'assessment.test-definition-missing': 'assessment.test.notReady',
  'assessment.no-open-attempt': 'assessment.test.alreadySubmitted',
  'assessment.question-not-in-attempt': 'assessment.test.save.reload',
  'assessment.option-not-in-question': 'assessment.test.save.reload',
  'assessment.single-choice-takes-one-option': 'assessment.test.save.singleChoice',
  'assessment.examination-changed': 'assessment.test.changed',
};

/** Each action's text when it failed for a reason the page has no words of its own for. */
const failed: Record<AssessmentAction, MessageKey> = {
  read: 'assessment.test.loadFailed',
  result: 'assessment.result.loadFailed',
  start: 'assessment.test.startFailed',
  answer: 'assessment.test.save.failed',
  submit: 'assessment.test.submitFailed',
};

/** What a page says about a refused or failed request: a catalog text, or a module's own words (#147, ADR 0007). */
export type AssessmentFailure = ErrorMessage;

/**
 * What the test and result pages say when a request is refused or fails, by the refusal's code
 * (src/i18n/errorText.ts): the page's own text for a code it knows, else the catalog's errors.<code>, the host's words
 * only for a module code the catalog does not know; anything else, a network failure included, that the action failed.
 */
export function assessmentFailure(action: AssessmentAction, error: unknown): AssessmentFailure {
  // A server error, whatever code it carries, and the guard's or the unique index's refusal say the action failed.
  const serverError = error instanceof ApiError && error.status >= 500;
  return errorMessage(serverError ? undefined : error, failed[action], {
    overrides: { ...byCode, 'invalid-argument': failed[action], 'already-exists': failed[action] },
  });
}

/** The failure's text in the language of the moment; an answer's failure names its question ({ number }). */
export function assessmentFailureText(failure: AssessmentFailure, values: Record<string, unknown> = {}): string {
  return messageText('key' in failure ? { key: failure.key, values: { ...failure.values, ...values } } : failure);
}
