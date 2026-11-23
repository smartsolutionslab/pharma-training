import { afterEach, describe, expect, test } from 'vitest';
import { createAssignSchema } from './assignSchema';

// The assignment wizard's schema (#37, T3): a course by its uuid, 1 to 100 people by theirs, a due date (yyyy-mm-dd)
// from today to ten years ahead, and whether it repeats yearly. Today comes from the clock it is given, as the day in
// Germany (Europe/Berlin), the host's day (DayInGermany()), whatever the browser's zone, so the tests do not depend on
// the day they run. Each message is a key, rendered with t where shown.

/** 23 November 2026 at noon in Germany: the same day in the browser's zone wherever it runs. */
const clock = () => new Date('2026-11-23T11:00:00Z');
const schema = createAssignSchema(clock);

const course = '0198c1a0-0000-7000-8000-0000000000c1';
const person = (index: number) => `0198c1a0-0000-7000-8000-${String(index).padStart(12, '0')}`;

const valid = { courseId: course, personIds: [person(1), person(2)], dueDate: '2027-03-15', repeatYearly: true };

/** The messages the schema gives, by the field they are about. */
function refusals(values: Record<string, unknown>): Record<string, string[]> {
  const result = schema.safeParse({ ...valid, ...values });
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

describe('accepts', () => {
  test('a course, two people, a due date and a yearly recurrence, as they are', () => {
    const result = schema.safeParse(valid);

    expect(result.success).toBe(true);
    expect(result.data).toEqual(valid);
  });

  test('no recurrence', () => {
    expect(schema.safeParse({ ...valid, repeatYearly: false }).data).toEqual({ ...valid, repeatYearly: false });
  });

  test("today, by the clock's day in Germany", () => {
    expect(refusals({ dueDate: '2026-11-23' })).toEqual({});
  });

  test('ten years from today to the day', () => {
    expect(refusals({ dueDate: '2036-11-23' })).toEqual({});
  });

  test('one person', () => {
    expect(refusals({ personIds: [person(1)] })).toEqual({});
  });

  test('100 people', () => {
    expect(refusals({ personIds: Array.from({ length: 100 }, (_, index) => person(index + 1)) })).toEqual({});
  });
});

describe('refuses', () => {
  test('no course as required', () => {
    expect(refusals({ courseId: '' })).toEqual({ courseId: ['compliance.assign.course.required'] });
  });

  test('a course that is not a uuid as required', () => {
    expect(refusals({ courseId: 'hygiene' })).toEqual({ courseId: ['compliance.assign.course.required'] });
  });

  test('nobody as required', () => {
    expect(refusals({ personIds: [] })).toEqual({ personIds: ['compliance.assign.learners.required'] });
  });

  test('101 people as too many', () => {
    expect(refusals({ personIds: Array.from({ length: 101 }, (_, index) => person(index + 1)) })).toEqual({
      personIds: ['compliance.assign.learners.tooMany'],
    });
  });

  test('a person who is not a uuid', () => {
    expect(Object.keys(refusals({ personIds: [person(1), 'ben'] }))).toEqual(['personIds']);
  });

  test('no due date as required', () => {
    expect(refusals({ dueDate: '' })).toEqual({ dueDate: ['compliance.assign.due.required'] });
  });

  test('a due date that is not a calendar date as required', () => {
    expect(refusals({ dueDate: '15.03.2027' })).toEqual({ dueDate: ['compliance.assign.due.required'] });
  });

  test('yesterday as in the past', () => {
    expect(refusals({ dueDate: '2026-11-22' })).toEqual({ dueDate: ['compliance.assign.due.inThePast'] });
  });

  test('a day more than ten years ahead as too far', () => {
    expect(refusals({ dueDate: '2036-11-24' })).toEqual({ dueDate: ['compliance.assign.due.tooFar'] });
  });
});

test('today follows the clock it is given', () => {
  const later = createAssignSchema(() => new Date('2027-01-10T07:00:00Z'));

  expect(later.safeParse({ ...valid, dueDate: '2027-01-09' }).success).toBe(false);
  expect(later.safeParse({ ...valid, dueDate: '2027-01-10' }).success).toBe(true);
});

// The host's today is Germany's day (#239): the browser's zone, UTC here, is another day for an hour each night.
describe("today is Germany's day, not the browser's", () => {
  const timeZone = process.env.TZ;

  afterEach(() => {
    // Node applies a change of TZ at once; the test process gets its own zone back.
    if (timeZone === undefined) {
      delete process.env.TZ;
    } else {
      process.env.TZ = timeZone;
    }
  });

  test('at 23:30 UTC on 23 November, already 24 November in Berlin, the 23rd is in the past and the 24th is not', () => {
    process.env.TZ = 'UTC';
    const lateAtNight = createAssignSchema(() => new Date('2026-11-23T23:30:00Z'));

    const refusalsOf = (dueDate: string) =>
      lateAtNight.safeParse({ ...valid, dueDate }).error?.issues.map((issue) => issue.message) ?? [];
    expect(refusalsOf('2026-11-23')).toEqual(['compliance.assign.due.inThePast']);
    expect(refusalsOf('2026-11-24')).toEqual([]);
  });

  // DateOnly.AddYears(10) on 29 February gives 28 February, not 1 March.
  test('ten years from 29 February is 28 February: 1 March is too far', () => {
    process.env.TZ = 'UTC';
    const leapDay = createAssignSchema(() => new Date('2028-02-29T11:00:00Z'));

    const refusalsOf = (dueDate: string) =>
      leapDay.safeParse({ ...valid, dueDate }).error?.issues.map((issue) => issue.message) ?? [];
    expect(refusalsOf('2038-02-28')).toEqual([]);
    expect(refusalsOf('2038-03-01')).toEqual(['compliance.assign.due.tooFar']);
  });
});
