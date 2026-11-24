import { useQueryClient } from '@tanstack/vue-query';
import { type MaybeRefOrGetter, toValue } from 'vue';
import {
  type EditorialCourseVersionDto,
  getGetEditorialCourseVersionsIdQueryKey,
  usePutEditorialCourseVersionsIdChaptersOrder,
} from '@/api/generated';
import { errorText } from '@/i18n/errorText';
import { notify } from '@/lib/notify';

/** The version with its chapters in the order given, numbered from 1; a chapter the order does not name stays out. */
function inOrder(version: EditorialCourseVersionDto, order: string[]): EditorialCourseVersionDto {
  const chapters = order.flatMap((id) => version.chapters.filter((each) => each.id === id));
  return { ...version, chapters: chapters.map((chapter, index) => ({ ...chapter, position: index + 1 })) };
}

/**
 * Puts the version's chapters in a new order (PUT .../chapters/order, every chapter's id). It is optimistic: the new
 * order is in the version's cache before the host answers, so the list moves at once. Orders go out one at a time: a
 * move made while one is on its way waits for it, and only the latest of such moves is sent, so the host cannot save
 * an older order over a newer one. A refusal puts back the last order the host accepted, drops a move still waiting
 * and says so, by the refusal's code (errorText). Once nothing more is to be sent, the version is read
 * again. While an order is not yet settled, leaving or reloading the page asks first.
 */
export function useReorderChapters(versionId: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient();
  const { mutateAsync } = usePutEditorialCourseVersionsIdChaptersOrder();

  /** The orders being sent, until the last has settled. */
  let sending: Promise<void> | undefined;
  /** The latest order made while another was on its way, sent once it has settled. */
  let waiting: string[] | undefined;

  /**
   * Until every order has settled, leaving or reloading the page asks the browser's confirmation first: the list already
   * shows the order, so leaving would drop it without a word. It stays while the list is gone (another page of the app),
   * since the order is still on its way, and goes once nothing more is to be sent.
   */
  function askBeforeLeaving(event: BeforeUnloadEvent) {
    event.preventDefault();
  }

  /** Sends the order, then any waiting one; on a refusal restores `accepted`, the version as the host last took it. */
  async function send(id: string, order: string[], accepted: EditorialCourseVersionDto | undefined): Promise<void> {
    const queryKey = getGetEditorialCourseVersionsIdQueryKey(id);
    let next: string[] | undefined = order;
    let saved = accepted;
    try {
      while (next !== undefined) {
        const current: string[] = next;
        try {
          await mutateAsync({ id, data: { chapters: current } });
        } catch (error) {
          waiting = undefined;
          if (saved !== undefined) {
            queryClient.setQueryData(queryKey, saved);
          }
          notify.failure(errorText(error, 'editorial.chapters.reorderFailed'));
          return;
        }
        saved = saved === undefined ? undefined : inOrder(saved, current);
        next = waiting;
        waiting = undefined;
      }
    } finally {
      sending = undefined;
      window.removeEventListener('beforeunload', askBeforeLeaving);
      await queryClient.invalidateQueries({ queryKey });
    }
  }

  const reorder = async (order: string[]): Promise<void> => {
    const id = toValue(versionId);
    const queryKey = getGetEditorialCourseVersionsIdQueryKey(id);
    // A read on its way would otherwise put the old order back over the new one.
    await queryClient.cancelQueries({ queryKey });
    const previous = queryClient.getQueryData<EditorialCourseVersionDto>(queryKey);
    if (previous !== undefined) {
      queryClient.setQueryData(queryKey, inOrder(previous, order));
    }
    if (sending !== undefined) {
      waiting = order;
      return sending;
    }
    window.addEventListener('beforeunload', askBeforeLeaving);
    sending = send(id, order, previous);
    return sending;
  };

  return { reorder };
}
