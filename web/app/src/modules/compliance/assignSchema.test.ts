import { describe, expect, test } from 'vitest';
import { createAssignSchema } from './assignSchema';

// The assignment wizard's schema (#37, T3): a course by its uuid, 1 to 100 people by theirs, a due date (yyyy-mm-dd)
// from today to ten years ahead, and whether it repeats yearly. Today comes from the clock it is given, in local time,
// so the tests do not depend on the day they run. Each message is a key, rendered with t where shown.

/** 23 November 2026, 23:30 local time: late, so a clock read in UTC would be another day east of Greenwich. */
const clock = () => new Date(2026, 10, 23, 23, 30, 0);
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

  test("today, by the clock's local day", () => {
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
  const later = createAssignSchema(() => new Date(2027, 0, 10, 8, 0, 0));

  expect(later.safeParse({ ...valid, dueDate: '2027-01-09' }).success).toBe(false);
  expect(later.safeParse({ ...valid, dueDate: '2027-01-10' }).success).toBe(true);
});
