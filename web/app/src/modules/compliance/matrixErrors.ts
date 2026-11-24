import type { MessageKey } from '@/i18n';
import { type ErrorMessage, errorMessage } from '@/i18n/errorText';

/** The page's own words for a refusal, where they depend on the matrix (#147). */
const overrides: Record<string, MessageKey> = {
  'compliance.capability-missing': 'compliance.matrix.notAllowed',
};

/**
 * What a failed read of the matrix says, by the refusal's code (src/i18n/errorText.ts): the page's own words for
 * capability-missing, else the catalog's errors.<code>, the host's words only for a module code the catalog does not
 * know; anything else, the network included, that the matrix could not be loaded.
 */
export function matrixFailure(error: unknown): ErrorMessage {
  return errorMessage(error, 'compliance.matrix.loadFailed', { overrides });
}
