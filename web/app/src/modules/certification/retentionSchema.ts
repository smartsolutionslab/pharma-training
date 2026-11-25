import { z } from 'zod';
import type { MessageKey } from '@/i18n';

/** How many years a pharmacy may keep a certificate in its export (the host's RetentionPeriod, ADR 0039). */
export const retentionYears = { min: 2, max: 30 } as const;

/** The host's own refusal of a period: the field says what the host's 400 would, its bounds from retentionYears. */
const invalid = 'errors.certification.invalid-retention-period' satisfies MessageKey;

/**
 * The retention period form's schema: a whole number of years within retentionYears. Anything else (an emptied
 * field's '', NaN, 2.5, 1, 31) gives the one key, rendered with messageText where it is shown.
 */
export const retentionSchema = z.object({
  years: z
    .number({ error: invalid })
    .int({ error: invalid })
    .min(retentionYears.min, { error: invalid })
    .max(retentionYears.max, { error: invalid }),
});

export type RetentionPeriod = z.infer<typeof retentionSchema>;
