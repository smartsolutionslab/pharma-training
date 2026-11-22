import { useQueryClient } from '@tanstack/vue-query';
import { computed, type MaybeRefOrGetter, toValue } from 'vue';
import {
  type EditorialQuestionRequest,
  getGetEditorialCourseVersionsIdQueryKey,
  useDeleteEditorialCourseVersionsIdQuestionsQuestionId,
  usePostEditorialCourseVersionsIdQuestions,
  usePostEditorialCourseVersionsIdQuestionsQuestionIdApprove,
  usePutEditorialCourseVersionsIdQuestionsQuestionId,
} from '@/api/generated';
import { ApiError } from '@/api/baseApi';

/** What changing a question came to: ok, or a failure with the host's words where a module sent some. */
export type QuestionChange = { ok: true } | { ok: false; serverMessage?: string };

/**
 * Adds, replaces, deletes and approves a question of the version's test. Afterwards the version is stale, also after a
 * refusal: a 409 (NotADraft) means the page shows something that is no longer so.
 */
export function useQuestionMutations(versionId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient();
  const mutation = {
    onSettled: () =>
      queryClient.invalidateQueries({ queryKey: getGetEditorialCourseVersionsIdQueryKey(toValue(versionId)) }),
  };
  const adding = usePostEditorialCourseVersionsIdQuestions({ mutation });
  const replacing = usePutEditorialCourseVersionsIdQuestionsQuestionId({ mutation });
  const removing = useDeleteEditorialCourseVersionsIdQuestionsQuestionId({ mutation });
  const approving = usePostEditorialCourseVersionsIdQuestionsQuestionIdApprove({ mutation });

  const settle = async (change: Promise<unknown>): Promise<QuestionChange> => {
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
    add: (request: EditorialQuestionRequest) => settle(adding.mutateAsync({ id: toValue(versionId), data: request })),
    replace: (questionId: string, request: EditorialQuestionRequest) =>
      settle(replacing.mutateAsync({ id: toValue(versionId), questionId, data: request })),
    remove: (questionId: string) => settle(removing.mutateAsync({ id: toValue(versionId), questionId })),
    /** Approves the question: in a draft or in review; the host checks its options (422) and a second approval (409). */
    approve: (questionId: string) => settle(approving.mutateAsync({ id: toValue(versionId), questionId })),
    isSaving: computed(() => adding.isPending.value || replacing.isPending.value),
    isRemoving: removing.isPending,
    isApproving: approving.isPending,
  };
}
