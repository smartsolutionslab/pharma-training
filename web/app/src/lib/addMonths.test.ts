import { expect, test } from 'vitest';
import { addMonths } from './addMonths';

// The day a certificate is valid until, as the host works it out: DateOnly.AddMonths (Certification's and Compliance's
// CalendarDate.Plus), which keeps the day of the month and clamps it to the last day of a shorter month.
test.each([
  ['2028-02-29', 12, '2029-02-28'],
  ['2027-01-31', 1, '2027-02-28'],
  ['2028-01-31', 1, '2028-02-29'],
  ['2026-08-31', 1, '2026-09-30'],
  ['2026-11-24', 12, '2027-11-24'],
  ['2026-11-24', 0, '2026-11-24'],
  ['2026-12-15', 1, '2027-01-15'],
])('%s plus %i months is %s', (day, months, later) => {
  expect(addMonths(day, months)).toBe(later);
});

test('a text that is not a calendar date is refused', () => {
  expect(() => addMonths('24.11.2026', 12)).toThrow('24.11.2026 is not a calendar date (yyyy-mm-dd).');
});
