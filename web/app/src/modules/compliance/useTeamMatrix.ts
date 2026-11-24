import { keepPreviousData } from '@tanstack/vue-query';
import { computed, type Ref } from 'vue';
import {
  type ComplianceMatrixCourseDto,
  type ComplianceMatrixRowDto,
  type MyPharmacyDto,
  useGetCompliancePharmaciesIdMatrix,
} from '@/api/generated';
import type { MatrixQuery } from './matrixQuery';

/** A pharmacy has tens of members: one page of 50 shows them all, or nearly. */
export const matrixPageSize = 50;

/** The figures the rate block shows: of the whole filter, or of the course chosen. */
export interface MatrixTally {
  assignments: number;
  compliant: number;
  overdue: number;
  /** The share on track, from 0 to 1; null without any assignment. */
  rate: number | null;
}

const noCourses: ComplianceMatrixCourseDto[] = [];
const noRows: ComplianceMatrixRowDto[] = [];

/** A rate as the host sends it (a number, or its text), or null without one. */
export const rateOf = (rate: number | string | null): number | null => (rate === null ? null : Number(rate));

/**
 * The current pharmacy's matrix at the address's page and job function, and what the page shows of it: the course the
 * address chooses, if the matrix has it; the columns, all or that course's; the tally, the matrix's or that course's;
 * the rows and their page. The course is the page's own filter: it asks the host nothing.
 */
export function useTeamMatrix(current: Ref<MyPharmacyDto | undefined>, query: Ref<MatrixQuery>) {
  const matrix = useGetCompliancePharmaciesIdMatrix(
    () => current.value?.id ?? '',
    () => ({ page: query.value.page, pageSize: matrixPageSize, jobFunction: query.value.jobFunction }),
    { query: { enabled: computed(() => current.value !== undefined), placeholderData: keepPreviousData } },
  );

  const courses = computed(() => matrix.data.value?.courses ?? noCourses);
  /** The course chosen, once the matrix has it; a course it does not have counts as none. */
  const course = computed(() =>
    query.value.course === undefined ? undefined : courses.value.find((each) => each.courseId === query.value.course),
  );
  const columns = computed(() => (course.value === undefined ? courses.value : [course.value]));
  const rows = computed(() => matrix.data.value?.members.items ?? noRows);
  const paging = computed(() => matrix.data.value?.members);

  const tally = computed<MatrixTally | undefined>(() => {
    const data = matrix.data.value;
    if (data === undefined) {
      return undefined;
    }
    const source = course.value ?? data;
    return {
      assignments: Number(source.assignments),
      compliant: Number(source.compliantAssignments),
      overdue: Number(source.overdueAssignments),
      rate: rateOf(source.complianceRate),
    };
  });

  return { matrix, courses, course, columns, rows, paging, tally };
}
