import { ApiError } from '@/api/baseApi';
import type { MessageKey } from '@/i18n';

/** What my courses says when it cannot list the courses: a text of its own, or the host's own words. */
export type MyCoursesFailure = { key: MessageKey } | { message: string };

/** Compliance's refusals the page has words for. */
const refusals: Partial<Record<string, MessageKey>> = {
  'compliance.pharmacy-not-found': 'learning.myCourses.pharmacyNotFound',
  'compliance.identity-incomplete': 'learning.myCourses.identityIncomplete',
};

/**
 * What a failed read of one's assignments says: a refusal the page has words for gives its key; another of
 * Compliance's own refusals the host's words (ADR 0007); anything else, the network included, that the courses could
 * not be loaded.
 */
export function myCoursesFailure(error: unknown): MyCoursesFailure {
  if (error instanceof ApiError && error.code !== undefined) {
    const key = refusals[error.code];
    if (key !== undefined) {
      return { key };
    }
    if (error.code.startsWith('compliance.') && error.serverMessage !== undefined) {
      return { message: error.serverMessage };
    }
  }
  return { key: 'learning.myCourses.loadFailed' };
}
