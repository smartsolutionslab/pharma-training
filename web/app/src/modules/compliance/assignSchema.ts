import { z } from 'zod';
import type { MessageKey } from '@/i18n';

/** At most this many people in one assignment, as the host takes them. */
export const maximumLearners = 100;

/** How many years ahead a due date may lie, as the host takes it. */
const maximumYearsAhead = 10;

const calendarDateFormat = /^(\d{4})-(\d{2})-(\d{2})$/;

/** The local day of a date as yyyy-mm-dd. */
export function localDay(date: Date): string {
  const year = String(date.getFullYear()).padStart(4, '0');
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
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
 * clock's local day, read at each check. Each message is a key, rendered with t where it is shown.
 */
export function createAssignSchema(clock: () => Date) {
  const today = () => localDay(clock());
  const latest = () => {
    const now = clock();
    return localDay(new Date(now.getFullYear() + maximumYearsAhead, now.getMonth(), now.getDate()));
  };

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
      .refine((due) => due >= today(), { error: 'compliance.assign.due.inThePast' satisfies MessageKey })
      .refine((due) => due <= latest(), { error: 'compliance.assign.due.tooFar' satisfies MessageKey }),
    repeatYearly: z.boolean(),
  });
}

export type AssignValues = z.output<ReturnType<typeof createAssignSchema>>;
