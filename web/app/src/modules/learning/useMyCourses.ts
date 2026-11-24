import { computed, type Ref } from 'vue';
import { type ComplianceMyAssignmentDto, type MyPharmacyDto, useGetComplianceMeAssignments } from '@/api/generated';

/** A learner has a handful of active assignments: one page of 50 is the rule. */
export const myCoursesPageSize = 50;

const none: ComplianceMyAssignmentDto[] = [];

/**
 * The caller's active assignments in the current pharmacy, at a page, in the host's order (overdue first, then by due
 * date, then by title): the query, its items and its page. Nothing is asked before the pharmacy is known.
 */
export function useMyCourses(current: Ref<MyPharmacyDto | undefined>, page: Ref<number>) {
  const assignments = useGetComplianceMeAssignments(
    () => ({ pharmacyId: current.value?.id, page: page.value, pageSize: myCoursesPageSize }),
    {
      query: {
        enabled: computed(() => current.value !== undefined),
        // The page shown is kept while the next one loads, but never another pharmacy's courses (#341's review).
        placeholderData: (previous, previousQuery) =>
          previousQuery?.queryKey.some(
            (part) =>
              typeof part === 'object' &&
              part !== null &&
              'pharmacyId' in part &&
              part.pharmacyId === current.value?.id,
          )
            ? previous
            : undefined,
      },
    },
  );

  const items = computed(() => assignments.data.value?.items ?? none);
  const paging = computed(() => assignments.data.value);

  return { assignments, items, paging };
}
