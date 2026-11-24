import { ApiError } from '@/api/baseApi';
import { type MessageKey, t } from '@/i18n';

/** A step of a course version's workflow (#26). */
export type WorkflowAction = 'submit' | 'publish' | 'return' | 'retire';

/** The page's text for a refusal of a step a module names by its code (ADR 0008). */
const byCode: Record<string, MessageKey> = {
  'editorial.identity-incomplete': 'editorial.workflow.errors.identityIncomplete',
  'editorial.course-version-not-found': 'editorial.workflow.errors.notFound',
  'editorial.course-version-changed': 'editorial.workflow.errors.changed',
  'editorial.concurrent-save': 'editorial.workflow.errors.concurrentSave',
  'editorial.not-a-draft': 'editorial.workflow.errors.notADraft',
  'editorial.no-chapters': 'editorial.workflow.errors.noChapters',
  'editorial.too-many-questions': 'editorial.workflow.errors.tooManyQuestions',
  'editorial.test-incomplete': 'editorial.workflow.errors.testIncomplete',
  'editorial.not-in-review': 'editorial.workflow.errors.notInReview',
  'editorial.not-published': 'editorial.workflow.errors.notPublished',
  'editorial.four-eyes': 'editorial.workflow.errors.fourEyes',
  'editorial.publisher-approved-questions': 'editorial.workflow.errors.publisherApprovedQuestions',
  'editorial.question-not-approved': 'editorial.workflow.errors.questionNotApproved',
  'editorial.too-few-options': 'editorial.workflow.errors.questionDefect',
  'editorial.single-choice-one-correct': 'editorial.workflow.errors.questionDefect',
  'editorial.multiple-choice-a-correct': 'editorial.workflow.errors.questionDefect',
  'editorial.invalid-return-reason': 'editorial.workflow.returnForm.reasonInvalid',
};

/** Each step's text when it failed for a reason the page has no words of its own for. */
const failed: Record<WorkflowAction, MessageKey> = {
  submit: 'editorial.workflow.errors.submitFailed',
  publish: 'editorial.workflow.errors.publishFailed',
  return: 'editorial.workflow.errors.returnFailed',
  retire: 'editorial.workflow.errors.retireFailed',
};

/** What the page says about a refused or failed step: a catalog key, or a module's own words (ADR 0007). */
export type WorkflowFailure = { key: MessageKey } | { serverMessage: string };

/** Whether the refusal names a module's code the page has words of its own for. */
export function isKnownRefusal(error: unknown): boolean {
  return error instanceof ApiError && error.code !== undefined && byCode[error.code] !== undefined;
}

/**
 * What the course version page says when a step of the workflow is refused or fails. A module's code the page knows
 * says the catalog's text; a module's refusal with a code it does not know says the host's words
 * (ApiError.serverMessage, ADR 0007, until #147 translates them); a policy's 403 without a code that the editor may not
 * take the step; anything else, a network failure included, that the step failed.
 */
export function workflowFailure(action: WorkflowAction, error: unknown): WorkflowFailure {
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
  if (error.status === 403 && error.failureCode === undefined) {
    return { key: 'editorial.workflow.errors.forbidden' };
  }
  return { key: failed[action] };
}

/** The failure's text in the language of the moment; a module's words as the host sent them. */
export function workflowFailureText(failure: WorkflowFailure): string {
  return 'key' in failure ? t(failure.key) : failure.serverMessage;
}
