import { describe, expect, test } from 'vitest';
import { defaultPeriod, exportSchema } from './exportSchema';

// The export form's schema (#297, T2). Its rules mirror the host's ExportPeriod (ADR 0032), so the host's
// invalid-export-period answers only when the two disagree: both days are calendar dates, to is on or after from, and
// to is at most from plus five years as DateOnly.AddYears gives it (29 February plus five years is 28 February). There
// is no rule against a future day; the host has none. Each message is a key, rendered with t where it is shown. The
// default range is the last twelve months in the German day, whatever the browser's zone.

/** 24 November 2026, 11:00 in Germany. */
const clock = () => new Date('2026-11-24T10:00:00Z');

/** The messages the schema gives, by the field they are about. */
function refusals(from: string, to: string): Record<string, string[]> {
  const result = exportSchema(clock).safeParse({ from, to });
  if (result.success) {
    return {};
  }
  const messages: Record<string, string[]> = {};
  for (const issue of result.error.issues) {
    const field = String(issue.path[0]);
    messages[field] = [...(messages[field] ?? []), issue.message];
  }
  return messages;
}

describe('the default period', () => {
  test('is the last twelve months to today', () => {
    expect(defaultPeriod(clock)).toEqual({ from: '2025-11-25', to: '2026-11-24' });
  });

  test("follows the day in Germany, not UTC's: 23:30 UTC is already the next day there", () => {
    expect(defaultPeriod(() => new Date('2026-11-24T23:30:00Z'))).toEqual({ from: '2025-11-26', to: '2026-11-25' });
  });

  test('on 29 February starts on 1 March, as AddYears takes 29 February back to 28 February', () => {
    expect(defaultPeriod(() => new Date('2028-02-29T10:00:00Z'))).toEqual({ from: '2027-03-01', to: '2028-02-29' });
  });
});

describe('accepts', () => {
  test('one day, from and to the same', () => {
    expect(refusals('2026-11-24', '2026-11-24')).toEqual({});
  });

  test('exactly five years', () => {
    expect(refusals('2021-03-01', '2026-03-01')).toEqual({});
  });

  test('29 February to 28 February five years later', () => {
    expect(refusals('2024-02-29', '2029-02-28')).toEqual({});
  });
});

describe('refuses', () => {
  test('to before from, on to', () => {
    expect(refusals('2026-11-24', '2026-11-23')).toEqual({ to: ['certification.export.period.order'] });
  });

  test('five years and a day, on to', () => {
    expect(refusals('2021-03-01', '2026-03-02')).toEqual({ to: ['certification.export.period.tooLong'] });
  });

  test('29 February to 1 March five years later, on to', () => {
    expect(refusals('2024-02-29', '2029-03-01')).toEqual({ to: ['certification.export.period.tooLong'] });
  });

  test('an empty from, on from', () => {
    expect(refusals('', '2026-11-24')).toEqual({ from: ['certification.export.period.invalid'] });
  });

  test('an empty to, on to', () => {
    expect(refusals('2025-11-25', '')).toEqual({ to: ['certification.export.period.invalid'] });
  });

  test('a day the calendar lacks in from, on from', () => {
    expect(refusals('2027-02-30', '2027-03-15')).toEqual({ from: ['certification.export.period.invalid'] });
  });

  test('a day the calendar lacks in to, on to', () => {
    expect(refusals('2027-02-01', '2027-02-30')).toEqual({ to: ['certification.export.period.invalid'] });
  });
});
