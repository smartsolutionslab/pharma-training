import { z } from 'zod';
import type { EditorialQuestionRequest } from '@/api/generated';
import type { MessageKey } from '@/i18n';

/** The bounds the host holds a question to: QuestionText, AnswerOptionText and DraftedQuestion's options. */
export const questionBounds = {
  text: { max: 1000 },
  option: { max: 400 },
  options: { min: 2, max: 10 },
} as const;

const hasNoNul = (value: string) => !value.includes('\u0000');

const questionTypes = ['SingleChoice', 'MultipleChoice'] as const;

/** Whether the type and which options are correct are there to check, whatever the texts hold. */
const correctnessFields = z.object({
  type: z.enum(questionTypes),
  options: z.array(z.object({ correct: z.boolean() })),
});

/**
 * The question form's schema, checked before the host checks the same rules again, and mirroring what approval checks
 * (Question.Defect), so a question saved through the form can be approved as it is: the chapter one of the version's,
 * the text trimmed 1 to 1000 characters without a NUL, 2 to 10 options each trimmed 1 to 400 characters without a NUL,
 * a single-choice question with exactly one correct option and a multiple-choice one with at least one. Each message is
 * a key, rendered with t where it is shown; the correct options are checked even while the chapter or text is refused.
 */
export function questionSchema(chapterIds: readonly string[]) {
  return z
    .object({
      chapterId: z.string().refine((id) => chapterIds.includes(id), {
        error: 'editorial.test.questionForm.chapterRequired' satisfies MessageKey,
      }),
      text: z
        .string()
        .trim()
        .min(1, { error: 'editorial.test.questionForm.textRequired' satisfies MessageKey })
        .max(questionBounds.text.max, { error: 'editorial.test.questionForm.textTooLong' satisfies MessageKey })
        .refine(hasNoNul, { error: 'editorial.test.questionForm.hasNul' satisfies MessageKey }),
      type: z.enum(questionTypes),
      options: z
        .array(
          z.object({
            text: z
              .string()
              .trim()
              .min(1, { error: 'editorial.test.questionForm.optionRequired' satisfies MessageKey })
              .max(questionBounds.option.max, {
                error: 'editorial.test.questionForm.optionTooLong' satisfies MessageKey,
              })
              .refine(hasNoNul, { error: 'editorial.test.questionForm.hasNul' satisfies MessageKey }),
            correct: z.boolean(),
          }),
        )
        .min(questionBounds.options.min, { error: 'editorial.test.questionForm.tooFewOptions' satisfies MessageKey })
        .max(questionBounds.options.max, {
          error: 'editorial.test.questionForm.tooManyOptions' satisfies MessageKey,
        }),
    })
    .superRefine(
      (values, context) => {
        const correct = values.options.filter((option) => option.correct).length;
        if (values.type === 'SingleChoice' && correct !== 1) {
          context.addIssue({
            code: 'custom',
            path: ['options'],
            message: 'editorial.test.questionForm.singleChoiceOneCorrect' satisfies MessageKey,
          });
        }
        if (values.type === 'MultipleChoice' && correct < 1) {
          context.addIssue({
            code: 'custom',
            path: ['options'],
            message: 'editorial.test.questionForm.multipleChoiceACorrect' satisfies MessageKey,
          });
        }
      },
      { when: ({ value }) => correctnessFields.safeParse(value).success },
    )
    .transform((values): EditorialQuestionRequest => values);
}

export type QuestionValues = z.input<ReturnType<typeof questionSchema>>;
