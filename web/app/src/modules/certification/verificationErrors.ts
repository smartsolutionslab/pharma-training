import type { MessageKey } from '@/i18n';

/**
 * What the verification page shows when the host does not verify a code: that the code cannot be verified, as content,
 * or an alert, in a text of its own or in the host's own words.
 */
export type VerificationOutcome =
  { kind: 'notVerifiable' } | { kind: 'alert'; key: MessageKey } | { kind: 'alert'; message: string };

/** What a failed verification shows. Stub for #54 (T3, red first). */
export function verificationOutcome(error: unknown): VerificationOutcome {
  void error;
  return { kind: 'alert', message: '' };
}
