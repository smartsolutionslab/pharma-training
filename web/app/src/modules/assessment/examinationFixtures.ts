import type {
  AssessmentAttemptDto,
  AssessmentAttemptQuestionDto,
  AssessmentAttemptResultDto,
  AssessmentExaminationDto,
  CatalogChapterDto,
  CatalogCourseVersionDto,
} from '@/api/generated';

// A learner's examination (#48) as Assessment answers it (GET /assessment/examinations/by-session/{sessionId}), the
// attempt the start opens (POST …/attempts), the result a submit answers (POST …/attempts/current/submit), and the
// course version the catalog answers (GET /catalog/course-versions/{id}) for the course's title and chapter titles.

export const sessionId = '0198c1a0-0000-7000-8000-0000000000b1';
export const examinationId = '0198c1a0-0000-7000-8000-0000000000a9';
export const attemptId = '0198c1a0-0000-7000-8000-0000000000f1';
export const versionId = '0198c1a0-0000-7000-8000-0000000000c1';
export const courseId = '0198c1a0-0000-7000-8000-0000000000d1';

export const introduction: CatalogChapterDto = {
  chapterId: '0198c1a0-0000-7000-8000-0000000000e1',
  position: 1,
  title: 'Einleitung',
  kind: 'Text',
  videoDurationSeconds: null,
  text: 'Hallo.',
};
export const hygienePlan: CatalogChapterDto = {
  chapterId: '0198c1a0-0000-7000-8000-0000000000e2',
  position: 2,
  title: 'Hygieneplan',
  kind: 'Text',
  videoDurationSeconds: null,
  text: 'Hände 30 Sekunden waschen.',
};
/** A chapter a result may name that the catalog's answer does not hold. */
export const unknownChapterId = '0198c1a0-0000-7000-8000-0000000000ef';

export const courseTitle = 'Hygiene in der Apotheke';

export function courseVersion(chapters: CatalogChapterDto[] = [introduction, hygienePlan]): CatalogCourseVersionDto {
  return {
    courseId,
    courseVersionId: versionId,
    versionNumber: 1,
    title: courseTitle,
    validityMonths: 12,
    isRetired: false,
    status: 'Current',
    chapters,
  };
}

// The questions in the server's shuffled order (ADR 0022): "5 Sekunden" before "30 Sekunden", "Sandalen" between the
// others. The page shows them as received and never sorts them.
export const durationQuestion: AssessmentAttemptQuestionDto = {
  id: '0198c1a0-0000-7000-8000-000000000101',
  type: 'SingleChoice',
  text: 'Wie lange waschen Sie die Hände?',
  options: [
    { id: '0198c1a0-0000-7000-8000-000000000111', text: '5 Sekunden' },
    { id: '0198c1a0-0000-7000-8000-000000000112', text: '30 Sekunden' },
  ],
  chosenOptionIds: [],
};
export const fiveSeconds = '0198c1a0-0000-7000-8000-000000000111';
export const thirtySeconds = '0198c1a0-0000-7000-8000-000000000112';

export const protectionQuestion: AssessmentAttemptQuestionDto = {
  id: '0198c1a0-0000-7000-8000-000000000102',
  type: 'MultipleChoice',
  text: 'Was gehört zur Schutzausrüstung?',
  options: [
    { id: '0198c1a0-0000-7000-8000-000000000121', text: 'Kittel' },
    { id: '0198c1a0-0000-7000-8000-000000000123', text: 'Sandalen' },
    { id: '0198c1a0-0000-7000-8000-000000000122', text: 'Handschuhe' },
  ],
  chosenOptionIds: [],
};
export const coat = '0198c1a0-0000-7000-8000-000000000121';
export const sandals = '0198c1a0-0000-7000-8000-000000000123';
export const gloves = '0198c1a0-0000-7000-8000-000000000122';

export function attempt(
  questions: AssessmentAttemptQuestionDto[] = [durationQuestion, protectionQuestion],
  number = 1,
): AssessmentAttemptDto {
  return { id: attemptId, number, questions };
}

/** A result as the submit answers it: failed at 50 % of 80 % by default, with two attempts left. */
export function result(overrides: Partial<AssessmentAttemptResultDto> = {}): AssessmentAttemptResultDto {
  return {
    attemptId,
    number: 1,
    scorePercent: 50,
    passingScorePercent: 80,
    outcome: 'Failed',
    chaptersToRepeat: [hygienePlan.chapterId, introduction.chapterId],
    attemptsLeft: 2,
    ...overrides,
  };
}

/** The examination, unlocked and without an attempt by default: three attempts, none used. */
export function examination(overrides: Partial<AssessmentExaminationDto> = {}): AssessmentExaminationDto {
  return {
    id: examinationId,
    sessionId,
    courseId,
    courseVersionId: versionId,
    status: 'Unlocked',
    attemptsUsed: 0,
    maxAttempts: 3,
    currentAttempt: null,
    lastResult: null,
    ...overrides,
  };
}

/** The examination with an open attempt of the questions, each with the options the server holds as chosen. */
export function inAttempt(
  questions: AssessmentAttemptQuestionDto[] = [durationQuestion, protectionQuestion],
  overrides: Partial<AssessmentExaminationDto> = {},
): AssessmentExaminationDto {
  return examination({ status: 'InAttempt', currentAttempt: attempt(questions), ...overrides });
}

/** The question with the options the server holds as chosen. */
export function chosen(question: AssessmentAttemptQuestionDto, optionIds: string[]): AssessmentAttemptQuestionDto {
  return { ...question, chosenOptionIds: optionIds };
}

/** A refusal as the host sends it: problem details with the code. */
export function refusal(status: number, code: string): Response {
  return new Response(JSON.stringify({ title: 'Refused', status, detail: 'Not for the user.', code }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  });
}

/** A module's refusal with a code the page has no words for: the host's words are shown (ADR 0007). */
export function unknownRefusal(status: number, words: string): Response {
  return new Response(JSON.stringify({ title: words, status, code: 'assessment.something-new' }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  });
}

/** A saved answer: 204 without a body. */
export const saved = () => new Response(null, { status: 204 });

/** A host answer the test releases when it wants. */
export function deferred() {
  let release: (response: Response) => void = () => undefined;
  const answer = new Promise<Response>((resolve) => {
    release = resolve;
  });
  return { answer, release };
}
