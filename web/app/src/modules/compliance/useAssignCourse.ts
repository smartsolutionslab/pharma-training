import { useQueryClient } from '@tanstack/vue-query';
import {
  type AssignComplianceCourseRequest,
  type ComplianceAssignedLearnerDto,
  type ComplianceRefusedLearnerDto,
  getGetCatalogCoursesQueryKey,
  usePostCompliancePharmaciesIdAssignments,
} from '@/api/generated';
import { ApiError } from '@/api/baseApi';
import { t } from '@/i18n';

/** The host's codes for a due date it refuses. */
export type DueDateCode =
  'compliance.due-date-in-the-past' | 'compliance.due-date-too-far' | 'compliance.due-date-missing';

const dueDateCodes: readonly string[] = [
  'compliance.due-date-in-the-past',
  'compliance.due-date-too-far',
  'compliance.due-date-missing',
] satisfies DueDateCode[];

/**
 * What an assignment came to, sorted by what the user can do about it: see who was assigned and who refused (done),
 * fix the due date, choose another course, nothing here at all (notAvailable, in the host's words), look again and send
 * again (changed), or try again (failed).
 */
export type AssignOutcome =
  | { kind: 'done'; assigned: ComplianceAssignedLearnerDto[]; refused: ComplianceRefusedLearnerDto[] }
  | { kind: 'dueDate'; code: DueDateCode }
  | { kind: 'courseGone' }
  | { kind: 'notAvailable'; message: string }
  | { kind: 'changed' }
  | { kind: 'failed' };

function isDueDateCode(code: string): code is DueDateCode {
  return dueDateCodes.includes(code);
}

/**
 * Assigns a course in a pharmacy (POST /compliance/pharmacies/{id}/assignments, #37). A course the host no longer
 * assigns marks every cached catalog page stale, so the course step reads it again.
 */
export function useAssignCourse() {
  const queryClient = useQueryClient();
  const { mutateAsync, isPending } = usePostCompliancePharmaciesIdAssignments();

  async function refusal(error: unknown): Promise<AssignOutcome> {
    if (!(error instanceof ApiError) || error.status >= 500 || error.failureCode === undefined) {
      return { kind: 'failed' };
    }
    const code = error.failureCode;
    if (isDueDateCode(code)) {
      return { kind: 'dueDate', code };
    }
    switch (code) {
      case 'compliance.course-retired':
      case 'compliance.course-not-found':
        await queryClient.invalidateQueries({ queryKey: getGetCatalogCoursesQueryKey() });
        return { kind: 'courseGone' };
      case 'compliance.capability-missing':
      case 'compliance.pharmacy-not-found':
        return { kind: 'notAvailable', message: error.serverMessage ?? t('compliance.assign.notAllowed') };
      case 'compliance.assignments-changed':
      case 'already-exists':
        return { kind: 'changed' };
      default:
        return { kind: 'failed' };
    }
  }

  async function assign(pharmacyId: string, request: AssignComplianceCourseRequest): Promise<AssignOutcome> {
    try {
      const { assigned, refused } = await mutateAsync({ id: pharmacyId, data: request });
      return { kind: 'done', assigned, refused };
    } catch (error) {
      return refusal(error);
    }
  }

  return { assign, isAssigning: isPending };
}
