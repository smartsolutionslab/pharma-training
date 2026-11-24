import { ApiError } from '@/api/baseApi';
import type { MessageKey } from '@/i18n';
import { type ErrorMessage, errorMessage } from '@/i18n/errorText';

/**
 * What the export page shows when the host refuses an export (#297, ADR 0027): the period's refusal at the field to,
 * any other refusal of the export in the alert under the form, which a pharmacy no longer found also follows by asking
 * for the memberships again.
 */
export type ExportRefusal =
  { kind: 'period'; message: ErrorMessage } | { kind: 'alert'; message: ErrorMessage; pharmacyGone: boolean };

const failed: MessageKey = 'certification.export.failed';

const period = 'certification.invalid-export-period';
const pharmacyNotFound = 'certification.pharmacy-not-found';

/** The refusals of the export worded by the catalog's errors.<code>, #52's texts. */
const alertCodes = new Set([
  'certification.export-too-large',
  'certification.capability-missing',
  'certification.identity-incomplete',
  pharmacyNotFound,
]);

/**
 * What a failed export shows, by the refusal's code: the export's own refusals by their catalog text, and anything
 * else (the network, a 5xx, the framework's 400 for a malformed day) as the export's failure, which submitting again
 * retries.
 */
export function exportRefusal(error: unknown): ExportRefusal {
  const code = error instanceof ApiError ? error.code : undefined;
  if (code === period) {
    return { kind: 'period', message: errorMessage(error, failed) };
  }
  if (code !== undefined && alertCodes.has(code)) {
    return { kind: 'alert', message: errorMessage(error, failed), pharmacyGone: code === pharmacyNotFound };
  }
  return { kind: 'alert', message: { key: failed, values: {} }, pharmacyGone: false };
}
