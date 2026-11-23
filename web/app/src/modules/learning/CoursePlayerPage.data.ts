// The example data CoursePlayerPage's axe check shows the page with, by hook of the generated client: in examples
// with data, in emptyExamples with nothing to show. A hook without an example answers as if its request were still on
// its way; the start (usePostLearningSessions) answers its example as its data.

const versionId = '0198c1a0-0000-7000-8000-0000000000c1';
const sessionId = '0198c1a0-0000-7000-8000-0000000000b1';
const courseId = '0198c1a0-0000-7000-8000-0000000000d1';
const introduction = '0198c1a0-0000-7000-8000-0000000000e1';
const handwashing = '0198c1a0-0000-7000-8000-0000000000e2';
const summary = '0198c1a0-0000-7000-8000-0000000000e3';

// The version as the catalog answers it (GET /catalog/course-versions/{id}): a text, a video and a second text chapter.
const version = {
  courseId,
  courseVersionId: versionId,
  versionNumber: 2,
  title: 'Hygiene in der Apotheke',
  validityMonths: 12,
  isRetired: false,
  status: 'Current',
  chapters: [
    {
      chapterId: introduction,
      position: 1,
      title: 'Einleitung',
      kind: 'Text',
      videoDurationSeconds: null,
      text: '# Willkommen\n\nDas ist **wichtig**: [die Leitlinie](https://example.org/leitlinie).',
    },
    {
      chapterId: handwashing,
      position: 2,
      title: 'Händewaschen im Video',
      kind: 'Video',
      videoDurationSeconds: 600,
      text: null,
    },
    {
      chapterId: summary,
      position: 3,
      title: 'Zusammenfassung',
      kind: 'Text',
      videoDurationSeconds: null,
      text: '## Merksätze\n\nHände **30 Sekunden** waschen.',
    },
  ],
};

// The learner's session (POST /learning/sessions, GET /learning/sessions/{id}): the introduction done, the video at
// 40 %, the summary open, so the overview shows each state. The axe check opens the summary with ?chapter=, a text
// chapter; the video waits for its own task.
const started = {
  id: sessionId,
  courseId,
  courseVersionId: versionId,
  status: 'InProgress',
  chapters: [
    { chapterId: introduction, position: 1, kind: 'Text', progressPercent: 100, completed: true },
    { chapterId: handwashing, position: 2, kind: 'Video', progressPercent: 40, completed: false },
    { chapterId: summary, position: 3, kind: 'Text', progressPercent: 0, completed: false },
  ],
};

export const examples: Record<string, unknown> = {
  useGetCatalogCourseVersionsId: version,
  usePostLearningSessions: started,
  useGetLearningSessionsId: started,
  useGetLearningMediaChapterId: {
    url: 'https://storage.example.org/media/handwashing.mp4?sig=example',
    expiresAt: '2026-11-23T10:15:00Z',
    contentType: 'video/mp4',
    kind: 'Video',
  },
};

const emptySession = { ...started, chapters: [] };

export const emptyExamples: Record<string, unknown> = {
  useGetCatalogCourseVersionsId: { ...version, chapters: [] },
  usePostLearningSessions: emptySession,
  useGetLearningSessionsId: emptySession,
};

/** The chapter the axe check opens with ?chapter=: a text chapter, so the check sees its Markdown and its hint. */
export const shownChapterId = summary;
