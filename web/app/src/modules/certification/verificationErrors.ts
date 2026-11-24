import { ApiError } from '@/api/baseApi';
import type { MessageKey } from '@/i18n';
import { type ErrorMessage, errorMessage } from '@/i18n/errorText';

/**
 * What the verification page shows when the host does not verify a code: that the code cannot be verified, as content,
 * or an alert with its message (src/i18n/errorText.ts, #147).
 */
export type VerificationOutcome = { kind: 'notVerifiable' } | { kind: 'alert'; message: ErrorMessage };

/** The page's own words for the shared limit (ADR 0026), where they depend on the page. */
const overrides: Record<string, MessageKey> = {
  'certification.verification-limit-reached': 'certification.verify.limitReached',
};

/** The alert that says the certificate could not be verified. */
export const verificationLoadFailed: VerificationOutcome = {
  kind: 'alert',
  message: { key: 'certification.verify.loadFailed', values: {} },
};

/**
 * What a failed verification shows, by the spec's "Refusal → UI" table and the refusal's code: the host's unverifiable
 * code as content, the shared limit in the page's own words, another of the module's own refusals by the catalog's
 * errors.<code> (the host's words only for a code the catalog does not know), and anything else, the network included,
 * that the certificate could not be verified.
 */
export function verificationOutcome(error: unknown): VerificationOutcome {
  if (error instanceof ApiError && error.code !== undefined && error.code.startsWith('certification.')) {
    if (error.code === 'certification.certificate-not-verifiable') {
      return { kind: 'notVerifiable' };
    }
    return { kind: 'alert', message: errorMessage(error, 'certification.verify.loadFailed', { overrides }) };
  }
  return verificationLoadFailed;
}
