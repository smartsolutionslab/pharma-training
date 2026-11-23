import { expect, test } from 'vitest';
import { testBlueprintSchema } from './testBlueprintSchema';

// The blueprint form's schema (#25, S2): whole numbers, 1 to 50 questions (QuestionCount, TestBlueprint's minimum of
// one), a passing score of 0 to 100 percent and a validity of 1 to 60 months, checked before the host checks them
// again. Each message is a key, rendered with t where it is shown.

/** The messages the schema gives, by field. */
function refusals(values: Record<string, unknown>) {
  const result = testBlueprintSchema.safeParse(values);
  return result.success
    ? {}
    : Object.fromEntries(result.error.issues.map((issue) => [issue.path.join('.'), issue.message]));
}

test.each([
  [{ questions: 10, passingScorePercent: 80, validityMonths: 12 }],
  [{ questions: 1, passingScorePercent: 0, validityMonths: 1 }],
  [{ questions: 50, passingScorePercent: 100, validityMonths: 60 }],
])('accepts %o and hands it over as it is', (values) => {
  const result = testBlueprintSchema.safeParse(values);

  expect(result.success).toBe(true);
  expect(result.data).toEqual(values);
});

test.each([0, 51, -1])('refuses %i questions', (questions) => {
  expect(refusals({ questions, passingScorePercent: 80, validityMonths: 12 })).toEqual({
    questions: 'editorial.test.blueprint.questionsInvalid',
  });
});

test.each([-1, 101])('refuses a passing score of %i percent', (passingScorePercent) => {
  expect(refusals({ questions: 10, passingScorePercent, validityMonths: 12 })).toEqual({
    passingScorePercent: 'editorial.test.blueprint.passingScoreInvalid',
  });
});

test.each([0, 61])('refuses a validity of %i months', (validityMonths) => {
  expect(refusals({ questions: 10, passingScorePercent: 80, validityMonths })).toEqual({
    validityMonths: 'editorial.test.blueprint.validityInvalid',
  });
});

// An emptied number field gives '' (Vue's v-model on type="number"); a fraction is no count.
test.each([[''], [undefined], [2.5], [Number.NaN]])('asks for a whole number instead of %o', (value) => {
  expect(refusals({ questions: value, passingScorePercent: value, validityMonths: value })).toEqual({
    questions: 'editorial.test.blueprint.required',
    passingScorePercent: 'editorial.test.blueprint.required',
    validityMonths: 'editorial.test.blueprint.required',
  });
});

test('reports every field out of range at once', () => {
  expect(refusals({ questions: 0, passingScorePercent: 101, validityMonths: 61 })).toEqual({
    questions: 'editorial.test.blueprint.questionsInvalid',
    passingScorePercent: 'editorial.test.blueprint.passingScoreInvalid',
    validityMonths: 'editorial.test.blueprint.validityInvalid',
  });
});
