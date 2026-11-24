import { useQueryClient } from '@tanstack/vue-query';
import { computed, type MaybeRefOrGetter, toValue } from 'vue';
import { z } from 'zod';
import { ApiError } from '@/api/baseApi';
import {
  type AssessmentExaminationDto,
  useGetAssessmentExaminationsBySessionSessionId,
  useGetCatalogCourseVersionsId,
} from '@/api/generated';
import { type AssessmentAction, type AssessmentFailure, assessmentFailure } from './assessmentErrors';

const isId = (value: unknown): value is string => z.uuid().safeParse(value).success;

/**
 * How often the page asks again while the examination is not there yet or still locked: it opens seconds after
 * ContentCompletedV1.
 */
const pollEvery = 3_000;

const notFoundCode = 'assessment.examination-not-found';
const isNotFound = (error: unknown) => error instanceof ApiError && error.code === notFoundCode;

/**
 * The learner's examination (#48), found by Learning's session id in the address (ADR 0010/0012), and the course
 * version it is on from the catalog, for the course's title and the chapters' titles. A session id that is no UUID names
 * no examination and is never sent. While there is no examination yet (404 not-found), or it is still Locked, it asks
 * again every 3 s, for as long as a page reads it.
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

  // Asked again by the query itself, so leaving the page stops it.
  function refetchInterval(read: { state: { data?: AssessmentExaminationDto; error: unknown } }): number | false {
    const waiting = read.state.data === undefined ? isNotFound(read.state.error) : read.state.data.status === 'Locked';
    return waiting ? pollEvery : false;
  }

  const query = useGetAssessmentExaminationsBySessionSessionId(sessionId, {
    query: { enabled: validSession, retry, refetchInterval },
  });
  const examination = computed(() => query.data.value);

  const versionId = computed(() => examination.value?.courseVersionId ?? '');
  const catalog = useGetCatalogCourseVersionsId(versionId, {
    query: { enabled: computed(() => versionId.value !== '') },
  });
  const version = computed(() => catalog.data.value);

  /** Whether the examination is not there yet: the page says it is not unlocked and asks again. */
  const notYetThere = computed(() => examination.value === undefined && isNotFound(query.error.value));

  /**
   * Why the page cannot show the examination, in the words of the page's action (the test's read or the result's):
   * none while it loads or is not there yet, and none while it is shown: a read again that fails keeps what is shown.
   */
  const failureOf = (action: Extract<AssessmentAction, 'read' | 'result'>) =>
    computed((): AssessmentFailure | undefined => {
      if (!validSession.value) {
        return { key: 'assessment.test.notFound' };
      }
      const error = query.error.value;
      if (error === null || examination.value !== undefined || isNotFound(error)) {
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
