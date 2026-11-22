import { useQueryClient } from '@tanstack/vue-query';
import { type MaybeRefOrGetter, toValue } from 'vue';
import { z } from 'zod';
import { getGetEditorialCourseVersionsIdQueryKey, usePostEditorialCourseVersionsIdTestDraft } from '@/api/generated';

/** The draft's answer (EditorialTestDraftDto), checked: whatever the host sends is data, never trusted for its shape. */
const draftAnswer = z.object({
  draftedQuestionIds: z.array(z.string()),
  leftOutChapterIds: z.array(z.string()),
});

/** What asking for a draft came to: the draft, an answer not of the draft's shape, or the error it failed with. */
export type TestDraftOutcome =
  | { status: 'drafted'; drafted: number; leftOutChapterIds: string[] }
  | { status: 'unreadable' }
  | { status: 'failed'; error: unknown };

/**
 * Asks the host for an AI draft of the version's test (POST …/test-draft, no body). Afterwards the version is stale
 * whatever the outcome: the draft replaced its unreviewed questions, or a refusal means the page shows something that
 * is no longer so, or a timeout may have been saved by the service after all.
 */
export function useTestDraft(versionId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient();
  const drafting = usePostEditorialCourseVersionsIdTestDraft({
    mutation: {
      onSettled: () =>
        queryClient.invalidateQueries({ queryKey: getGetEditorialCourseVersionsIdQueryKey(toValue(versionId)) }),
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
        }
      : { status: 'unreadable' };
  }

  return { draft, isDrafting: drafting.isPending };
}
