import { useQueryClient } from '@tanstack/vue-query';
import { computed, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { z } from 'zod';
import { ApiError } from '@/api/baseApi';
import {
  type CatalogChapterKind,
  getGetLearningSessionsIdQueryKey,
  type LearningSessionDto,
  useGetCatalogCourseVersionsId,
  useGetLearningSessionsId,
  usePostLearningSessions,
} from '@/api/generated';

/** A chapter of the version as the player shows it: the catalog's content joined with the session's progress. */
export interface PlayerChapter {
  chapterId: string;
  position: number;
  title: string;
  kind: CatalogChapterKind;
  text: string | null;
  videoDurationSeconds: number | null;
  progressPercent: number;
  completed: boolean;
}

/** Why the page cannot show the course: each is a message of learning.player. */
export type PlayerFailure = 'notFound' | 'notReady' | 'noIdentity' | 'loadFailed';

const isId = (value: unknown): value is string => z.uuid().safeParse(value).success;

/** The words for a refusal, chosen by its code, never by what the host wrote. */
function failureOf(error: unknown): PlayerFailure {
  if (!(error instanceof ApiError)) {
    return 'loadFailed';
  }
  switch (error.code) {
    case 'catalog.course-version-not-found':
    case 'learning.session-not-found':
      return 'notFound';
    case 'learning.course-version-not-found':
      return 'notReady';
    case 'learning.identity-incomplete':
      return 'noIdentity';
    default:
      return error.status === 404 ? 'notFound' : 'loadFailed';
  }
}

/**
 * The chapter player's data (#44) at /learning/courses/:courseVersionId: the version's content from the catalog, the
 * learner's session on it, started or resumed (POST /learning/sessions, idempotent; a start that lost a race,
 * already-exists, is posted once more), and the chapter shown, kept in ?chapter=.
 */
export function useCoursePlayer() {
  const route = useRoute();
  const router = useRouter();
  const queryClient = useQueryClient();

  const versionId = computed(() => String(route.params.courseVersionId ?? ''));
  /** A version id that is no UUID names no version, and is never sent. */
  const validVersion = computed(() => isId(versionId.value));

  const catalog = useGetCatalogCourseVersionsId(versionId, { query: { enabled: validVersion } });

  // The start's answer is read from the mutation, whose data is the latest start's alone; a start that lost a race is
  // posted again, and the second post is the latest.
  const { mutateAsync: postStart, data: started } = usePostLearningSessions();
  const startError = ref<unknown>();

  async function startOnce(): Promise<LearningSessionDto> {
    const request = { data: { courseVersionId: versionId.value } };
    try {
      return await postStart(request);
    } catch (error) {
      if (error instanceof ApiError && error.failureCode === 'already-exists') {
        return await postStart(request);
      }
      throw error;
    }
  }

  /** Starts or resumes the session; its answer goes under the session's key, where the session is read from. */
  async function start() {
    if (!validVersion.value) {
      return;
    }
    startError.value = undefined;
    try {
      const answer = await startOnce();
      queryClient.setQueryData(getGetLearningSessionsIdQueryKey(answer.id), answer);
    } catch (error) {
      startError.value = error;
    }
  }

  watch(versionId, () => void start(), { immediate: true });

  const sessionId = computed(() => started.value?.id ?? '');
  const sessionQuery = useGetLearningSessionsId(sessionId, {
    query: { enabled: computed(() => started.value !== undefined), refetchOnWindowFocus: true },
  });
  const session = computed(() => sessionQuery.data.value ?? started.value);
  const version = computed(() => catalog.data.value);

  const failure = computed((): PlayerFailure | undefined => {
    if (!validVersion.value) {
      return 'notFound';
    }
    if (catalog.error.value !== null) {
      return failureOf(catalog.error.value);
    }
    if (startError.value !== undefined) {
      return failureOf(startError.value);
    }
    return undefined;
  });
  /** Whether Retry can change anything: an id that is no UUID stays one. */
  const retryable = computed(() => validVersion.value);
  const loading = computed(
    () => failure.value === undefined && (version.value === undefined || session.value === undefined),
  );

  /** Asks again for what failed: the catalog read again, the start posted again. */
  function retry() {
    if (catalog.error.value !== null) {
      void catalog.refetch();
    }
    if (startError.value !== undefined) {
      void start();
    }
  }

  const chapters = computed((): PlayerChapter[] => {
    const shown = version.value;
    if (shown === undefined) {
      return [];
    }
    const progress = new Map((session.value?.chapters ?? []).map((chapter) => [chapter.chapterId, chapter]));
    return [...shown.chapters]
      .sort((first, second) => Number(first.position) - Number(second.position))
      .map((chapter) => {
        const own = progress.get(chapter.chapterId);
        return {
          chapterId: chapter.chapterId,
          position: Number(chapter.position),
          title: chapter.title,
          kind: chapter.kind,
          text: chapter.text,
          videoDurationSeconds: chapter.videoDurationSeconds === null ? null : Number(chapter.videoDurationSeconds),
          progressPercent: Number(own?.progressPercent ?? 0),
          completed: own?.completed ?? false,
        };
      });
  });

  /** The chapter ?chapter= names, when it is in the course; else the first not done, else the first. */
  const current = computed((): PlayerChapter | undefined => {
    const requested = route.query.chapter;
    const named = isId(requested) ? chapters.value.find((chapter) => chapter.chapterId === requested) : undefined;
    return named ?? chapters.value.find((chapter) => !chapter.completed) ?? chapters.value[0];
  });

  // The chapter shown is kept in the address, so a reload or a shared link opens it again.
  watch(
    () => current.value?.chapterId,
    (chapterId) => {
      if (chapterId !== undefined && route.query.chapter !== chapterId) {
        void router.replace({ query: { ...route.query, chapter: chapterId } });
      }
    },
    { immediate: true },
  );

  const contentCompleted = computed(() => session.value?.status === 'ContentCompleted');

  return { version, session, sessionId, chapters, current, contentCompleted, failure, retryable, loading, retry };
}
