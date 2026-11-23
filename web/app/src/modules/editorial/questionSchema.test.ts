import { describe, expect, test } from 'vitest';
import type { EditorialQuestionRequest } from '@/api/generated';
import { handout, introduction } from './courseTestFixtures';
import { questionSchema } from './questionSchema';

// The question form's schema (#25, S5): the chapter one of the version's, the text trimmed 1 to 1000 characters
// without a NUL, 2 to 10 options each trimmed 1 to 400 characters without a NUL, a single-choice question with exactly
// one correct option and a multiple-choice one with at least one. These mirror what approval checks (Question.Defect),
// so a question saved through the form can be approved as it is. Each message is a key, rendered with t where shown.

const schema = questionSchema([introduction.id, handout.id]);

const valid: EditorialQuestionRequest = {
  chapterId: introduction.id,
  text: 'Wie lange waschen Sie die Hände?',
  type: 'SingleChoice',
  options: [
    { text: '30 Sekunden', correct: true },
    { text: '5 Sekunden', correct: false },
  ],
};

const options = (count: number, correct: number[] = [0]) =>
  Array.from({ length: count }, (_, index) => ({ text: `Option ${index + 1}`, correct: correct.includes(index) }));

/** The messages the schema gives, by the path of the field they belong to. */
function refusals(values: unknown): Record<string, string[]> {
  const result = schema.safeParse(values);
  if (result.success) {
    return {};
  }
  const byPath: Record<string, string[]> = {};
  for (const issue of result.error.issues) {
    const path = issue.path.join('.');
    byPath[path] = [...(byPath[path] ?? []), issue.message];
  }
  return byPath;
}
/** Every message the schema gives, wherever it puts it. */
const messages = (values: unknown) => Object.values(refusals(values)).flat();

describe('accepts', () => {
  test('a single-choice question with two options, one correct, and hands it over as the request', () => {
    const result = schema.safeParse(valid);

    expect(result.success).toBe(true);
    expect(result.data).toEqual(valid);
  });

  test('a multiple-choice question with ten options, three correct, about the other chapter', () => {
    const values = { ...valid, chapterId: handout.id, type: 'MultipleChoice', options: options(10, [0, 4, 9]) };

    expect(schema.safeParse(values).success).toBe(true);
  });

  test('a multiple-choice question with every option correct', () => {
    expect(schema.safeParse({ ...valid, type: 'MultipleChoice', options: options(2, [0, 1]) }).success).toBe(true);
  });

  test('a text of exactly 1000 characters and an option of exactly 400', () => {
    const values = {
      ...valid,
      text: 'a'.repeat(1000),
      options: [
        { text: 'b'.repeat(400), correct: true },
        { text: 'c', correct: false },
      ],
    };

    expect(schema.safeParse(values).success).toBe(true);
  });

  test('the text and the options trimmed', () => {
    const result = schema.safeParse({
      ...valid,
      text: '  Wie lange waschen Sie die Hände?\n',
      options: [
        { text: ' 30 Sekunden ', correct: true },
        { text: '5 Sekunden\t', correct: false },
      ],
    });

    expect(result.data).toEqual(valid);
  });
});

describe('the chapter', () => {
  test.each([[''], ['0198c1a0-0000-7000-8000-0000000000ef'], ['kein-kapitel']])(
    'refuses %j, which is none of the version’s chapters',
    (chapterId) => {
      expect(refusals({ ...valid, chapterId })).toEqual({ chapterId: ['editorial.test.questionForm.chapterRequired'] });
    },
  );

  test('a version without chapters takes none', () => {
    expect(questionSchema([]).safeParse(valid).success).toBe(false);
  });
});

describe('the text', () => {
  test.each([[''], ['   '], ['\n\t']])('refuses %j as missing', (text) => {
    expect(refusals({ ...valid, text })).toEqual({ text: ['editorial.test.questionForm.textRequired'] });
  });

  test('refuses 1001 characters', () => {
    expect(refusals({ ...valid, text: 'a'.repeat(1001) })).toEqual({
      text: ['editorial.test.questionForm.textTooLong'],
    });
  });

  test('refuses a NUL', () => {
    expect(refusals({ ...valid, text: 'Wie\u0000lange?' })).toEqual({ text: ['editorial.test.questionForm.hasNul'] });
  });
});

describe('the options', () => {
  test.each([0, 1])('refuses %i options as too few', (count) => {
    expect(messages({ ...valid, options: options(count) })).toContain('editorial.test.questionForm.tooFewOptions');
  });

  test('refuses 11 options as too many', () => {
    expect(messages({ ...valid, options: options(11) })).toContain('editorial.test.questionForm.tooManyOptions');
  });

  test.each([[''], ['  ']])('refuses an option of %j as missing, at its place', (text) => {
    expect(
      refusals({
        ...valid,
        options: [
          { text: '30 Sekunden', correct: true },
          { text, correct: false },
        ],
      }),
    ).toEqual({ 'options.1.text': ['editorial.test.questionForm.optionRequired'] });
  });

  test('refuses an option of 401 characters', () => {
    expect(
      refusals({
        ...valid,
        options: [
          { text: 'b'.repeat(401), correct: true },
          { text: '5 Sekunden', correct: false },
        ],
      }),
    ).toEqual({ 'options.0.text': ['editorial.test.questionForm.optionTooLong'] });
  });

  test('refuses an option with a NUL', () => {
    expect(
      refusals({
        ...valid,
        options: [
          { text: '30\u0000Sekunden', correct: true },
          { text: '5 Sekunden', correct: false },
        ],
      }),
    ).toEqual({ 'options.0.text': ['editorial.test.questionForm.hasNul'] });
  });
});

describe('the correct options', () => {
  test('a single-choice question with two correct options is refused', () => {
    expect(messages({ ...valid, options: options(3, [0, 2]) })).toEqual([
      'editorial.test.questionForm.singleChoiceOneCorrect',
    ]);
  });

  test('a single-choice question without a correct option is refused', () => {
    expect(messages({ ...valid, options: options(2, []) })).toEqual([
      'editorial.test.questionForm.singleChoiceOneCorrect',
    ]);
  });

  test('a multiple-choice question without a correct option is refused', () => {
    expect(messages({ ...valid, type: 'MultipleChoice', options: options(3, []) })).toEqual([
      'editorial.test.questionForm.multipleChoiceACorrect',
    ]);
  });

  test('every field is reported at once', () => {
    const shown = messages({ chapterId: '', text: '', type: 'SingleChoice', options: options(2, []) });

    expect(shown).toEqual(
      expect.arrayContaining([
        'editorial.test.questionForm.chapterRequired',
        'editorial.test.questionForm.textRequired',
        'editorial.test.questionForm.singleChoiceOneCorrect',
      ]),
    );
  });
});
