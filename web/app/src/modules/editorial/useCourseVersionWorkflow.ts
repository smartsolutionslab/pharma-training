import type { QueryClient } from '@tanstack/vue-query';
import { computed, type MaybeRefOrGetter, toValue } from 'vue';
import {
  getGetEditorialCourseVersionsIdQueryKey,
  getGetEditorialCourseVersionsIdReturnsQueryKey,
  getGetEditorialCourseVersionsQueryKey,
  usePostEditorialCourseVersionsIdPublish,
  usePostEditorialCourseVersionsIdRetire,
  usePostEditorialCourseVersionsIdReturn,
  usePostEditorialCourseVersionsIdSubmit,
} from '@/api/generated';
import { ApiError } from '@/api/baseApi';
import { isKnownRefusal } from './workflowErrors';

/** What a step of the workflow came to: taken, or what the client threw. */
export type WorkflowResult = { ok: true } | { ok: false; error: unknown };

/** A step taken changes the version, its returns and the inbox: everything below ['editorial', 'course-versions']. */
const stepTaken = (_data: unknown, _variables: unknown, _result: unknown, { client }: { client: QueryClient }) =>
  client.invalidateQueries({ queryKey: getGetEditorialCourseVersionsQueryKey() });

/**
 * A refusal may mean the version changed meanwhile: a conflict (409), a request the version no longer allows (422), or
 * a module's refusal the page knows of a version that is still there (four eyes, an incomplete identity). Then the
 * version and its returns are read again (another tab may have returned it meanwhile, #376), and the step answers once
 * they have been, so the page shows why. Any
 * other failure (an edge's 5xx, a policy's 403, a 404, the network) changed nothing and the host may be out of reach:
 * the version is marked stale, but the step answers at once, not after the read's retries.
 */
function mayHaveChanged(error: unknown): boolean {
  if (!(error instanceof ApiError)) {
    return false;
  }
  return error.status === 409 || error.status === 422 || (error.status !== 404 && isKnownRefusal(error));
}

const stepRefused = (error: unknown, { id }: { id: string }, _result: unknown, { client }: { client: QueryClient }) => {
  const version = client.invalidateQueries({ queryKey: getGetEditorialCourseVersionsIdQueryKey(id), exact: true });
  if (!mayHaveChanged(error)) {
    return undefined;
  }
  const returns = client.invalidateQueries({ queryKey: getGetEditorialCourseVersionsIdReturnsQueryKey(id) });
  return Promise.all([version, returns]);
};

const mutation = { mutation: { onSuccess: stepTaken, onError: stepRefused } };

/** What the page does when a step is taken, before the cache is marked stale. */
export interface WorkflowHooks {
  /**
   * A return was taken: runs before the version and its returns are read again, and the step waits for it. The page
   * turns its return history to the first page here, so that page, where the new return is, is the one read again.
   */
  onReturned?: () => unknown;
}

/**
 * A course version's workflow (#26): submit, publish, return with a reason and retire, through
 * POST /editorial/course-versions/{id}/<step>. Each answers once the cache has been marked stale and the version read
 * again where it is shown.
 */
export function useCourseVersionWorkflow(versionId: MaybeRefOrGetter<string>, hooks: WorkflowHooks = {}) {
  const submitting = usePostEditorialCourseVersionsIdSubmit(mutation);
  const publishing = usePostEditorialCourseVersionsIdPublish(mutation);
  const returning = usePostEditorialCourseVersionsIdReturn({
    mutation: {
      onSuccess: async (...taken: Parameters<typeof stepTaken>) => {
        await hooks.onReturned?.();
        return stepTaken(...taken);
      },
      onError: stepRefused,
    },
  });
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
