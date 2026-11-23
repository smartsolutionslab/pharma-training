import type { EditorialCourseVersionDto } from '@/api/generated';
import type { MessageKey } from '@/i18n';
import { testCounts } from './testCounts';

/** Why four eyes refused this page's publish: the editor submitted the version, or approved some of its questions. */
export type PublishRefusal = 'fourEyes' | 'publisherApproved';

/**
 * One item of the publishing checklist: whether it is met, and the catalog key and values of its text. A plural text
 * is chosen by its values' `count`.
 */
export interface ChecklistItem {
  name: 'chapters' | 'questions' | 'approved' | 'submitted' | PublishRefusal;
  met: boolean;
  key: MessageKey;
  values: Record<string, number>;
}

/**
 * What blocks publishing a version: chapters, the blueprint's number of questions, every question approved, the version
 * submitted, and last, once this page's publish was refused by four eyes, why.
 */
export function publishChecklist(
  version: Pick<EditorialCourseVersionDto, 'status' | 'chapters' | 'questions' | 'blueprint'>,
  refusal?: PublishRefusal,
): ChecklistItem[] {
  const { required, held, approved } = testCounts(version);
  const items: ChecklistItem[] = [
    version.chapters.length > 0
      ? { name: 'chapters', met: true, key: 'editorial.workflow.checklist.chapters', values: {} }
      : { name: 'chapters', met: false, key: 'editorial.workflow.checklist.chaptersMissing', values: {} },
    questionsItem(held, required),
    approvedItem(approved, held),
    version.status === 'InReview'
      ? { name: 'submitted', met: true, key: 'editorial.workflow.checklist.submitted', values: {} }
      : { name: 'submitted', met: false, key: 'editorial.workflow.checklist.notSubmitted', values: {} },
  ];
  if (refusal !== undefined) {
    items.push({ name: refusal, met: false, key: `editorial.workflow.checklist.${refusal}`, values: {} });
  }
  return items;
}

function questionsItem(held: number, required: number): ChecklistItem {
  if (held === required) {
    return { name: 'questions', met: true, key: 'editorial.workflow.checklist.questions', values: { count: required } };
  }
  return {
    name: 'questions',
    met: false,
    key:
      held < required
        ? 'editorial.workflow.checklist.questionsMissing'
        : 'editorial.workflow.checklist.questionsTooMany',
    values: { held, count: required },
  };
}

function approvedItem(approved: number, held: number): ChecklistItem {
  if (approved === 0) {
    return { name: 'approved', met: false, key: 'editorial.workflow.checklist.approvedNone', values: {} };
  }
  if (approved < held) {
    return {
      name: 'approved',
      met: false,
      key: 'editorial.workflow.checklist.approvedSome',
      values: { count: approved, held },
    };
  }
  return { name: 'approved', met: true, key: 'editorial.workflow.checklist.approved', values: {} };
}
