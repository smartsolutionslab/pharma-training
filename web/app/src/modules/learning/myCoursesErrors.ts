import { ApiError } from '@/api/baseApi';
import type { MessageKey } from '@/i18n';
import { type ErrorMessage, errorMessage } from '@/i18n/errorText';

/** The page's own words for a refusal, where they depend on my courses. */
const overrides: Record<string, MessageKey> = {
  'compliance.pharmacy-not-found': 'learning.myCourses.pharmacyNotFound',
  'compliance.identity-incomplete': 'learning.myCourses.identityIncomplete',
};

/**
 * What a failed read of one's assignments says, by the refusal's code (src/i18n/errorText.ts): the page's own words for
 * pharmacy-not-found and identity-incomplete, else the catalog's errors.<code>, the host's words only for a module code
 * the catalog does not know; anything else, the network included, that the courses could not be loaded.
 */
export function myCoursesFailure(error: unknown): ErrorMessage {
  return errorMessage(error, 'learning.myCourses.loadFailed', { overrides });
}

/** The refusals a second read cannot turn round: the caller is no member any more, or their account names no person. */
const final = new Set(['compliance.pharmacy-not-found', 'compliance.identity-incomplete']);

/**
 * Whether reading one's assignments again can help: for any failure but the two final refusals, a framework's
 * invalid-argument, another module's code, a code the catalog does not know, the host's 5xx and the network included.
 */
export function myCoursesRetryable(error: unknown): boolean {
  return !(error instanceof ApiError && error.code !== undefined && final.has(error.code));
}
