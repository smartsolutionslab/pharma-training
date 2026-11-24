import { z } from 'zod';
import { type MessageKey } from '@/i18n';
import { isCalendarDate, today } from '@/lib/germanDay';

/** At most this many people in one assignment, as the host takes them. */
export const maximumLearners = 100;

/** How many years ahead a due date may lie, as the host takes it. */
export const maximumYearsAhead = 10;

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
