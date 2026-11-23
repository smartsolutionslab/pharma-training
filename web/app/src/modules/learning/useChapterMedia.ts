import { computed, type MaybeRefOrGetter, toValue } from 'vue';
import { ApiError } from '@/api/baseApi';
import { useGetLearningMediaChapterId } from '@/api/generated';

/** How often a file not ready yet is asked for again, and how far apart. */
const notReadyRetries = 5;
const notReadyDelay = 2_000;

/** The refusal that says the file is still being prepared (409 learning.media-not-ready). */
export const isMediaNotReady = (error: unknown) =>
  error instanceof ApiError && error.code === 'learning.media-not-ready';

/**
 * The signed read URL of a chapter's file (GET /learning/media/{chapterId}, #44). A file not ready yet (409
 * learning.media-not-ready) is asked for 5 more times 2 s apart before it counts as failed; any other refusal fails at
 * once.
 *
 * The URL is kept until the file fails to load with it, and the chapter then reads it again (`reload`): a URL lives
 * 15 minutes, and storage refuses it once it has expired. It is never renewed by the browser's clock, which may run
 * ahead of or behind the server's that signed `expiresAt`; a clock running ahead would read it again every second.
 */
export function useChapterMedia(chapterId: MaybeRefOrGetter<string>) {
  const query = useGetLearningMediaChapterId(() => toValue(chapterId), {
    query: {
      retry: (failures, error) => isMediaNotReady(error) && failures < notReadyRetries,
      retryDelay: notReadyDelay,
      staleTime: Number.POSITIVE_INFINITY,
    },
  });

  const url = computed(() => query.data.value?.url);
  /** Why the file cannot be shown, once the asking is over: still not ready, or anything else. */
  const failure = computed((): 'notReady' | 'failed' | undefined => {
    if (!query.isError.value) {
      return undefined;
    }
    return isMediaNotReady(query.error.value) ? 'notReady' : 'failed';
  });

  /** Reads the URL again; resolves once it has been answered. */
  async function reload() {
    await query.refetch();
  }

  return { url, failure, reload };
}
