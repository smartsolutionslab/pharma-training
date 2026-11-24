import { ApiError } from '@/api/baseApi';
import type { MessageKey } from '@/i18n';
import { type ErrorMessage, errorMessage, messageText } from '@/i18n/errorText';

/** A step of a course version's workflow (#26). */
export type WorkflowAction = 'submit' | 'publish' | 'return' | 'retire';

/** The page's own text for a code, where its words depend on the step (ADR 0008, #147). */
const overrides: Record<string, MessageKey> = {
  'editorial.course-version-changed': 'editorial.workflow.errors.changed',
  'editorial.not-a-draft': 'editorial.workflow.errors.notADraft',
  'editorial.not-published': 'editorial.workflow.errors.notPublished',
  'editorial.too-few-options': 'editorial.workflow.errors.questionDefect',
  'editorial.single-choice-one-correct': 'editorial.workflow.errors.questionDefect',
  'editorial.multiple-choice-a-correct': 'editorial.workflow.errors.questionDefect',
  'editorial.invalid-return-reason': 'editorial.workflow.returnForm.reasonInvalid',
};

/** The module's refusals of a step the page knows, besides its overrides: the catalog says them (errors.<code>). */
const knownCodes = new Set([
  ...Object.keys(overrides),
  'editorial.identity-incomplete',
  'editorial.course-version-not-found',
  'editorial.no-chapters',
  'editorial.too-many-questions',
  'editorial.test-incomplete',
  'editorial.not-in-review',
  'editorial.four-eyes',
  'editorial.publisher-approved-questions',
  'editorial.question-not-approved',
]);

/** Each step's text when it failed for a reason the page has no words of its own for. */
const failed: Record<WorkflowAction, MessageKey> = {
  submit: 'editorial.workflow.errors.submitFailed',
  publish: 'editorial.workflow.errors.publishFailed',
  return: 'editorial.workflow.errors.returnFailed',
  retire: 'editorial.workflow.errors.retireFailed',
};

/** Whether the refusal names a module's code the page knows a step's refusal by. */
export function isKnownRefusal(error: unknown): boolean {
  return error instanceof ApiError && error.code !== undefined && knownCodes.has(error.code);
}

/**
 * What the course version page says when a step of the workflow is refused or fails, by the refusal's code
 * (src/i18n/errorText.ts): the page's own text where its words depend on the step, else the catalog's errors.<code>,
 * the host's words only for a module code the catalog does not know; a policy's 403 without a code that the editor may
 * not take the step; anything else, the unique index's already-exists and a network failure included, that the step
 * failed.
 */
export function workflowFailure(action: WorkflowAction, error: unknown): ErrorMessage {
  const forbidden = error instanceof ApiError && error.status === 403 && error.failureCode === undefined;
  // A step creates nothing: the unique index's already-exists says that the step failed, not that something was created.
  return errorMessage(error, forbidden ? 'editorial.workflow.errors.forbidden' : failed[action], {
    overrides: { ...overrides, 'already-exists': failed[action] },
  });
}

/** The failure's text in the language of the moment. */
export function workflowFailureText(failure: ErrorMessage): string {
  return messageText(failure);
}
