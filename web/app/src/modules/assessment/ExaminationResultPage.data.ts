// The example data ExaminationResultPage's axe check shows the page with, by hook of the generated client: in examples
// the test locked after the last failed attempt, with two chapters to repeat; in emptyExamples no attempt submitted.
// A hook without an example answers as if its request were still on its way.

const sessionId = '0198c1a0-0000-7000-8000-0000000000b1';
const versionId = '0198c1a0-0000-7000-8000-0000000000c1';
const courseId = '0198c1a0-0000-7000-8000-0000000000d1';
const introduction = '0198c1a0-0000-7000-8000-0000000000e1';
const hygienePlan = '0198c1a0-0000-7000-8000-0000000000e2';

// The version as the catalog answers it (GET /catalog/course-versions/{id}), for the title and the chapters' titles.
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
      chapterId: introduction,
      position: 1,
      title: 'Einleitung',
      kind: 'Text',
      videoDurationSeconds: null,
      text: 'Hallo.',
    },
    {
      chapterId: hygienePlan,
      position: 2,
      title: 'Hygieneplan',
      kind: 'Text',
      videoDurationSeconds: null,
      text: 'Hände 30 Sekunden waschen.',
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

const exhausted = {
  ...unlocked,
  status: 'Exhausted',
  attemptsUsed: 3,
  lastResult: {
    attemptId: '0198c1a0-0000-7000-8000-0000000000f3',
    number: 3,
    scorePercent: 50,
    passingScorePercent: 80,
    outcome: 'Failed',
    chaptersToRepeat: [hygienePlan, introduction],
    attemptsLeft: 0,
  },
};

export const examples: Record<string, unknown> = {
  useGetAssessmentExaminationsBySessionSessionId: exhausted,
  useGetCatalogCourseVersionsId: version,
};

export const emptyExamples: Record<string, unknown> = {
  useGetAssessmentExaminationsBySessionSessionId: unlocked,
  useGetCatalogCourseVersionsId: version,
};
