import type {
  EditorialChapterDto,
  EditorialCourseVersionDto,
  EditorialCourseVersionStatus,
  EditorialQuestionDto,
} from '@/api/generated';

// The course version the test page's tests show (GET /editorial/course-versions/{id}): a draft with a text and a PDF
// chapter, the blueprint's defaults (10 questions, 80 %, 12 months) and three questions, listed out of their order: an
// approved AI-drafted one, an unreviewed AI-drafted one and an unreviewed hand-written one about the PDF.

export const versionId = '0198c1a0-0000-7000-8000-0000000000c1';

export const introduction: EditorialChapterDto = {
  id: '0198c1a0-0000-7000-8000-0000000000e1',
  position: 1,
  title: 'Einleitung',
  kind: 'Text',
  text: '# Willkommen\n\nHände werden **30 Sekunden** gewaschen.',
  file: null,
  durationSeconds: null,
};

export const handout: EditorialChapterDto = {
  id: '0198c1a0-0000-7000-8000-0000000000e2',
  position: 2,
  title: 'Handout',
  kind: 'Pdf',
  text: null,
  file: `course-versions/${versionId}/0198c1a0-0000-7000-8000-0000000000f2.pdf`,
  durationSeconds: null,
};

/** Approved by another editor; who it was is T8's, pending a decision. */
export const approvedAi: EditorialQuestionDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a1',
  position: 1,
  chapterId: introduction.id,
  text: 'Wie lange waschen Sie die Hände?',
  type: 'SingleChoice',
  options: [
    { id: '0198c1a0-0000-7000-8000-0000000000b1', text: '30 Sekunden', correct: true },
    { id: '0198c1a0-0000-7000-8000-0000000000b2', text: '5 Sekunden', correct: false },
  ],
  provenance: 'AiDrafted',
  review: 'Approved',
  reviewedBy: '0198c1a0-0000-7000-8000-0000000000d9',
  reviewedAt: '2026-11-20T09:30:00Z',
};

export const unreviewedAi: EditorialQuestionDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a2',
  position: 2,
  chapterId: introduction.id,
  text: 'Was gehört zur Händehygiene?',
  type: 'MultipleChoice',
  options: [
    { id: '0198c1a0-0000-7000-8000-0000000000b3', text: 'Seife', correct: true },
    { id: '0198c1a0-0000-7000-8000-0000000000b4', text: 'Desinfektionsmittel', correct: true },
    { id: '0198c1a0-0000-7000-8000-0000000000b5', text: 'Kaltes Wasser allein', correct: false },
  ],
  provenance: 'AiDrafted',
  review: 'Unreviewed',
  reviewedBy: null,
  reviewedAt: null,
};

export const handWritten: EditorialQuestionDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a3',
  position: 3,
  chapterId: handout.id,
  text: 'Wo liegt das Handout aus?',
  type: 'SingleChoice',
  options: [
    { id: '0198c1a0-0000-7000-8000-0000000000b6', text: 'Am HV-Tisch', correct: true },
    { id: '0198c1a0-0000-7000-8000-0000000000b7', text: 'Im Labor', correct: false },
  ],
  provenance: 'Human',
  review: 'Unreviewed',
  reviewedBy: null,
  reviewedAt: null,
};

/** The version as the host answers it, a draft with both chapters and all three questions unless the test says otherwise. */
export function versionOf(
  status: EditorialCourseVersionStatus = 'Draft',
  changes: Partial<Pick<EditorialCourseVersionDto, 'chapters' | 'questions' | 'blueprint'>> = {},
): EditorialCourseVersionDto {
  return {
    id: versionId,
    courseId: '0198c1a0-0000-7000-8000-0000000000d1',
    number: 2,
    title: 'Hygiene in der Apotheke',
    status,
    chapters: [introduction, handout],
    questions: [handWritten, approvedAi, unreviewedAi],
    blueprint: { questions: 10, passingScorePercent: 80, validityMonths: 12 },
    ...changes,
  };
}

/** A refusal as the host sends it: problem details with a module's code. */
export function refusal(status: number, code: string, detail: string): Response {
  return new Response(JSON.stringify({ title: 'Refused', status, detail, code }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  });
}
