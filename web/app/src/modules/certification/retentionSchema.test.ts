import { describe, expect, test } from 'vitest';
import { retentionSchema, retentionYears } from './retentionSchema';

// The retention period form's schema (#419, T1). Its bounds are the host's RetentionPeriod (ADR 0034, amended by ADR
// 0039): a whole number of years from 2 to 30. Every refusal gives the one message, the catalog key of the host's own
// refusal, so the form and the host's 400 show the same sentence; an emptied field's '' and NaN included.

const invalid = 'errors.certification.invalid-retention-period';

/** The messages the schema gives for years; none when it accepts the value. */
function refusals(years: unknown): string[] {
  const result = retentionSchema.safeParse({ years });
  return result.success ? [] : result.error.issues.map((issue) => issue.message);
}

describe('accepts', () => {
  test.each([2, 5, 30])('%s years', (years) => {
    expect(refusals(years)).toEqual([]);
  });

  test('a whole number as the number it is', () => {
    expect(retentionSchema.parse({ years: 7 })).toEqual({ years: 7 });
  });
});

describe('refuses', () => {
  test.each([
    ['1, below the shortest period', 1],
    ['31, above the longest period', 31],
    ['2.5, no whole number', 2.5],
    ["an emptied field's ''", ''],
    ['NaN', Number.NaN],
  ])('%s, with the catalog key of the host’s refusal', (_name, years) => {
    expect(refusals(years)).toEqual([invalid]);
  });
});

test('the bounds are 2 to 30 years, as the host’s RetentionPeriod has them', () => {
  expect(retentionYears).toEqual({ min: 2, max: 30 });
});
