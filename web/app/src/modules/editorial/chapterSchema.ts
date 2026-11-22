import { z } from 'zod';
import type { EditorialChapterDto, EditorialChapterRequest } from '@/api/generated';
import type { MessageKey } from '@/i18n';

/** The most a text chapter holds (MarkdownText). */
export const maxTextLength = 200_000;

const hasNoNul = (value: string) => !value.includes('\u0000');

// Each message is a key, rendered with t where it is shown: a message already shown follows a switch of the language.
// The content's rules apply to a text chapter alone; they are checked beside the title's, so both are reported at once.
const chapterFields = z.object({
  title: z
    .string()
    .trim()
    .min(1, { error: 'editorial.chapterForm.titleRequired' satisfies MessageKey })
    .max(150, { error: 'editorial.chapterForm.titleTooLong' satisfies MessageKey })
    .refine(hasNoNul, { error: 'editorial.chapterForm.titleHasNul' satisfies MessageKey }),
  kind: z.enum(['Text', 'Pdf', 'Video']),
  text: z.string(),
});

type ChapterFields = z.infer<typeof chapterFields>;
const isText = (values: ChapterFields) => values.kind === 'Text';

/** A check of the content runs even while the title is refused, as long as the kind and the content are there. */
const alongsideTheTitle = {
  when: ({ value }: { value: unknown }) => chapterFields.pick({ kind: true, text: true }).safeParse(value).success,
};

/**
 * The chapter form's schema (ChapterTitle: 1 to 150 characters, trimmed, no NUL; MarkdownText: 1 to 200,000
 * characters, not blank, no NUL), checked before the host checks them again. A PDF or video chapter keeps the file of
 * the chapter it replaces, when that is of its kind; until a file can be chosen here, any other is refused at the kind.
 */
export function chapterSchema(chapter?: EditorialChapterDto) {
  const keepsFile = (values: ChapterFields) => chapter !== undefined && chapter.kind === values.kind;
  return chapterFields
    .refine((values) => !isText(values) || values.text.trim() !== '', {
      ...alongsideTheTitle,
      path: ['text'],
      error: 'editorial.chapterForm.textRequired' satisfies MessageKey,
    })
    .refine((values) => !isText(values) || values.text.length <= maxTextLength, {
      ...alongsideTheTitle,
      path: ['text'],
      error: 'editorial.chapterForm.textTooLong' satisfies MessageKey,
    })
    .refine((values) => !isText(values) || hasNoNul(values.text), {
      ...alongsideTheTitle,
      path: ['text'],
      error: 'editorial.chapterForm.textHasNul' satisfies MessageKey,
    })
    .refine((values) => isText(values) || keepsFile(values), {
      ...alongsideTheTitle,
      path: ['kind'],
      error: 'editorial.chapterForm.fileRequired' satisfies MessageKey,
    })
    .transform((values): EditorialChapterRequest => ({
      title: values.title,
      kind: values.kind,
      text: isText(values) ? values.text : null,
      file: isText(values) ? null : (chapter?.file ?? null),
      durationSeconds: values.kind === 'Video' ? (chapter?.durationSeconds ?? null) : null,
    }));
}
