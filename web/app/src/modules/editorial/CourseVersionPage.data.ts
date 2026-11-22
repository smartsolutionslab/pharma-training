// The example data CourseVersionPage's axe check shows the page with, by hook of the generated client: in examples
// with data, in emptyExamples with nothing to show. A hook without an example answers as if its request were still on
// its way.

const versionId = '0198c1a0-0000-7000-8000-0000000000c1';

// A draft (GET /editorial/course-versions/{id}) with a text, a PDF and a video chapter, and questions about two of them.
export const examples: Record<string, unknown> = {
  useGetEditorialCourseVersionsId: {
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
      },
    ],
    blueprint: { questions: 10, passingScorePercent: 80, validityMonths: 12 },
  },
};

// A draft with no chapters yet: the page shows its empty state.
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
};
