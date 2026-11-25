import { z } from 'zod';

// Stub for #419 T1 (red): only so retentionSchema.test.ts compiles. The green step moves retentionYears here from
// src/i18n/errorValues.ts and writes the schema; until then every parse throws and the bounds name no number.

/** How many years a pharmacy may keep a certificate in its view (RetentionPeriod). Not set yet. */
export const retentionYears = { min: Number.NaN, max: Number.NaN } as const;

/** The retention period form's schema. Not written yet: every value it checks throws. */
export const retentionSchema = z.object({ years: z.number() }).superRefine(() => {
  throw new Error('retentionSchema is not written yet (#419 T1).');
});
