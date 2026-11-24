import type { z } from 'zod';

/** The range of issue days an export covers, both inclusive, as yyyy-mm-dd in Germany (#297, ADR 0032). */
export interface ExportPeriod {
  from: string;
  to: string;
}

/** The last twelve months in the German day, to today: the export form's starting range. */
export function defaultPeriod(clock: () => Date): ExportPeriod {
  throw new Error(`Not implemented (#297 T2): defaultPeriod at ${clock().toISOString()}`);
}

/** The export form's schema: two calendar dates, to on or after from, at most five years apart. */
export function exportSchema(clock: () => Date): z.ZodType<ExportPeriod, ExportPeriod> {
  throw new Error(`Not implemented (#297 T2): exportSchema at ${clock().toISOString()}`);
}
