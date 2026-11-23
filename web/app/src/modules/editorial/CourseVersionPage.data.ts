// The example data CourseVersionPage's axe check shows the page with, by hook of the generated client: in examples
// with data, in emptyExamples with nothing to show. A hook without an example answers as if its request were still on
// its way.

const versionId = '0198c1a0-0000-7000-8000-0000000000c1';

// A draft (GET /editorial/course-versions/{id}) with a text, a PDF and a video chapter, and questions about two of them.
const draft = {
  id: versionId,
  courseId: '0198c1a0-0000-7000-8000-0000000000d1',
  number: 2,
  title: 'Hygiene in der Apotheke',
  status: 'Draft',
  chapters: [
    {
      id: '0198c1a0-0000-7000-8000-0000000000e1',
      position: 1,
      title: 'Einleitung',
      kind: 'Text',
      text: '# Willkommen\n\nDas ist **wichtig**: [die Leitlinie](https://example.org/leitlinie).',
      file: null,
      durationSeconds: null,
    },
    {
      id: '0198c1a0-0000-7000-8000-0000000000e2',
      position: 2,
      title: 'Handout',
      kind: 'Pdf',
      text: null,
      file: `course-versions/${versionId}/0198c1a0-0000-7000-8000-0000000000f2.pdf`,
      durationSeconds: null,
    },
    {
      id: '0198c1a0-0000-7000-8000-0000000000e3',
      position: 3,
      title: 'Händewaschen im Video',
      kind: 'Video',
      text: null,
      file: `course-versions/${versionId}/0198c1a0-0000-7000-8000-0000000000f3.mp4`,
      durationSeconds: 725,
    },
  ],
  questions: [
    {
      id: '0198c1a0-0000-7000-8000-0000000000a1',
      position: 1,
      chapterId: '0198c1a0-0000-7000-8000-0000000000e1',
      text: 'Wie lange waschen Sie die Hände?',
      type: 'SingleChoice',
      options: [
        { id: '0198c1a0-0000-7000-8000-0000000000b1', text: '30 Sekunden', correct: true },
        { id: '0198c1a0-0000-7000-8000-0000000000b2', text: '5 Sekunden', correct: false },
      ],
      provenance: 'Human',
      review: 'Approved',
      reviewedBy: 'editor',
      reviewedAt: '2026-11-20T09:30:00Z',
      approvedByName: 'Emma Editor',
      approvedAt: '2026-11-20T09:30:00Z',
    },
  ],
  blueprint: { questions: 10, passingScorePercent: 80, validityMonths: 12 },
};

// Its return history (GET /editorial/course-versions/{id}/returns, #201), newest first: a two-line reason by the
// signed-in user's colleague, stored with her name (#220), and an older one without a name whose reason looks like
// markup, shown as the characters it is.
const returns = {
  items: [
    {
      id: '0198c1a0-0000-7000-8000-0000000000a9',
      returnedBy: '0198c1a0-0000-7000-8000-0000000000ea',
      returnedByName: 'Carla Kollegin',
      returnedAt: '2026-11-21T14:05:00Z',
      reason: 'Kapitel 2 braucht eine Quelle.\nDie Tabelle in Kapitel 3 ist veraltet.',
    },
    {
      id: '0198c1a0-0000-7000-8000-0000000000a8',
      returnedBy: '0198c1a0-0000-7000-8000-0000000000ea',
      returnedByName: null,
      returnedAt: '2026-11-19T08:30:00Z',
      reason: 'Frage 1: <b>30 Sekunden</b> ist zu kurz.',
    },
  ],
  totalCount: 2,
  page: 1,
  pageSize: 20,
};

export const examples: Record<string, unknown> = {
  useGetEditorialCourseVersionsId: draft,
  useGetEditorialCourseVersionsIdReturns: returns,
};

// The same version in review (#26): the checklist, Publish and Return, no dialog open.
export const inReviewExamples: Record<string, unknown> = {
  useGetEditorialCourseVersionsId: { ...draft, status: 'InReview' },
  useGetEditorialCourseVersionsIdReturns: returns,
};

// The same version published (#26): Retire beside "Neue Version anlegen", no dialog open.
export const publishedExamples: Record<string, unknown> = {
  useGetEditorialCourseVersionsId: { ...draft, status: 'Published' },
  useGetEditorialCourseVersionsIdReturns: returns,
};

// A draft with no chapters yet and no returns: the page shows its empty states.
export const emptyExamples: Record<string, unknown> = {
  useGetEditorialCourseVersionsId: {
    id: versionId,
    courseId: '0198c1a0-0000-7000-8000-0000000000d1',
    number: 1,
    title: 'Lagerung von Arzneimitteln',
    status: 'Draft',
    chapters: [],
    questions: [],
    blueprint: { questions: 10, passingScorePercent: 80, validityMonths: 12 },
  },
  // Never returned: the history shows its empty state.
  useGetEditorialCourseVersionsIdReturns: { items: [], totalCount: 0, page: 1, pageSize: 20 },
};
