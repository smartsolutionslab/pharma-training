const calendarDateFormat = /^(\d{4})-(\d{2})-(\d{2})$/;

// Test support: the MVP 1 e2e spec computes a certificate's valid-until with it. It lives under src/ because vitest runs
// only src/**/*.test.ts, and the rule it mirrors is the host's, so it is worth a unit test of its own.

/**
 * A calendar day (yyyy-mm-dd) a number of months later, as yyyy-mm-dd, the way .NET's DateOnly.AddMonths counts: the
 * same day of the month, or the target month's last day when it is shorter (29.02.2028 plus 12 months is 28.02.2029).
 */
export function addMonths(day: string, months: number): string {
  const [, year, month, date] = calendarDateFormat.exec(day) ?? [];
  if (year === undefined || month === undefined || date === undefined) {
    throw new Error(`${day} is not a calendar date (yyyy-mm-dd).`);
  }
  const firstOfTarget = new Date(Date.UTC(Number(year), Number(month) - 1 + months, 1));
  const daysInTarget = new Date(
    Date.UTC(firstOfTarget.getUTCFullYear(), firstOfTarget.getUTCMonth() + 1, 0),
  ).getUTCDate();
  const pad = (value: number) => String(value).padStart(2, '0');
  return (
    `${String(firstOfTarget.getUTCFullYear()).padStart(4, '0')}-${pad(firstOfTarget.getUTCMonth() + 1)}-` +
    pad(Math.min(Number(date), daysInTarget))
  );
}
