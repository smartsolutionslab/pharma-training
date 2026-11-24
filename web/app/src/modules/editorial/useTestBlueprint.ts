import { useQueryClient } from '@tanstack/vue-query';
import { type MaybeRefOrGetter, toValue } from 'vue';
import {
  type EditorialBlueprintRequest,
  getGetEditorialCourseVersionsIdQueryKey,
  usePutEditorialCourseVersionsIdBlueprint,
} from '@/api/generated';

/** What saving the blueprint came to: ok, or a failure carrying what the call rejected with (shown by errorText). */
export type BlueprintChange = { ok: true } | { ok: false; error: unknown };

/**
 * Saves the test's blueprint of the version. Afterwards the version is stale, also after a refusal: a 409 (NotADraft)
 * means the page shows something that is no longer so.
 */
export function useTestBlueprint(versionId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient();
  const saving = usePutEditorialCourseVersionsIdBlueprint({
    mutation: {
      onSettled: () =>
        queryClient.invalidateQueries({ queryKey: getGetEditorialCourseVersionsIdQueryKey(toValue(versionId)) }),
    },
  });

  async function save(request: EditorialBlueprintRequest): Promise<BlueprintChange> {
    try {
      await saving.mutateAsync({ id: toValue(versionId), data: request });
      return { ok: true };
    } catch (error) {
      return { ok: false, error };
    }
  }

  return { save, isSaving: saving.isPending };
}
