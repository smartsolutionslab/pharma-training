import { describe, expect, test } from 'vitest';
import { retentionSchema, retentionYears } from './retentionSchema';

// The retention period form's schema (#419, T1): a whole number of years from 2 to 30, as the host's RetentionPeriod
// holds it (ADR 0039). Every refusal is the catalog key of the host's own 400, so the field and the host say the same.

const invalid = 'errors.certification.invalid-retention-period';

describe('retentionSchema', () => {
  test('bounds the period from 2 to 30 years', () => {
    expect(retentionYears).toEqual({ min: 2, max: 30 });
  });

  test.each([2, 5, 30])('accepts %s years', (years) => {
    expect(retentionSchema.safeParse({ years })).toEqual({ success: true, data: { years } });
  });

  test.each([
    ['1', 1],
    ['31', 31],
    ['2.5', 2.5],
    ['an emptied field', ''],
    ['NaN', Number.NaN],
  ])('refuses %s with the catalog key of the host’s refusal', (_name, years) => {
    const result = retentionSchema.safeParse({ years });

    expect(result.success).toBe(false);
    expect(result.error?.issues.map((issue) => issue.message)).toEqual([invalid]);
  });
});
