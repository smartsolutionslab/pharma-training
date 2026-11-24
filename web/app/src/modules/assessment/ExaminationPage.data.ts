// The example data ExaminationPage's axe check shows the page with, by hook of the generated client: in examples with
// an attempt open, in emptyExamples unlocked without one. A hook without an example answers as if its request were
// still on its way; a mutation without one has not been asked yet.

const sessionId = '0198c1a0-0000-7000-8000-0000000000b1';
const versionId = '0198c1a0-0000-7000-8000-0000000000c1';
const courseId = '0198c1a0-0000-7000-8000-0000000000d1';

// The version as the catalog answers it (GET /catalog/course-versions/{id}), for the course's title.
const version = {
  courseId,
  courseVersionId: versionId,
  versionNumber: 1,
  title: 'Hygiene in der Apotheke',
  validityMonths: 12,
  isRetired: false,
  status: 'Current',
  chapters: [
    {
      chapterId: '0198c1a0-0000-7000-8000-0000000000e1',
      position: 1,
      title: 'Einleitung',
      kind: 'Text',
      videoDurationSeconds: null,
      text: 'Hallo.',
    },
  ],
};

const unlocked = {
  id: '0198c1a0-0000-7000-8000-0000000000a9',
  sessionId,
  courseId,
  courseVersionId: versionId,
  status: 'Unlocked',
  attemptsUsed: 0,
  maxAttempts: 3,
  currentAttempt: null,
  lastResult: null,
};

// An attempt open: a single-choice question answered, a multiple-choice question not yet, in the server's order.
const inAttempt = {
  ...unlocked,
  status: 'InAttempt',
  currentAttempt: {
    id: '0198c1a0-0000-7000-8000-0000000000f1',
    number: 1,
    questions: [
      {
        id: '0198c1a0-0000-7000-8000-000000000101',
        type: 'SingleChoice',
        text: 'Wie lange waschen Sie die Hände?',
        options: [
          { id: '0198c1a0-0000-7000-8000-000000000111', text: '5 Sekunden' },
          { id: '0198c1a0-0000-7000-8000-000000000112', text: '30 Sekunden' },
        ],
        chosenOptionIds: ['0198c1a0-0000-7000-8000-000000000112'],
      },
      {
        id: '0198c1a0-0000-7000-8000-000000000102',
        type: 'MultipleChoice',
        text: 'Was gehört zur Schutzausrüstung?',
        options: [
          { id: '0198c1a0-0000-7000-8000-000000000121', text: 'Kittel' },
          { id: '0198c1a0-0000-7000-8000-000000000123', text: 'Sandalen' },
          { id: '0198c1a0-0000-7000-8000-000000000122', text: 'Handschuhe' },
        ],
        chosenOptionIds: [],
      },
    ],
  },
};

export const examples: Record<string, unknown> = {
  useGetAssessmentExaminationsBySessionSessionId: inAttempt,
  useGetCatalogCourseVersionsId: version,
};

export const emptyExamples: Record<string, unknown> = {
  useGetAssessmentExaminationsBySessionSessionId: unlocked,
  useGetCatalogCourseVersionsId: version,
};
