import { type MaybeRefOrGetter, toValue } from 'vue';
import { z } from 'zod';
import {
  getGetEditorialCourseVersionsIdQueryKey,
  getGetEditorialCourseVersionsQueryKey,
  usePostEditorialCourseVersionsIdNext,
} from '@/api/generated';
import { ApiError } from '@/api/baseApi';

/** What drafting the next version came to: the new draft's id, or a failure with a module's words where it sent some. */
export type DraftNextResult = { ok: true; id: string } | { ok: false; serverMessage?: string };

/**
 * Drafts the successor of a published version (POST /editorial/course-versions/{id}/next). Afterwards every cached
 * inbox page is stale, the new draft may be on any of them (a version's own key shares the prefix, but is left alone:
 * the published one has not changed); after a refusal the published version is stale instead: it may have been
 * retired or superseded meanwhile.
 */
export function useDraftNextVersion(versionId: MaybeRefOrGetter<string>) {
  const { mutateAsync, isPending } = usePostEditorialCourseVersionsIdNext({
    mutation: {
      onSuccess: (_id, _variables, _result, { client }) =>
        client.invalidateQueries({
          queryKey: getGetEditorialCourseVersionsQueryKey(),
          predicate: ({ queryKey }) => typeof queryKey[2] !== 'string',
        }),
      onError: (_error, { id }, _result, { client }) =>
        client.invalidateQueries({ queryKey: getGetEditorialCourseVersionsIdQueryKey(id) }),
    },
  });
  const draftNext = async (): Promise<DraftNextResult> => {
    try {
      return { ok: true, id: z.uuid().parse(await mutateAsync({ id: toValue(versionId) })) };
    } catch (error) {
      return error instanceof ApiError && error.serverMessage !== undefined
        ? { ok: false, serverMessage: error.serverMessage }
        : { ok: false };
    }
  };
  return { draftNext, isDrafting: isPending };
}
