import { expect, test } from 'vitest';
import type { EditorialQuestionDto } from '@/api/generated';
import { approvedAi, handWritten, unreviewedAi } from './courseTestFixtures';
import { testCounts } from './testCounts';

// What the test page counts (#25, S3, S8): the questions the blueprint requires, those the test holds, how many of them
// are approved and unreviewed, how many to remove when it holds too many, and how many an AI draft would write (the
// blueprint less the approved ones, never below none).

const approved = (position: number): EditorialQuestionDto => ({ ...approvedAi, id: `approved-${position}`, position });
const unreviewed = (position: number): EditorialQuestionDto => ({
  ...unreviewedAi,
  id: `unreviewed-${position}`,
  position,
});
const blueprint = (questions: number | string) => ({ questions, passingScorePercent: 80, validityMonths: 12 });

test('counts an approved question and two unreviewed ones, one of them by hand, against a blueprint of ten', () => {
  expect(testCounts({ questions: [approvedAi, unreviewedAi, handWritten], blueprint: blueprint(10) })).toEqual({
    required: 10,
    held: 3,
    approved: 1,
    unreviewed: 2,
    tooMany: 0,
    toDraft: 9,
  });
});

test('an empty test holds nothing and an AI draft would write the whole blueprint', () => {
  expect(testCounts({ questions: [], blueprint: blueprint(10) })).toEqual({
    required: 10,
    held: 0,
    approved: 0,
    unreviewed: 0,
    tooMany: 0,
    toDraft: 10,
  });
});

test('a test with more questions than the blueprint says how many are too many', () => {
  expect(
    testCounts({ questions: [approved(1), unreviewed(2), unreviewed(3), unreviewed(4)], blueprint: blueprint(1) }),
  ).toEqual({ required: 1, held: 4, approved: 1, unreviewed: 3, tooMany: 3, toDraft: 0 });
});

test('approved questions beyond the blueprint leave nothing to draft, not less than nothing', () => {
  expect(testCounts({ questions: [approved(1), approved(2), approved(3)], blueprint: blueprint(2) })).toEqual({
    required: 2,
    held: 3,
    approved: 3,
    unreviewed: 0,
    tooMany: 1,
    toDraft: 0,
  });
});

// The host types its numbers as number | string.
test('reads a blueprint the host sends as a string as a number', () => {
  expect(testCounts({ questions: [approved(1)], blueprint: blueprint('5') })).toEqual({
    required: 5,
    held: 1,
    approved: 1,
    unreviewed: 0,
    tooMany: 0,
    toDraft: 4,
  });
});
