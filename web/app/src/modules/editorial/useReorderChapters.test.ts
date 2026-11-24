import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, waitFor } from '@testing-library/vue';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { defineComponent } from 'vue';
import {
  type EditorialChapterDto,
  type EditorialCourseVersionDto,
  getGetEditorialCourseVersionsIdQueryKey,
} from '@/api/generated';
import { type Language, setLanguage, t } from '@/i18n';
import { useReorderChapters } from './useReorderChapters';

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

// Reordering is optimistic (spec 2.3): the new order is in the version's cache before PUT .../chapters/order answers,
// so the list moves at once. A refusal puts the previous order back, says so (by its code in the catalog's words, #147;
// the host's only for a code the catalog does not know) and reads the version again; success reads it again too.
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
  pdfText: null,
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
  const { unmount } = render(Reordering, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } });
  if (used.reordering === undefined) {
    throw new Error('The component did not set up.');
  }
  return { reordering: used.reordering, queryClient, unmount };
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

/** A refusal of the order as the host sends it: a module's code and its words, which only an unknown code shows. */
const refusedOrder = (code: string) =>
  new Response(JSON.stringify({ title: 'Conflict', status: 409, detail: 'SERVER WORDS', code }), {
    status: 409,
    headers: { 'Content-Type': 'application/problem+json' },
  });

test.each<[Language, string]>([
  ['de-DE', 'Die Reihenfolge muss jedes Kapitel genau einmal enthalten. Laden Sie die Version neu.'],
  ['en-GB', 'The order must contain every chapter exactly once. Reload the version.'],
])(
  'an order a module refuses is undone and reported by its code in the catalog text, in %s',
  async (language, text) => {
    setLanguage(language, { remember: false });
    host.mockImplementation(async () => refusedOrder('editorial.order-does-not-match'));
    const { reordering, queryClient } = renderReordering();

    await reordering.reorder([c.id, a.id, b.id]);

    expect(cachedOrder(queryClient)).toEqual([a.id, b.id, c.id]);
    expect(notify.failure).toHaveBeenCalledWith(text);
    expect(String(notify.failure.mock.calls)).not.toContain('SERVER WORDS');
    expect(queryClient.getQueryState(versionKey)?.isInvalidated).toBe(true);
  },
);

test("an order refused with a module code the catalog does not know is reported in the host's words", async () => {
  host.mockImplementation(async () => refusedOrder('editorial.order-does-not-match-chapters'));
  const { reordering, queryClient } = renderReordering();

  await reordering.reorder([c.id, a.id, b.id]);

  expect(cachedOrder(queryClient)).toEqual([a.id, b.id, c.id]);
  expect(notify.failure).toHaveBeenCalledWith('SERVER WORDS');
});

test('an order that never reached the host is undone and reported in the catalog text of the action', async () => {
  host.mockImplementation(async () => {
    throw new TypeError('Failed to fetch');
  });
  const { reordering, queryClient } = renderReordering();

  await reordering.reorder([c.id, a.id, b.id]);

  expect(cachedOrder(queryClient)).toEqual([a.id, b.id, c.id]);
  expect(notify.failure).toHaveBeenCalledWith(t('editorial.chapters.reorderFailed'));
});

// Moves made while an order is on its way are serialised: at most one PUT .../chapters/order is in flight, and the
// next one sends the latest order once the one before has settled. Two PUTs in flight could reach the host in either
// order and leave the older order saved.

/** Every PUT of an order, held until the test answers it, in the order they went out. */
function holdEach() {
  const puts: { body: unknown; answer: (response: Response) => void }[] = [];
  host.mockImplementation(
    (_input, init) =>
      new Promise<Response>((resolve) => {
        puts.push({ body: JSON.parse(String(init?.body)), answer: resolve });
      }),
  );
  return puts;
}

test('a second move while the first order is on its way waits for it, then sends the latest order', async () => {
  const puts = holdEach();
  const { reordering, queryClient } = renderReordering();

  void reordering.reorder([c.id, a.id, b.id]);
  await waitFor(() => expect(puts).toHaveLength(1));
  void reordering.reorder([c.id, b.id, a.id]);

  // The list shows the latest order at once, but only one order is on its way.
  await waitFor(() => expect(cachedOrder(queryClient)).toEqual([c.id, b.id, a.id]));
  expect(puts).toHaveLength(1);
  expect(puts[0]?.body).toEqual({ chapters: [c.id, a.id, b.id] });

  puts[0]?.answer(Response.json({}));

  await waitFor(() => expect(puts).toHaveLength(2));
  expect(puts[1]?.body).toEqual({ chapters: [c.id, b.id, a.id] });
  expect(cachedOrder(queryClient)).toEqual([c.id, b.id, a.id]);
  puts[1]?.answer(Response.json({}));

  await waitFor(() => expect(queryClient.getQueryState(versionKey)?.isInvalidated).toBe(true));
  expect(cachedOrder(queryClient)).toEqual([c.id, b.id, a.id]);
  expect(puts).toHaveLength(2);
  expect(notify.failure).not.toHaveBeenCalled();
});

