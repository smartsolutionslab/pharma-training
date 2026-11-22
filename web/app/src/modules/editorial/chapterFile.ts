import type { MessageKey } from '@/i18n';

/** The kinds of chapter that hold a file. */
export type FileKind = 'Pdf' | 'Video';

/** What a chapter's file may be (spec 2.4): a PDF of at most 50 MiB, an MP4 video of at most 2 GiB. */
export const fileLimits = {
  Pdf: { contentType: 'application/pdf', maxBytes: 52_428_800 },
  Video: { contentType: 'video/mp4', maxBytes: 2_147_483_648 },
} as const satisfies Record<FileKind, { contentType: string; maxBytes: number }>;

/** Why a file is refused. */
export type FileProblem = 'wrongType' | 'empty' | 'tooLarge';

const fileProblems: readonly string[] = ['wrongType', 'empty', 'tooLarge'] satisfies FileProblem[];

/** Whether the reason is the file's own problem, rather than its way to storage. */
export const isFileProblem = (reason: string): reason is FileProblem => fileProblems.includes(reason);

/** Why the file cannot be a chapter's of this kind under these limits, or undefined when it can. */
export function fileProblem(
  file: File,
  { contentType, maxBytes }: { contentType: string; maxBytes: number },
): FileProblem | undefined {
  if (file.type !== contentType) {
    return 'wrongType';
  }
  if (file.size === 0) {
    return 'empty';
  }
  if (file.size > maxBytes) {
    return 'tooLarge';
  }
  return undefined;
}

/** The message refusing a file of the kind for the problem; fileTooLarge takes { max } in megabytes. */
export function fileProblemKey(kind: FileKind, problem: FileProblem): MessageKey {
  switch (problem) {
    case 'wrongType':
      return `editorial.chapterForm.fileWrongType.${kind}`;
    case 'empty':
      return 'editorial.chapterForm.fileEmpty';
    case 'tooLarge':
      return 'editorial.chapterForm.fileTooLarge';
  }
}

/** The kind's size limit in megabytes (MiB), as the refusal names it. */
export const maxMegabytes = (kind: FileKind) => fileLimits[kind].maxBytes / 1_048_576;
