import { ApiError } from '@/api/baseApi';
import type { MessageKey } from '@/i18n';

/** What the team matrix says when it cannot show: a text of its own, or the host's own words. */
export type MatrixFailure = { key: MessageKey } | { message: string };

/** The module's refusals the page has words for. */
const refusals: Partial<Record<string, MessageKey>> = {
  'compliance.capability-missing': 'compliance.matrix.notAllowed',
  'compliance.pharmacy-not-found': 'compliance.matrix.pharmacyNotFound',
  'compliance.identity-incomplete': 'compliance.matrix.identityIncomplete',
};

/**
 * What a failed read of the matrix says: a refusal the page has words for gives its key; another of the module's own
 * refusals the host's words (ADR 0007); anything else, the network included, that the matrix could not be loaded.
 */
export function matrixFailure(error: unknown): MatrixFailure {
  if (error instanceof ApiError && error.code !== undefined) {
    const key = refusals[error.code];
    if (key !== undefined) {
      return { key };
    }
    if (error.code.startsWith('compliance.') && error.serverMessage !== undefined) {
      return { message: error.serverMessage };
    }
  }
  return { key: 'compliance.matrix.loadFailed' };
}
