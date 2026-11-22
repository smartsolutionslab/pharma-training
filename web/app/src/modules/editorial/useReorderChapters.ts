import { useQueryClient } from '@tanstack/vue-query';
import { type MaybeRefOrGetter, toValue } from 'vue';
import {
  type EditorialCourseVersionDto,
  getGetEditorialCourseVersionsIdQueryKey,
  usePutEditorialCourseVersionsIdChaptersOrder,
} from '@/api/generated';
import { ApiError } from '@/api/baseApi';
import { t } from '@/i18n';
import { notify } from '@/lib/notify';

/** The version with its chapters in the order given, numbered from 1; a chapter the order does not name stays out. */
function inOrder(version: EditorialCourseVersionDto, order: string[]): EditorialCourseVersionDto {
  const chapters = order.flatMap((id) => version.chapters.filter((each) => each.id === id));
  return { ...version, chapters: chapters.map((chapter, index) => ({ ...chapter, position: index + 1 })) };
}

/**
 * Puts the version's chapters in a new order (PUT .../chapters/order, every chapter's id). It is optimistic: the new
 * order is in the version's cache before the host answers, so the list moves at once. A refusal puts the previous
 * order back and says so, in the host's words where a module sent some; either way the version is read again.
 */
export function useReorderChapters(versionId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient();
  const { mutateAsync } = usePutEditorialCourseVersionsIdChaptersOrder();

  const reorder = async (order: string[]): Promise<void> => {
    const id = toValue(versionId);
    const queryKey = getGetEditorialCourseVersionsIdQueryKey(id);
    // A read on its way would otherwise put the old order back over the new one.
    await queryClient.cancelQueries({ queryKey });
    const previous = queryClient.getQueryData<EditorialCourseVersionDto>(queryKey);
    if (previous !== undefined) {
      queryClient.setQueryData(queryKey, inOrder(previous, order));
    }
    try {
      await mutateAsync({ id, data: { chapters: order } });
    } catch (error) {
      if (previous !== undefined) {
        queryClient.setQueryData(queryKey, previous);
      }
      const serverMessage = error instanceof ApiError ? error.serverMessage : undefined;
      notify.failure(serverMessage ?? t('editorial.chapters.reorderFailed'));
    } finally {
      await queryClient.invalidateQueries({ queryKey });
    }
  };

  return { reorder };
}
