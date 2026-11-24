import { ApiError } from '@/api/baseApi';
import type { MessageKey } from '@/i18n';

/**
 * What the verification page shows when the host does not verify a code: that the code cannot be verified, as content,
 * or an alert, in a text of its own or in the host's own words.
 */
export type VerificationOutcome =
  { kind: 'notVerifiable' } | { kind: 'alert'; key: MessageKey } | { kind: 'alert'; message: string };

/**
 * What a failed verification shows, by the spec's "Refusal → UI" table: the host's unverifiable code as content, the
 * shared limit in the page's own words, another of the module's own refusals in the host's words (ADR 0007), and
 * anything else, the network included, that the certificate could not be verified.
 */
export function verificationOutcome(error: unknown): VerificationOutcome {
  if (error instanceof ApiError && error.code !== undefined) {
    if (error.code === 'certification.certificate-not-verifiable') {
      return { kind: 'notVerifiable' };
    }
    if (error.code === 'certification.verification-limit-reached') {
      return { kind: 'alert', key: 'certification.verify.limitReached' };
    }
    if (error.code.startsWith('certification.') && error.serverMessage !== undefined) {
      return { kind: 'alert', message: error.serverMessage };
    }
  }
  return { kind: 'alert', key: 'certification.verify.loadFailed' };
}
