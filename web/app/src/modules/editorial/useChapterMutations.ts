import { useQueryClient } from '@tanstack/vue-query';
import { computed, type MaybeRefOrGetter, toValue } from 'vue';
import {
  type EditorialChapterRequest,
  getGetEditorialCourseVersionsIdQueryKey,
  useDeleteEditorialCourseVersionsIdChaptersChapterId,
  usePostEditorialCourseVersionsIdChapters,
  usePutEditorialCourseVersionsIdChaptersChapterId,
} from '@/api/generated';
import { ApiError } from '@/api/baseApi';

/** What changing a chapter came to: ok, or a failure with the host's words where a module sent some. */
export type ChapterChange = { ok: true } | { ok: false; serverMessage?: string };

/**
 * Adds, replaces and deletes a chapter of the version. Afterwards the version is stale, also after a refusal: a 409
 * (NotADraft, a lost race) means the page shows something that is no longer so.
 */
export function useChapterMutations(versionId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient();
  const mutation = {
    onSettled: () =>
      queryClient.invalidateQueries({ queryKey: getGetEditorialCourseVersionsIdQueryKey(toValue(versionId)) }),
  };
  const adding = usePostEditorialCourseVersionsIdChapters({ mutation });
  const replacing = usePutEditorialCourseVersionsIdChaptersChapterId({ mutation });
  const removing = useDeleteEditorialCourseVersionsIdChaptersChapterId({ mutation });

  const settle = async (change: Promise<unknown>): Promise<ChapterChange> => {
    try {
      await change;
      return { ok: true };
    } catch (error) {
      return error instanceof ApiError && error.serverMessage !== undefined
        ? { ok: false, serverMessage: error.serverMessage }
        : { ok: false };
    }
  };

  return {
    add: (request: EditorialChapterRequest) => settle(adding.mutateAsync({ id: toValue(versionId), data: request })),
    replace: (chapterId: string, request: EditorialChapterRequest) =>
      settle(replacing.mutateAsync({ id: toValue(versionId), chapterId, data: request })),
    remove: (chapterId: string) => settle(removing.mutateAsync({ id: toValue(versionId), chapterId })),
    isSaving: computed(() => adding.isPending.value || replacing.isPending.value),
  };
}
