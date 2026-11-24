import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render } from '@testing-library/vue';
import { afterEach, expect, test, vi } from 'vitest';
import { defineComponent, ref } from 'vue';
import { getGetEditorialCourseVersionsIdQueryKey } from '@/api/generated';
import { versionId } from './courseTestFixtures';
import { useTestDraft } from './useTestDraft';

// The AI draft of a version's test, through the generated client (#25). Afterwards the version it was asked for is
// stale, also when the page's version changed while it ran (the route's id changing mid-flight, review round 2): the
// id is taken when the draft is asked for.
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);
afterEach(() => host.mockReset());
afterEach(cleanup);

const otherVersionId = '0198c1a0-0000-7000-8000-0000000000c2';
const versionKey = getGetEditorialCourseVersionsIdQueryKey(versionId);
const otherVersionKey = getGetEditorialCourseVersionsIdQueryKey(otherVersionId);

test('the draft marks stale the version it was asked for, though the version changed while it ran', async () => {
  let answer: (response: Response) => void = () => undefined;
  host.mockImplementation(
    () =>
      new Promise<Response>((resolve) => {
        answer = resolve;
      }),
  );
  const id = ref(versionId);
  const used: { drafting?: ReturnType<typeof useTestDraft> } = {};
  const Host = defineComponent({
    setup() {
      used.drafting = useTestDraft(id);
      return () => null;
    },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  queryClient.setQueryData(versionKey, { id: versionId, questions: [] });
  queryClient.setQueryData(otherVersionKey, { id: otherVersionId, questions: [] });
  render(Host, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } });
  if (used.drafting === undefined) {
    throw new Error('The component did not set up.');
  }

  const settled = used.drafting.draft();
  await vi.waitFor(() => expect(host).toHaveBeenCalledOnce());
  expect(String(host.mock.calls[0]?.[0])).toBe(`/api/editorial/course-versions/${versionId}/test-draft`);
  id.value = otherVersionId;
  answer(Response.json({ draftedQuestionIds: [], leftOutChapterIds: [] }));
  await settled;

  await vi.waitFor(() => expect(queryClient.getQueryState(versionKey)?.isInvalidated).toBe(true));
  expect(queryClient.getQueryState(otherVersionKey)?.isInvalidated).toBe(false);
});

/** Renders a component that drafts the version, and hands back its draft. */
function drafter() {
  const used: { drafting?: ReturnType<typeof useTestDraft> } = {};
  const Host = defineComponent({
    setup() {
      used.drafting = useTestDraft(versionId);
      return () => null;
    },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(Host, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } });
  if (used.drafting === undefined) {
    throw new Error('The component did not set up.');
  }
  return used.drafting;
}

test('the draft passes on the chapters it left out and the chapters it cut (#301)', async () => {
  const leftOut = '0198c1a0-0000-7000-8000-0000000000e6';
  const cut = '0198c1a0-0000-7000-8000-0000000000e5';
  host.mockResolvedValue(
    Response.json({
      draftedQuestionIds: ['0198c1a0-0000-7000-8000-0000000000a7'],
      leftOutChapterIds: [leftOut],
      truncatedChapterIds: [cut],
    }),
  );

  const outcome = await drafter().draft();

  expect(outcome).toEqual({ status: 'drafted', drafted: 1, leftOutChapterIds: [leftOut], truncatedChapterIds: [cut] });
});

test('an answer whose cut chapters are not ids is not the draft’s shape', async () => {
  host.mockResolvedValue(
    Response.json({ draftedQuestionIds: [], leftOutChapterIds: [], truncatedChapterIds: [42, { id: 'x' }] }),
  );

  const outcome = await drafter().draft();

  expect(outcome).toEqual({ status: 'unreadable' });
});
