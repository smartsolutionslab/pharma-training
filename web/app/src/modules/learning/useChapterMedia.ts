import type { Query } from '@tanstack/vue-query';
import { computed, type MaybeRefOrGetter, toValue } from 'vue';
import { ApiError } from '@/api/baseApi';
import { type LearningMediaUrlDto, useGetLearningMediaChapterId } from '@/api/generated';

/** How often a file not ready yet is asked for again, and how far apart. */
const notReadyRetries = 5;
const notReadyDelay = 2_000;
/** A signed URL is read again this long before it expires, so a playing video never holds an expired one. */
const renewAhead = 60_000;
/** The shortest wait before a renewal, so a URL that is (nearly) expired already is not asked for in a loop. */
const shortestRenewal = 1_000;

export interface ChapterMediaOptions {
  /**
   * Whether the URL is read again a minute before it expires: a video streams for as long as it plays. A PDF is read
   * once, whole, and keeps the URL it was opened with.
   */
  renew: boolean;
}

/** The refusal that says the file is still being prepared (409 learning.media-not-ready). */
export const isMediaNotReady = (error: unknown) =>
  error instanceof ApiError && error.code === 'learning.media-not-ready';

/** Milliseconds until the URL the query holds is to be read again; none while it holds none. */
function untilRenewal(query: Query<LearningMediaUrlDto, unknown, LearningMediaUrlDto, readonly unknown[]>) {
  const expiresAt = query.state.data?.expiresAt;
  if (expiresAt === undefined) {
    return 0;
  }
  return Math.max(shortestRenewal, Date.parse(expiresAt) - renewAhead - Date.now());
}

/**
 * The signed read URL of a chapter's file (GET /learning/media/{chapterId}, #44). A file not ready yet (409
 * learning.media-not-ready) is asked for 5 more times 2 s apart before it counts as failed; any other refusal fails at
 * once. With `renew`, the URL stays fresh until a minute before it expires and is read again then.
 */
export function useChapterMedia(chapterId: MaybeRefOrGetter<string>, { renew }: ChapterMediaOptions) {
  const query = useGetLearningMediaChapterId(() => toValue(chapterId), {
    query: {
      retry: (failures, error) => isMediaNotReady(error) && failures < notReadyRetries,
      retryDelay: notReadyDelay,
      staleTime: renew ? untilRenewal : Number.POSITIVE_INFINITY,
      refetchInterval: renew ? (current) => (current.state.data === undefined ? false : untilRenewal(current)) : false,
      refetchIntervalInBackground: renew,
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
