import { z } from 'zod';
import type { MessageKey } from '@/i18n';

/** The bounds the host holds a blueprint to: QuestionCount with TestBlueprint's minimum, PassingScore, Validity. */
export const blueprintBounds = {
  questions: { min: 1, max: 50 },
  passingScorePercent: { min: 0, max: 100 },
  validityMonths: { min: 1, max: 60 },
} as const;

const required = 'editorial.test.blueprint.required' satisfies MessageKey;

/** A whole number within its bounds; anything else that is no whole number (an emptied field's '', NaN, 2.5) is asked for. */
const wholeNumber = ({ min, max }: { min: number; max: number }, invalid: MessageKey) =>
  z.number({ error: required }).int({ error: required }).min(min, { error: invalid }).max(max, { error: invalid });

// Each message is a key, rendered with t where it is shown, {min} and {max} formatted there: a message already shown
// follows a switch of the language.
/** The blueprint form's schema, checked before the host checks the same bounds again. */
export const testBlueprintSchema = z.object({
  questions: wholeNumber(blueprintBounds.questions, 'editorial.test.blueprint.questionsInvalid'),
  passingScorePercent: wholeNumber(blueprintBounds.passingScorePercent, 'editorial.test.blueprint.passingScoreInvalid'),
  validityMonths: wholeNumber(blueprintBounds.validityMonths, 'editorial.test.blueprint.validityInvalid'),
});

export type TestBlueprint = z.infer<typeof testBlueprintSchema>;
