import type { EditorialQuestionDto, EditorialTestBlueprintDto } from '@/api/generated';

/** What the test page counts of a test against its blueprint. */
export interface TestCounts {
  /** The questions the blueprint requires. */
  required: number;
  /** The questions the test holds. */
  held: number;
  approved: number;
  unreviewed: number;
  /** The questions to remove to match the blueprint, none when it holds no more than required. */
  tooMany: number;
  /** The questions an AI draft would write: the blueprint less the approved ones, never below none. */
  toDraft: number;
}

/** Counts a test's questions against its blueprint, whose count the host may send as a string. */
export function testCounts({
  questions,
  blueprint,
}: {
  questions: readonly EditorialQuestionDto[];
  blueprint: EditorialTestBlueprintDto;
}): TestCounts {
  const required = Number(blueprint.questions);
  const held = questions.length;
  const approved = questions.filter((question) => question.review === 'Approved').length;
  return {
    required,
    held,
    approved,
    unreviewed: held - approved,
    tooMany: Math.max(held - required, 0),
    toDraft: Math.max(required - approved, 0),
  };
}
