import { z } from 'zod';
import { type MessageKey } from '@/i18n';
import { isCalendarDate, today } from '@/lib/germanDay';

/** The range of issue days an export covers, both inclusive, as yyyy-mm-dd in Germany (#297, ADR 0032). */
export interface ExportPeriod {
  from: string;
  to: string;
}

/** The most years one export covers, as the host's ExportPeriod takes them. */
export const maximumYears = 5;

/** The day a calendar date (yyyy-mm-dd) names, at midnight UTC, so counting from it knows no zone. */
function utcDay(day: string): Date {
  const [year = '', month = '', date = ''] = day.split('-');
  return new Date(Date.UTC(Number(year), Number(month) - 1, Number(date)));
}

function dayOf(date: Date): string {
  const year = String(date.getUTCFullYear()).padStart(4, '0');
  const month = String(date.getUTCMonth() + 1).padStart(2, '0');
  const day = String(date.getUTCDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

/** The day so many years on, as DateOnly.AddYears gives it: 29 February in a year without one is 28 February. */
export function addYears(day: string, years: number): string {
  const date = utcDay(day);
  const month = date.getUTCMonth();
  date.setUTCFullYear(date.getUTCFullYear() + years);
  if (date.getUTCMonth() !== month) {
    date.setUTCDate(0);
  }
  return dayOf(date);
}

function addDays(day: string, days: number): string {
  const date = utcDay(day);
  date.setUTCDate(date.getUTCDate() + days);
  return dayOf(date);
}

/** The last twelve months in the German day, to today: the export form's starting range. */
export function defaultPeriod(clock: () => Date): ExportPeriod {
  const to = today(clock);
  return { from: addDays(addYears(to, -1), 1), to };
}

const calendarDate = z
  .string()
  .refine(isCalendarDate, { error: 'certification.export.period.invalid' satisfies MessageKey });

/**
 * The export form's schema: two calendar dates, to on or after from, at most five years apart. Its rules mirror the
 * host's, which has no rule against a future day, so the clock goes unread here. Each message is a key, rendered with
 * t where it is shown.
 */
export function exportSchema(clock: () => Date): z.ZodType<ExportPeriod, ExportPeriod> {
  void clock;
  return z.object({ from: calendarDate, to: calendarDate }).superRefine(({ from, to }, context) => {
    if (!isCalendarDate(from) || !isCalendarDate(to)) {
      return;
    }
    // yyyy-mm-dd compares as the days it names.
    if (to < from) {
      context.addIssue({
        code: 'custom',
        path: ['to'],
        message: 'certification.export.period.order' satisfies MessageKey,
      });
    } else if (to > addYears(from, maximumYears)) {
      context.addIssue({
        code: 'custom',
        path: ['to'],
        message: 'certification.export.period.tooLong' satisfies MessageKey,
      });
    }
  });
}
