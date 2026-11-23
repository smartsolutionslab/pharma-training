// The example data CourseTestPage's axe check shows the page with, by hook of the generated client: in examples with
// data, in emptyExamples with nothing to show. A hook without an example answers as if its request were still on its
// way.

const versionId = '0198c1a0-0000-7000-8000-0000000000c1';
const textChapter = {
  id: '0198c1a0-0000-7000-8000-0000000000e1',
  position: 1,
  title: 'Einleitung',
  kind: 'Text',
  text: '# Willkommen\n\nHände werden **30 Sekunden** gewaschen.',
  file: null,
  durationSeconds: null,
};
const pdfChapter = {
  id: '0198c1a0-0000-7000-8000-0000000000e2',
  position: 2,
  title: 'Handout',
  kind: 'Pdf',
  text: null,
  file: `course-versions/${versionId}/0198c1a0-0000-7000-8000-0000000000f2.pdf`,
  durationSeconds: null,
};

// A draft (GET /editorial/course-versions/{id}) with a text and a PDF chapter, the blueprint's defaults (10 questions,
// 80 %, 12 months), an approved AI-drafted question, an unreviewed AI-drafted one and an unreviewed hand-written one.
export const examples: Record<string, unknown> = {
  useGetEditorialCourseVersionsId: {
    id: versionId,
    courseId: '0198c1a0-0000-7000-8000-0000000000d1',
    number: 2,
    title: 'Hygiene in der Apotheke',
    status: 'Draft',
    chapters: [textChapter, pdfChapter],
    questions: [
      {
        id: '0198c1a0-0000-7000-8000-0000000000a1',
        position: 1,
        chapterId: textChapter.id,
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
        approvedByName: 'Emma Editor',
        approvedAt: '2026-11-20T09:30:00Z',
      },
      {
        id: '0198c1a0-0000-7000-8000-0000000000a2',
        position: 2,
        chapterId: textChapter.id,
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
        approvedByName: null,
        approvedAt: null,
      },
      {
        id: '0198c1a0-0000-7000-8000-0000000000a3',
        position: 3,
        chapterId: pdfChapter.id,
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
        approvedByName: null,
        approvedAt: null,
      },
    ],
    blueprint: { questions: 10, passingScorePercent: 80, validityMonths: 12 },
  },
};

// A draft with a text chapter and no questions yet: the question list shows its empty state.
export const emptyExamples: Record<string, unknown> = {
  useGetEditorialCourseVersionsId: {
    id: versionId,
    courseId: '0198c1a0-0000-7000-8000-0000000000d1',
    number: 1,
    title: 'Lagerung von Arzneimitteln',
    status: 'Draft',
    chapters: [textChapter],
    questions: [],
    blueprint: { questions: 10, passingScorePercent: 80, validityMonths: 12 },
  },
};
