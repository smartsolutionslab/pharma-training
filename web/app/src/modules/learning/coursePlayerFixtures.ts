import type {
  CatalogChapterDto,
  CatalogCourseVersionDto,
  LearningSessionChapterDto,
  LearningSessionDto,
  LearningSessionStatus,
} from '@/api/generated';

// A published course version with a text, a video and a second text chapter, as the catalog answers it
// (GET /catalog/course-versions/{id}), and the learner's session on it (POST /learning/sessions,
// GET /learning/sessions/{id}, POST /learning/sessions/{id}/progress).

export const versionId = '0198c1a0-0000-7000-8000-0000000000c1';
export const sessionId = '0198c1a0-0000-7000-8000-0000000000b1';

export const introduction: CatalogChapterDto = {
  chapterId: '0198c1a0-0000-7000-8000-0000000000e1',
  position: 1,
  title: 'Einleitung',
  kind: 'Text',
  videoDurationSeconds: null,
  text: '# Willkommen\n\nDas ist **wichtig**.',
};
export const handwashing: CatalogChapterDto = {
  chapterId: '0198c1a0-0000-7000-8000-0000000000e2',
  position: 2,
  title: 'Händewaschen im Video',
  kind: 'Video',
  videoDurationSeconds: 600,
  text: null,
};
export const summary: CatalogChapterDto = {
  chapterId: '0198c1a0-0000-7000-8000-0000000000e3',
  position: 3,
  title: 'Zusammenfassung',
  kind: 'Text',
  videoDurationSeconds: null,
  text: '## Merksätze\n\nHände **30 Sekunden** waschen.',
};
/** A PDF chapter, for a course that has one: courseVersion([introduction, handout]). */
export const handout: CatalogChapterDto = {
  chapterId: '0198c1a0-0000-7000-8000-0000000000e4',
  position: 4,
  title: 'Merkblatt Händehygiene',
  kind: 'Pdf',
  videoDurationSeconds: null,
  text: null,
};

export function courseVersion(
  chapters: CatalogChapterDto[] = [introduction, handwashing, summary],
): CatalogCourseVersionDto {
  return {
    courseId: '0198c1a0-0000-7000-8000-0000000000d1',
    courseVersionId: versionId,
    versionNumber: 2,
    title: 'Hygiene in der Apotheke',
    validityMonths: 12,
    isRetired: false,
    chapters,
  };
}

/** A chapter's progress in the session: its percent, done once completed. */
export interface ChapterProgress {
  percent: number;
  completed: boolean;
}

const open: ChapterProgress = { percent: 0, completed: false };

/**
 * The session as the host stores it, each catalog chapter at the progress the test gives it (none yet by default). The
 * session's chapters come in another order than the catalog's: the page joins them by chapterId, never by index.
 */
export function session(
  progress: Record<string, ChapterProgress> = {},
  status: LearningSessionStatus = 'InProgress',
  chapters: CatalogChapterDto[] = [introduction, handwashing, summary],
): LearningSessionDto {
  const sessionChapters: LearningSessionChapterDto[] = chapters
    .map((chapter) => {
      const { percent, completed } = progress[chapter.chapterId] ?? open;
      return {
        chapterId: chapter.chapterId,
        position: chapter.position,
        kind: chapter.kind,
        progressPercent: percent,
        completed,
      };
    })
    .reverse();
  return {
    id: sessionId,
    courseId: '0198c1a0-0000-7000-8000-0000000000d1',
    courseVersionId: versionId,
    status,
    chapters: sessionChapters,
  };
}

/** A refusal as the host sends it: problem details with the code. */
export function refusal(status: number, code: string): Response {
  return new Response(JSON.stringify({ title: 'Refused', status, detail: 'Not for the user.', code }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  });
}