test('an order refused after an earlier one was saved restores the order from before the refused request', async () => {
  const puts = holdEach();
  const { reordering, queryClient } = renderReordering();

  void reordering.reorder([c.id, a.id, b.id]);
  await waitFor(() => expect(puts).toHaveLength(1));
  void reordering.reorder([c.id, b.id, a.id]);
  await waitFor(() => expect(cachedOrder(queryClient)).toEqual([c.id, b.id, a.id]));
  expect(puts).toHaveLength(1);
  puts[0]?.answer(Response.json({}));
  await waitFor(() => expect(puts).toHaveLength(2));

  puts[1]?.answer(new Response('', { status: 500 }));

  await waitFor(() => expect(notify.failure).toHaveBeenCalledOnce());
  expect(notify.failure).toHaveBeenCalledWith(t('editorial.chapters.reorderFailed'));
  // The first order was saved: the list goes back to it, not to the one before both moves.
  await waitFor(() => expect(cachedOrder(queryClient)).toEqual([c.id, a.id, b.id]));
});

test('an order refused while a later move waits restores the order from before it, and the later move is not sent', async () => {
  const puts = holdEach();
  const { reordering, queryClient } = renderReordering();

  void reordering.reorder([c.id, a.id, b.id]);
  await waitFor(() => expect(puts).toHaveLength(1));
  void reordering.reorder([c.id, b.id, a.id]);
  await waitFor(() => expect(cachedOrder(queryClient)).toEqual([c.id, b.id, a.id]));
  expect(puts).toHaveLength(1);

  puts[0]?.answer(new Response('', { status: 500 }));

  await waitFor(() => expect(notify.failure).toHaveBeenCalledOnce());
  await waitFor(() => expect(cachedOrder(queryClient)).toEqual([a.id, b.id, c.id]));
  await waitFor(() => expect(queryClient.getQueryState(versionKey)?.isInvalidated).toBe(true));
  expect(puts).toHaveLength(1);
  expect(notify.failure).toHaveBeenCalledOnce();
});

// Leaving or reloading the page while an order is not yet saved would drop it without a word (#223): until every order
// has settled, the page asks the browser to confirm leaving it, by cancelling beforeunload.

/** Whether the page would ask before it is left or reloaded now. */
function asksBeforeLeaving() {
  const event = new Event('beforeunload', { cancelable: true });
  window.dispatchEvent(event);
  return event.defaultPrevented;
}

test('while an order is being saved, leaving the page asks first; once it is saved, it does not', async () => {
  const puts = holdEach();
  const { reordering } = renderReordering();
  expect(asksBeforeLeaving()).toBe(false);

  const done = reordering.reorder([c.id, a.id, b.id]);
  await waitFor(() => expect(puts).toHaveLength(1));
  expect(asksBeforeLeaving()).toBe(true);

  // A move made meanwhile waits for the first order, and the page keeps asking until it is saved too.
  void reordering.reorder([c.id, b.id, a.id]);
  puts[0]?.answer(Response.json({}));
  await waitFor(() => expect(puts).toHaveLength(2));
  expect(asksBeforeLeaving()).toBe(true);

  puts[1]?.answer(Response.json({}));
  await done;
  expect(asksBeforeLeaving()).toBe(false);
});

test('once an order is refused and undone, leaving the page does not ask', async () => {
  const puts = holdEach();
  const { reordering } = renderReordering();

  const done = reordering.reorder([c.id, a.id, b.id]);
  await waitFor(() => expect(puts).toHaveLength(1));
  expect(asksBeforeLeaving()).toBe(true);
  puts[0]?.answer(new Response('', { status: 500 }));
  await done;

  expect(notify.failure).toHaveBeenCalledOnce();
  expect(asksBeforeLeaving()).toBe(false);
});

test('an order still being saved when the list is gone keeps the page asking until it is saved', async () => {
  const puts = holdEach();
  const { reordering, unmount } = renderReordering();

  const done = reordering.reorder([c.id, a.id, b.id]);
  await waitFor(() => expect(puts).toHaveLength(1));
  // Another page of the app: the order is still on its way, and a reload now would lose it.
  unmount();
  expect(asksBeforeLeaving()).toBe(true);

  puts[0]?.answer(Response.json({}));
  await done;
  expect(asksBeforeLeaving()).toBe(false);
});
