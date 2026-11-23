import { z } from 'zod';
import { i18n, type MessageKey } from '@/i18n';

/** At most this many people in one assignment, as the host takes them. */
export const maximumLearners = 100;

/** How many years ahead a due date may lie, as the host takes it. */
const maximumYearsAhead = 10;

const calendarDateFormat = /^(\d{4})-(\d{2})-(\d{2})$/;

/** The local day of a date as yyyy-mm-dd. */
function localDay(date: Date): string {
  const year = String(date.getFullYear()).padStart(4, '0');
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

/** German dates read day, month, year: 23.11.2026. */
const germanDate = /^(\d{2})\.(\d{2})\.(\d{4})$/;

/** The day the clock shows in Germany, the host's day (DayInGermany()), whatever the browser's zone: yyyy-mm-dd. */
export function today(clock: () => Date): string {
  const shown = i18n.global.d(clock(), 'germanDay', 'de-DE');
  const [, day, month, year] = germanDate.exec(shown) ?? [];
  if (day === undefined || month === undefined || year === undefined) {
    throw new Error(`${shown} is not a German date (dd.mm.yyyy).`);
  }
  return `${year}-${month}-${day}`;
}

function isLeapYear(year: number): boolean {
  return (year % 4 === 0 && year % 100 !== 0) || year % 400 === 0;
}

/** The latest due date the host takes: today in Germany plus ten years, as DateOnly.AddYears gives it (29 February becomes 28 February). */
export function latest(clock: () => Date): string {
  const [year = '', month = '', day = ''] = today(clock).split('-');
  const later = Number(year) + maximumYearsAhead;
  const lastDay = month === '02' && day === '29' && !isLeapYear(later) ? '28' : day;
  return `${String(later).padStart(4, '0')}-${month}-${lastDay}`;
}

/** Whether the value is a day of the calendar written yyyy-mm-dd: 2027-02-30 is not. */
function isCalendarDate(value: string): boolean {
  const [, year, month, day] = calendarDateFormat.exec(value) ?? [];
  if (year === undefined || month === undefined || day === undefined) {
    return false;
  }
  const date = new Date(Number(year), Number(month) - 1, Number(day));
  return localDay(date) === value;
}

/**
 * The assignment wizard's schema (#37): a course and 1 to 100 people by their uuids, a due date from today to ten years
 * ahead and whether it repeats yearly, checked as the host checks them before it checks them again. Today is the
 * clock's day in Germany, read at each check. Each message is a key, rendered with t where it is shown.
 */
export function createAssignSchema(clock: () => Date) {
  return z.object({
    courseId: z.uuid({ error: 'compliance.assign.course.required' satisfies MessageKey }),
    personIds: z
      .array(z.uuid())
      .min(1, { error: 'compliance.assign.learners.required' satisfies MessageKey })
      .max(maximumLearners, { error: 'compliance.assign.learners.tooMany' satisfies MessageKey }),
    // yyyy-mm-dd compares as the days it names.
    dueDate: z
      .string()
      .refine(isCalendarDate, { error: 'compliance.assign.due.required' satisfies MessageKey, abort: true })
      .refine((due) => due >= today(clock), { error: 'compliance.assign.due.inThePast' satisfies MessageKey })
      .refine((due) => due <= latest(clock), { error: 'compliance.assign.due.tooFar' satisfies MessageKey }),
    repeatYearly: z.boolean(),
  });
}

export type AssignValues = z.output<ReturnType<typeof createAssignSchema>>;
