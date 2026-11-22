import { keepPreviousData } from '@tanstack/vue-query';
import { computed, type MaybeRefOrGetter, toValue, unref } from 'vue';
import { useGetEditorialCourseVersions } from '@/api/generated';
import type { InboxQuery } from './inboxQuery';

/** The inbox's page size: the host's default. */
export const inboxPageSize = 20;

/**
 * The course versions of a status, a page at a time, and the page they are of: undefined until the host answers. Asks
 * again whenever the status or the page changes; until the host answers, the page shown before stays (so the pager
 * does too, and keeps the focus), and isPending is true.
 */
export function useCourseVersions(query: MaybeRefOrGetter<InboxQuery>) {
  const params = computed(() => ({ ...toValue(query), pageSize: inboxPageSize }));
  const { data, isPending, isPlaceholderData, isError } = useGetEditorialCourseVersions(params, {
    query: { placeholderData: keepPreviousData },
  });
  return {
    versions: computed(() => data.value?.items),
    page: data,
    // unref: a stand-in for the hook (the axe check's) may answer without isPlaceholderData.
    isPending: computed(() => isPending.value || unref(isPlaceholderData) === true),
    isError,
  };
}
