import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, waitFor } from '@testing-library/vue';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { defineComponent } from 'vue';
import {
  type EditorialChapterDto,
  type EditorialCourseVersionDto,
  getGetEditorialCourseVersionsIdQueryKey,
} from '@/api/generated';
import { t } from '@/i18n';
import { useReorderChapters } from './useReorderChapters';

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

// Reordering is optimistic (spec 2.3): the new order is in the version's cache before PUT .../chapters/order answers,
// so the list moves at once. A refusal puts the previous order back, says so (the host's words where a module sent
// some, else the catalog's) and reads the version again; success reads it again too.
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);
beforeEach(() => {
  notify.success.mockReset();
  notify.failure.mockReset();
});
afterEach(() => host.mockReset());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const versionId = '0198c1a0-0000-7000-8000-0000000000c1';
const versionKey = getGetEditorialCourseVersionsIdQueryKey(versionId);

const chapter = (id: string, position: number, title: string): EditorialChapterDto => ({
  id,
  position,
  title,
  kind: 'Text',
  text: `# ${title}`,
  file: null,
  durationSeconds: null,
});
const a = chapter('0198c1a0-0000-7000-8000-0000000000e1', 1, 'A');
const b = chapter('0198c1a0-0000-7000-8000-0000000000e2', 2, 'B');
const c = chapter('0198c1a0-0000-7000-8000-0000000000e3', 3, 'C');

const version: EditorialCourseVersionDto = {
  id: versionId,
  courseId: '0198c1a0-0000-7000-8000-0000000000d1',
  number: 1,
  title: 'Hygiene in der Apotheke',
  status: 'Draft',
  chapters: [a, b, c],
  questions: [],
  blueprint: { questions: 10, passingScorePercent: 80, validityMonths: 12 },
};

// A query client of its own per test, holding the version; it does not retry, so a failure shows at once.
function renderReordering() {
  const used: { reordering?: ReturnType<typeof useReorderChapters> } = {};
  const Reordering = defineComponent({
    setup() {
      used.reordering = useReorderChapters(versionId);
      return () => null;
    },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  queryClient.setQueryData(versionKey, version);
  render(Reordering, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } });
  if (used.reordering === undefined) {
    throw new Error('The component did not set up.');
  }
  return { reordering: used.reordering, queryClient };
}

/** The chapters' ids in the order the cache holds them. */
const cachedOrder = (queryClient: QueryClient) =>
  queryClient.getQueryData<EditorialCourseVersionDto>(versionKey)?.chapters.map((each) => each.id);

/** The host's answer, held until the test lets it go. */
function held() {
  let answer: (response: Response) => void = () => undefined;
  const response = new Promise<Response>((resolve) => {
    answer = resolve;
  });
  return { response, answer };
}

test('the new order is in the cache before the host answers, and is sent as every chapter id', async () => {
  const { response, answer } = held();
  host.mockImplementation(() => response);
  const { reordering, queryClient } = renderReordering();

  const done = reordering.reorder([c.id, a.id, b.id]);

  await waitFor(() => expect(cachedOrder(queryClient)).toEqual([c.id, a.id, b.id]));
  expect(host).toHaveBeenCalledOnce();
  const [input, init] = host.mock.calls[0] ?? [];
  expect(String(input)).toBe(`/api/editorial/course-versions/${versionId}/chapters/order`);
  expect(init?.method).toBe('PUT');
  expect(JSON.parse(String(init?.body))).toEqual({ chapters: [c.id, a.id, b.id] });

  answer(Response.json({}));
  await done;

  expect(cachedOrder(queryClient)).toEqual([c.id, a.id, b.id]);
  expect(queryClient.getQueryState(versionKey)?.isInvalidated).toBe(true);
  expect(notify.failure).not.toHaveBeenCalled();
});

test('an order the host cannot save is undone in the cache, reported and read again', async () => {
  const { response, answer } = held();
  host.mockImplementation(() => response);
  const { reordering, queryClient } = renderReordering();

  const done = reordering.reorder([c.id, a.id, b.id]);
  await waitFor(() => expect(cachedOrder(queryClient)).toEqual([c.id, a.id, b.id]));
  answer(new Response('', { status: 500 }));
  await done;

  expect(cachedOrder(queryClient)).toEqual([a.id, b.id, c.id]);
  expect(notify.failure).toHaveBeenCalledOnce();
  expect(notify.failure).toHaveBeenCalledWith(t('editorial.chapters.reorderFailed'));
  expect(notify.failure).toHaveBeenCalledWith(
    'Die Reihenfolge konnte nicht gespeichert werden. Die vorherige Reihenfolge ist wiederhergestellt.',
  );
  expect(queryClient.getQueryState(versionKey)?.isInvalidated).toBe(true);
});

test("an order a module refuses is undone and reported in the host's words", async () => {
  host.mockImplementation(
    async () =>
      new Response(
        JSON.stringify({
          title: 'Conflict',
          status: 409,
          detail: 'Die Reihenfolge nennt nicht jedes Kapitel der Version genau einmal.',
          code: 'editorial.order-does-not-match-chapters',
        }),
        { status: 409, headers: { 'Content-Type': 'application/problem+json' } },
      ),
  );
  const { reordering, queryClient } = renderReordering();

  await reordering.reorder([c.id, a.id, b.id]);

  expect(cachedOrder(queryClient)).toEqual([a.id, b.id, c.id]);
  expect(notify.failure).toHaveBeenCalledWith('Die Reihenfolge nennt nicht jedes Kapitel der Version genau einmal.');
  expect(queryClient.getQueryState(versionKey)?.isInvalidated).toBe(true);
});
