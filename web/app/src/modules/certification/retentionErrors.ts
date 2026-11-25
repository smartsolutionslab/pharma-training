import { ApiError } from '@/api/baseApi';
import type { MessageKey } from '@/i18n';
import { type ErrorMessage, errorMessage } from '@/i18n/errorText';

/**
 * What the retention period section shows when the host refuses a period (ADR 0027): the period's own refusal at the
 * field, any other in a toast, which a pharmacy no longer found also follows by asking for the memberships again.
 */
export type RetentionRefusal =
  { kind: 'field'; message: ErrorMessage } | { kind: 'toast'; message: ErrorMessage; pharmacyGone: boolean };

const failed: MessageKey = 'certification.retention.failed';

const invalidPeriod = 'certification.invalid-retention-period';
const pharmacyNotFound = 'certification.pharmacy-not-found';

/** The route's refusals (#418) worded by the catalog's errors.<code>. */
const toastCodes = new Set(['certification.capability-missing', 'certification.identity-incomplete', pharmacyNotFound]);

/**
 * What a refused save shows, by the refusal's code: the period's own and the route's by their catalog text, and
 * anything else (the network, a 5xx, a 400 without a code) as the save's failure, which pressing again retries.
 */
export function retentionRefusal(error: unknown): RetentionRefusal {
  const code = error instanceof ApiError ? error.code : undefined;
  if (code === invalidPeriod) {
    return { kind: 'field', message: errorMessage(error, failed) };
  }
  if (code !== undefined && toastCodes.has(code)) {
    return { kind: 'toast', message: errorMessage(error, failed), pharmacyGone: code === pharmacyNotFound };
  }
  return { kind: 'toast', message: { key: failed, values: {} }, pharmacyGone: false };
}
