import { z } from 'zod';
import type { EditorialChapterDto, EditorialChapterRequest } from '@/api/generated';
import type { MessageKey } from '@/i18n';
import { type FileKind, fileLimits, fileProblem, fileProblemKey } from './chapterFile';

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
  /** A file chosen for a PDF or video chapter, not yet uploaded. */
  file: z.custom<File | undefined>((value) => value === undefined || value instanceof File),
  /** A video's duration as minutes:seconds. */
  duration: z.string(),
});

type ChapterFields = z.infer<typeof chapterFields>;
const isText = (values: ChapterFields) => values.kind === 'Text';
const isFile = (values: ChapterFields): values is ChapterFields & { kind: FileKind } => values.kind !== 'Text';
const isVideo = (values: ChapterFields) => values.kind === 'Video';

/** A duration as minutes:seconds (12:05, minutes not capped at 59), from 0:01 to 240:00 (VideoDuration). */
const durationFormat = /^(\d{1,3}):([0-5]\d)$/;
const maxDurationSeconds = 14_400;

/** The seconds a duration typed as minutes:seconds stands for, or undefined when it is none or out of range. */
export function durationSeconds(typed: string): number | undefined {
  const [, minutes, seconds] = durationFormat.exec(typed.trim()) ?? [];
  if (minutes === undefined || seconds === undefined) {
    return undefined;
  }
  const total = Number(minutes) * 60 + Number(seconds);
  return total >= 1 && total <= maxDurationSeconds ? total : undefined;
}

/** What the form hands over: the chapter, and the file to upload first when one was chosen. */
export interface ChapterSubmission {
  request: EditorialChapterRequest;
  upload?: File;
}

/** A check of the content runs even while the title is refused, as long as the kind and the content are there. */
const alongsideTheTitle = {
  when: ({ value }: { value: unknown }) =>
    chapterFields.pick({ kind: true, text: true, file: true, duration: true }).safeParse(value).success,
};

/**
 * The chapter form's schema (ChapterTitle: 1 to 150 characters, trimmed, no NUL; MarkdownText: 1 to 200,000
 * characters, not blank, no NUL), checked before the host checks them again. A PDF or video chapter keeps the file of
 * the chapter it replaces, when that is of its kind, unless a new file is chosen: a PDF of at most 50 MiB or an MP4
 * video of at most 2 GiB, uploaded before the chapter is saved. A video takes its duration as minutes:seconds.
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
    .superRefine((values, context) => {
      if (!isFile(values)) {
        return;
      }
      if (values.file !== undefined) {
        const problem = fileProblem(values.file, fileLimits[values.kind]);
        if (problem !== undefined) {
          context.addIssue({ code: 'custom', path: ['file'], message: fileProblemKey(values.kind, problem) });
        }
      } else if (!keepsFile(values)) {
        context.addIssue({
          code: 'custom',
          path: ['file'],
          message: 'editorial.chapterForm.fileRequired' satisfies MessageKey,
        });
      }
    }, alongsideTheTitle)
    .superRefine((values, context) => {
      if (!isVideo(values)) {
        return;
      }
      if (values.duration.trim() === '') {
        context.addIssue({
          code: 'custom',
          path: ['duration'],
          message: 'editorial.chapterForm.durationRequired' satisfies MessageKey,
        });
      } else if (durationSeconds(values.duration) === undefined) {
        context.addIssue({
          code: 'custom',
          path: ['duration'],
          message: 'editorial.chapterForm.durationInvalid' satisfies MessageKey,
        });
      }
    }, alongsideTheTitle)
    .transform((values): ChapterSubmission => ({
      request: {
        title: values.title,
        kind: values.kind,
        text: isText(values) ? values.text : null,
        file: isText(values) || values.file !== undefined ? null : (chapter?.file ?? null),
        durationSeconds: isVideo(values) ? (durationSeconds(values.duration) ?? null) : null,
      },
      upload: isFile(values) ? values.file : undefined,
    }));
}
