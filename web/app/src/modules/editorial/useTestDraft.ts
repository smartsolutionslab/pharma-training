import { useIsMutating, useQueryClient } from '@tanstack/vue-query';
import { computed, type MaybeRefOrGetter, toValue } from 'vue';
import { z } from 'zod';
import {
  getGetEditorialCourseVersionsIdQueryKey,
  getPostEditorialCourseVersionsIdTestDraftMutationKey,
  usePostEditorialCourseVersionsIdTestDraft,
} from '@/api/generated';

/** The draft's answer (EditorialTestDraftDto), checked: whatever the host sends is data, never trusted for its shape. */
const draftAnswer = z.object({
  draftedQuestionIds: z.array(z.string()),
  leftOutChapterIds: z.array(z.string()),
  truncatedChapterIds: z.array(z.string()),
});

/** What asking for a draft came to: the draft, an answer not of the draft's shape, or the error it failed with. */
export type TestDraftOutcome =
  | { status: 'drafted'; drafted: number; leftOutChapterIds: string[]; truncatedChapterIds: string[] }
  | { status: 'unreadable' }
  | { status: 'failed'; error: unknown };

/**
 * Asks the host for an AI draft of the version's test (POST …/test-draft, no body). Afterwards the version is stale
 * whatever the outcome: the draft replaced its unreviewed questions, or a refusal means the page shows something that
 * is no longer so, or a timeout may have been saved by the service after all. Whether a draft of the version runs is
 * read from the query client, not from this component: it outlives the page, so leaving it and coming back still shows
 * the draft running until it settles.
 */
export function useTestDraft(versionId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient();
  const drafting = usePostEditorialCourseVersionsIdTestDraft({
    mutation: {
      onSettled: (_data, _error, variables) =>
        queryClient.invalidateQueries({ queryKey: getGetEditorialCourseVersionsIdQueryKey(variables.id) }),
    },
  });

  async function draft(): Promise<TestDraftOutcome> {
    let answer: unknown;
    try {
      answer = await drafting.mutateAsync({ id: toValue(versionId) });
    } catch (error) {
      return { status: 'failed', error };
    }
    const parsed = draftAnswer.safeParse(answer);
    return parsed.success
      ? {
          status: 'drafted',
          drafted: parsed.data.draftedQuestionIds.length,
          leftOutChapterIds: parsed.data.leftOutChapterIds,
          truncatedChapterIds: parsed.data.truncatedChapterIds,
        }
      : { status: 'unreadable' };
  }

  const running = useIsMutating(() => {
    const id = toValue(versionId);
    return {
      mutationKey: getPostEditorialCourseVersionsIdTestDraftMutationKey(),
      predicate: (mutation) => (mutation.state.variables as { id?: unknown } | undefined)?.id === id,
    };
  });

  return { draft, isDrafting: computed(() => running.value > 0) };
}
