import type { QueryClient } from '@tanstack/vue-query';
import { computed, type MaybeRefOrGetter, toValue } from 'vue';
import {
  getGetEditorialCourseVersionsIdQueryKey,
  getGetEditorialCourseVersionsQueryKey,
  usePostEditorialCourseVersionsIdPublish,
  usePostEditorialCourseVersionsIdRetire,
  usePostEditorialCourseVersionsIdReturn,
  usePostEditorialCourseVersionsIdSubmit,
} from '@/api/generated';
import { ApiError } from '@/api/baseApi';

/** What a step of the workflow came to: taken, or what the client threw. */
export type WorkflowResult = { ok: true } | { ok: false; error: unknown };

/** A step taken changes the version, its returns and the inbox: everything below ['editorial', 'course-versions']. */
const stepTaken = (_data: unknown, _variables: unknown, _result: unknown, { client }: { client: QueryClient }) =>
  client.invalidateQueries({ queryKey: getGetEditorialCourseVersionsQueryKey() });

/**
 * A refused step may mean the version changed meanwhile: the version alone (not its returns) is read again, and the
 * step answers once it has been. A step that never reached the host changed nothing, and the host is likely out of
 * reach: the version is marked stale, but the step answers at once, not after the read's retries.
 */
const stepRefused = (error: unknown, { id }: { id: string }, _result: unknown, { client }: { client: QueryClient }) => {
  const read = client.invalidateQueries({ queryKey: getGetEditorialCourseVersionsIdQueryKey(id), exact: true });
  return error instanceof ApiError ? read : undefined;
};

const mutation = { mutation: { onSuccess: stepTaken, onError: stepRefused } };

/**
 * A course version's workflow (#26): submit, publish, return with a reason and retire, through
 * POST /editorial/course-versions/{id}/<step>. Each answers once the cache has been marked stale and the version read
 * again where it is shown.
 */
export function useCourseVersionWorkflow(versionId: MaybeRefOrGetter<string>) {
  const submitting = usePostEditorialCourseVersionsIdSubmit(mutation);
  const publishing = usePostEditorialCourseVersionsIdPublish(mutation);
  const returning = usePostEditorialCourseVersionsIdReturn(mutation);
  const retiring = usePostEditorialCourseVersionsIdRetire(mutation);

  async function taken(step: () => Promise<unknown>): Promise<WorkflowResult> {
    try {
      await step();
      return { ok: true };
    } catch (error) {
      return { ok: false, error };
    }
  }

  return {
    submit: () => taken(() => submitting.mutateAsync({ id: toValue(versionId) })),
    publish: () => taken(() => publishing.mutateAsync({ id: toValue(versionId) })),
    returnToDraft: (reason: string) => taken(() => returning.mutateAsync({ id: toValue(versionId), data: { reason } })),
    retire: () => taken(() => retiring.mutateAsync({ id: toValue(versionId) })),
    isPending: computed(
      () =>
        submitting.isPending.value ||
        publishing.isPending.value ||
        returning.isPending.value ||
        retiring.isPending.value,
    ),
  };
}
