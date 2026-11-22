import { computed, type MaybeRefOrGetter, toValue } from 'vue';
import { useGetEditorialCourseVersions } from '@/api/generated';
import type { InboxQuery } from './inboxQuery';

/** The inbox's page size: the host's default. */
export const inboxPageSize = 20;

/**
 * The course versions of a status, a page at a time, and the page they are of: undefined until the host answers. Asks
 * again whenever the status or the page changes.
 */
export function useCourseVersions(query: MaybeRefOrGetter<InboxQuery>) {
  const params = computed(() => ({ ...toValue(query), pageSize: inboxPageSize }));
  const { data, isPending, isError } = useGetEditorialCourseVersions(params);
  return { versions: computed(() => data.value?.items), page: data, isPending, isError };
}
