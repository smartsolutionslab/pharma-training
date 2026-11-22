import { getGetEditorialCourseVersionsQueryKey, usePostEditorialCourses } from '@/api/generated';
import { ApiError } from '@/api/baseApi';

/** What starting a course came to: its first version's id, or a failure with the host's words where it sent some. */
export type CreateCourseResult = { ok: true; id: string } | { ok: false; serverMessage?: string };

/**
 * Starts a course (POST /editorial/courses); resolves to its first version's id. Every cached inbox page is then
 * stale, whatever its status and page: the new draft may be on any of them.
 */
export function useCreateCourse() {
  const { mutateAsync, isPending } = usePostEditorialCourses({
    mutation: {
      onSuccess: (_id, _variables, _result, { client }) =>
        client.invalidateQueries({ queryKey: getGetEditorialCourseVersionsQueryKey() }),
    },
  });
  const create = async (title: string): Promise<CreateCourseResult> => {
    try {
      return { ok: true, id: await mutateAsync({ data: { title } }) };
    } catch (error) {
      return error instanceof ApiError && error.serverMessage !== undefined
        ? { ok: false, serverMessage: error.serverMessage }
        : { ok: false };
    }
  };
  return { create, isSaving: isPending };
}
