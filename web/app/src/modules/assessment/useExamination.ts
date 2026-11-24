import { useQueryClient } from '@tanstack/vue-query';
import { computed, type MaybeRefOrGetter, onBeforeUnmount, toValue, watch } from 'vue';
import { z } from 'zod';
import { ApiError } from '@/api/baseApi';
import { useGetAssessmentExaminationsBySessionSessionId, useGetCatalogCourseVersionsId } from '@/api/generated';
import { type AssessmentAction, type AssessmentFailure, assessmentFailure } from './assessmentErrors';

const isId = (value: unknown): value is string => z.uuid().safeParse(value).success;

/** How often the page asks again while the examination is not there yet: it opens seconds after ContentCompletedV1. */
const pollEvery = 3_000;

const notFoundCode = 'assessment.examination-not-found';
const isNotFound = (error: unknown) => error instanceof ApiError && error.code === notFoundCode;

/**
 * The learner's examination (#48), found by Learning's session id in the address (ADR 0010/0012), and the course
 * version it is on from the catalog, for the course's title and the chapters' titles. A session id that is no UUID names
 * no examination and is never sent. While there is no examination yet (404 not-found), it asks again every 3 s.
 */
export function useExamination(session: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient();

  const sessionId = computed(() => toValue(session));
  const validSession = computed(() => isId(sessionId.value));

  // A 404 is not asked again at once: the page asks every 3 s by itself. Anything else as the client's default says.
  const retryDefault = queryClient.getDefaultOptions().queries?.retry ?? 3;
  function retry(failureCount: number, error: unknown): boolean {
    if (error instanceof ApiError && error.status === 404) {
      return false;
    }
    if (typeof retryDefault === 'function') {
      return retryDefault(failureCount, error instanceof Error ? error : new Error(String(error)));
    }
    return typeof retryDefault === 'number' ? failureCount < retryDefault : retryDefault;
  }

  const query = useGetAssessmentExaminationsBySessionSessionId(sessionId, {
    query: { enabled: validSession, retry },
  });
  const examination = computed(() => query.data.value);

  const versionId = computed(() => examination.value?.courseVersionId ?? '');
  const catalog = useGetCatalogCourseVersionsId(versionId, {
    query: { enabled: computed(() => versionId.value !== '') },
  });
  const version = computed(() => catalog.data.value);

  /** Whether the examination is not there yet: the page says it is not unlocked and asks again. */
  const notYetThere = computed(() => isNotFound(query.error.value));

  let poll: ReturnType<typeof setInterval> | undefined;
  function stopPolling() {
    if (poll !== undefined) {
      clearInterval(poll);
      poll = undefined;
    }
  }
  watch(
    notYetThere,
    (waiting) => {
      if (waiting && poll === undefined) {
        poll = setInterval(() => void query.refetch(), pollEvery);
      } else if (!waiting) {
        stopPolling();
      }
    },
    { immediate: true },
  );
  onBeforeUnmount(stopPolling);

  /**
   * Why the page cannot show the examination, in the words of the page's action (the test's read or the result's):
   * none while it loads or is shown, or while it is not there yet.
   */
  const failureOf = (action: Extract<AssessmentAction, 'read' | 'result'>) =>
    computed((): AssessmentFailure | undefined => {
      if (!validSession.value) {
        return { key: 'assessment.test.notFound' };
      }
      const error = query.error.value;
      if (error === null || isNotFound(error)) {
        return undefined;
      }
      return assessmentFailure(action, error);
    });
  /** Whether Retry can change anything: an id that is no UUID stays one, an incomplete account stays one. */
  const retryable = computed(
    () =>
      validSession.value &&
      !(query.error.value instanceof ApiError && query.error.value.code === 'assessment.identity-incomplete'),
  );
  const loading = computed(() => validSession.value && query.error.value === null && examination.value === undefined);

  return {
    sessionId,
    examination,
    version,
    catalogFailed: computed(() => catalog.error.value !== null),
    retryCatalog: () => void catalog.refetch(),
    notYetThere,
    failureOf,
    retryable,
    loading,
    refetch: () => query.refetch(),
  };
}
