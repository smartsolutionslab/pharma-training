import type { ErrorMessage } from '@/i18n/errorText';

// Stub for #419 T2 (red): only so retentionErrors.test.ts compiles. The green step tells the refusals apart by code.

/**
 * What the retention period section shows when the host refuses a period (ADR 0027): the period's own refusal at the
 * field, any other in a toast, which a pharmacy no longer found also follows by asking for the memberships again.
 */
export type RetentionRefusal =
  { kind: 'field'; message: ErrorMessage } | { kind: 'toast'; message: ErrorMessage; pharmacyGone: boolean };

/** What a refused save shows, by the refusal's code. Not written yet: it throws. */
export function retentionRefusal(error: unknown): RetentionRefusal {
  throw new Error('retentionRefusal is not written yet (#419 T2).', { cause: error });
}
