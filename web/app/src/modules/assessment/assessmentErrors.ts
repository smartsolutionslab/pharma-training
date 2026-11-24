import { ApiError } from '@/api/baseApi';
import { type MessageKey, t } from '@/i18n';

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

/** What a page says about a refused or failed request: a catalog key, or a module's own words (ADR 0007). */
export type AssessmentFailure = { key: MessageKey } | { serverMessage: string };

/**
 * What the test and result pages say when a request is refused or fails. A module's code the page knows says the
 * catalog's text; a module's refusal with a code it does not know says the host's words (ApiError.serverMessage);
 * anything else, a network failure included, that the action failed.
 */
export function assessmentFailure(action: AssessmentAction, error: unknown): AssessmentFailure {
  if (!(error instanceof ApiError)) {
    return { key: failed[action] };
  }
  const known = error.code === undefined ? undefined : byCode[error.code];
  if (known !== undefined) {
    return { key: known };
  }
  if (error.serverMessage !== undefined) {
    return { serverMessage: error.serverMessage };
  }
  return { key: failed[action] };
}

/** The failure's text in the language of the moment; an answer's failure names its question ({ number }). */
export function assessmentFailureText(failure: AssessmentFailure, values: Record<string, unknown> = {}): string {
  return 'key' in failure ? t(failure.key, values) : failure.serverMessage;
}
