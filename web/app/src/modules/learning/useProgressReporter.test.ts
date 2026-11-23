import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, waitFor } from '@testing-library/vue';
import { afterEach, beforeEach, expect, onTestFinished, test, vi } from 'vitest';
import { defineComponent, nextTick, type Ref, ref } from 'vue';
import { getGetLearningSessionsIdQueryKey, type LearningSessionDto } from '@/api/generated';
import { t } from '@/i18n';
import { handwashing, introduction, refusal, session, sessionId, summary } from './coursePlayerFixtures';
import { useProgressReporter } from './useProgressReporter';

// useProgressReporter(sessionId, { titleOf, announce }) reports a chapter's percent to
// POST /learning/sessions/{id}/progress through the generated client (#44):
// - report(chapterId, percent) sends at once (a pause, the chapter's end, a chapter change);
// - track(chapterId, percent) is progress while playing, kept pending and sent at most every 30 s;
// - flush() sends what is pending now ("Save now");
// - failed and finished say whether the last report failed and whether the session is finished.
// Hiding the page (visibilitychange to hidden), pagehide and unmounting send what is pending with keepalive.
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

const sessionKey = getGetLearningSessionsIdQueryKey(sessionId);
const titles: Record<string, string> = {
  [introduction.chapterId]: introduction.title,
  [handwashing.chapterId]: handwashing.title,
  [summary.chapterId]: summary.title,
};
const announce = vi.fn<(text: string) => void>();

/** What the host stores: the session with the chapter at the reported percent, done at 100. */
let stored: LearningSessionDto;
function storing(body: { chapterId: string; percent: number }, status: LearningSessionDto['status'] = 'InProgress') {
  stored = session(
    Object.fromEntries(
      stored.chapters.map((chapter) => [
        chapter.chapterId,
        chapter.chapterId === body.chapterId
          ? { percent: Math.max(Number(chapter.progressPercent), body.percent), completed: body.percent === 100 }
          : { percent: Number(chapter.progressPercent), completed: chapter.completed },
      ]),
    ),
    status,
  );
  return Response.json(stored);
}

beforeEach(() => {
  stored = session();
  host.mockImplementation(async (_input, init) => storing(JSON.parse(String(init?.body))));
  announce.mockReset();
});
afterEach(() => {
  host.mockReset();
  vi.useRealTimers();
});
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

/** The reporter in a component of its own, with a query client holding the session as it was started. */
function renderReporter(started: LearningSessionDto = session(), id: Ref<string> = ref(sessionId)) {
  const used: { reporter?: ReturnType<typeof useProgressReporter> } = {};
  const Reporter = defineComponent({
    setup() {
      used.reporter = useProgressReporter(id, {
        titleOf: (chapterId: string) => titles[chapterId] ?? '',
        announce,
      });
      return () => null;
    },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  queryClient.setQueryData(sessionKey, started);
  stored = started;
  const { unmount } = render(Reporter, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } });
  if (used.reporter === undefined) {
    throw new Error('The component did not set up.');
  }
  return { reporter: used.reporter, queryClient, unmount };
}

/** What the reporter sent, in order: the address, the body and whether the request outlives the page. */
const sent = () =>
  host.mock.calls.map(([input, init]) => ({
    url: String(input),
    method: init?.method,
    body: JSON.parse(String(init?.body)) as unknown,
    keepalive: init?.keepalive === true,
  }));
const bodies = () => sent().map(({ body }) => body);
const progressUrl = `/api/learning/sessions/${sessionId}/progress`;

/** A host answer the test releases when it wants. */
function deferred() {
  let release: (response: Response) => void = () => undefined;
  const answer = new Promise<Response>((resolve) => {
    release = resolve;
  });
  return { answer, release };
}

test('a report is posted at once to the session through the generated client', async () => {
  const { reporter } = renderReporter();

  reporter.report(introduction.chapterId, 40);

  await waitFor(() =>
    expect(sent()).toEqual([
      { url: progressUrl, method: 'POST', body: { chapterId: introduction.chapterId, percent: 40 }, keepalive: false },
    ]),
  );
});

test("the host's answer is the stored session: it goes under the session's key, without another read", async () => {
  const { reporter, queryClient } = renderReporter();

  reporter.report(introduction.chapterId, 40);

  await waitFor(() =>
    expect(queryClient.getQueryData<LearningSessionDto>(sessionKey)?.chapters).toContainEqual(
      expect.objectContaining({ chapterId: introduction.chapterId, progressPercent: 40 }),
    ),
  );
  expect(sent().map(({ method }) => method)).toEqual(['POST']);
});

test('the reported percent never goes down: nothing at or below what the host acknowledged is sent', async () => {
  const { reporter } = renderReporter();

  reporter.report(introduction.chapterId, 50);
  await waitFor(() => expect(bodies()).toHaveLength(1));
  reporter.report(introduction.chapterId, 40);
  reporter.report(introduction.chapterId, 50);
  reporter.report(introduction.chapterId, 51);

  await waitFor(() =>
    expect(bodies()).toEqual([
      { chapterId: introduction.chapterId, percent: 50 },
      { chapterId: introduction.chapterId, percent: 51 },
    ]),
  );
});

test('progress the host already has from another device is not reported again', async () => {
  const { reporter } = renderReporter(session({ [handwashing.chapterId]: { percent: 70, completed: false } }));

  reporter.report(handwashing.chapterId, 60);
  reporter.report(handwashing.chapterId, 70);
  reporter.report(introduction.chapterId, 10);

  await waitFor(() => expect(bodies()).toEqual([{ chapterId: introduction.chapterId, percent: 10 }]));
});

test('one report is in flight at a time, and only the latest pending percent follows it', async () => {
  const first = deferred();
  host.mockImplementationOnce(() => first.answer);
  const { reporter } = renderReporter();

  reporter.report(introduction.chapterId, 10);
  await waitFor(() => expect(bodies()).toHaveLength(1));
  reporter.report(introduction.chapterId, 20);
  reporter.report(introduction.chapterId, 30);
  expect(bodies()).toHaveLength(1);

  first.release(storing({ chapterId: introduction.chapterId, percent: 10 }));

  await waitFor(() =>
    expect(bodies()).toEqual([
      { chapterId: introduction.chapterId, percent: 10 },
      { chapterId: introduction.chapterId, percent: 30 },
    ]),
  );
});

test('progress while playing is sent at most every 30 seconds, the latest percent each time', async () => {
  vi.useFakeTimers();
  const { reporter } = renderReporter();

  reporter.track(handwashing.chapterId, 5);
  await vi.advanceTimersByTimeAsync(10_000);
  reporter.track(handwashing.chapterId, 10);
  await vi.advanceTimersByTimeAsync(19_000);
  expect(bodies()).toEqual([]);

  await vi.advanceTimersByTimeAsync(1_000);
  expect(bodies()).toEqual([{ chapterId: handwashing.chapterId, percent: 10 }]);

  reporter.track(handwashing.chapterId, 15);
  reporter.track(handwashing.chapterId, 20);
  await vi.advanceTimersByTimeAsync(30_000);
  expect(bodies()).toEqual([
    { chapterId: handwashing.chapterId, percent: 10 },
    { chapterId: handwashing.chapterId, percent: 20 },
  ]);

  await vi.advanceTimersByTimeAsync(60_000);
  expect(bodies()).toHaveLength(2);
});

test('a pause or the end reports at once what was tracked, without waiting for the 30 seconds', async () => {
  vi.useFakeTimers();
  const { reporter } = renderReporter();

  reporter.track(handwashing.chapterId, 12);
  await vi.advanceTimersByTimeAsync(5_000);
  reporter.report(handwashing.chapterId, 14);
  await vi.advanceTimersByTimeAsync(0);
  expect(bodies()).toEqual([{ chapterId: handwashing.chapterId, percent: 14 }]);

  reporter.report(handwashing.chapterId, 100);
  await vi.advanceTimersByTimeAsync(0);
  expect(bodies()).toEqual([
    { chapterId: handwashing.chapterId, percent: 14 },
    { chapterId: handwashing.chapterId, percent: 100 },
  ]);

  // The 30 seconds that were running for the tracked percent send nothing more.
  await vi.advanceTimersByTimeAsync(30_000);
  expect(bodies()).toHaveLength(2);
});

test('hiding the page sends what is pending with keepalive', async () => {
  const { reporter } = renderReporter();

  reporter.track(handwashing.chapterId, 33);
  Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'hidden' });
  document.dispatchEvent(new Event('visibilitychange'));
  Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' });

  await waitFor(() =>
    expect(sent()).toEqual([
      { url: progressUrl, method: 'POST', body: { chapterId: handwashing.chapterId, percent: 33 }, keepalive: true },
    ]),
  );
});

test('a report on its way when the page is left is sent again with keepalive, since the page may cut it off', async () => {
  const first = deferred();
  host.mockImplementationOnce(() => first.answer);
  const { reporter } = renderReporter();

  reporter.report(handwashing.chapterId, 40);
  await waitFor(() => expect(sent()).toHaveLength(1));
  window.dispatchEvent(new Event('pagehide'));

  await waitFor(() =>
    expect(sent()).toEqual([
      { url: progressUrl, method: 'POST', body: { chapterId: handwashing.chapterId, percent: 40 }, keepalive: false },
      { url: progressUrl, method: 'POST', body: { chapterId: handwashing.chapterId, percent: 40 }, keepalive: true },
    ]),
  );
  first.release(storing({ chapterId: handwashing.chapterId, percent: 40 }));
});

test('a report the host acknowledged is not sent again when the page is left', async () => {
  const { reporter } = renderReporter();

  reporter.report(handwashing.chapterId, 40);
  await waitFor(() => expect(sent()).toHaveLength(1));
  await reporter.flush();
  window.dispatchEvent(new Event('pagehide'));

  expect(sent()).toHaveLength(1);
});

test("what waits for one session is dropped when the session changes (another course version): it is not the new one's", async () => {
  const id = ref(sessionId);
  const { reporter, queryClient } = renderReporter(session(), id);
  const otherSession = '0198c1a0-0000-7000-8000-0000000000b2';
  queryClient.setQueryData(getGetLearningSessionsIdQueryKey(otherSession), { ...session(), id: otherSession });

  reporter.track(handwashing.chapterId, 33);
  id.value = otherSession;
  await nextTick();
  await reporter.flush();

  expect(sent()).toEqual([]);
});

test('the page coming back into view sends nothing', async () => {
  const { reporter } = renderReporter();

  reporter.track(handwashing.chapterId, 33);
  document.dispatchEvent(new Event('visibilitychange'));

  // The page is visible: the tracked percent waits for its 30 seconds.
  expect(bodies()).toEqual([]);
});

test('leaving the page (pagehide) sends what is pending with keepalive', async () => {
  const { reporter } = renderReporter();

  reporter.track(handwashing.chapterId, 47);
  window.dispatchEvent(new Event('pagehide'));

  await waitFor(() =>
    expect(sent()).toEqual([
      { url: progressUrl, method: 'POST', body: { chapterId: handwashing.chapterId, percent: 47 }, keepalive: true },
    ]),
  );
});

test('leaving the route (the reporter unmounted) sends what is pending with keepalive, and listens no more', async () => {
  const { reporter, unmount } = renderReporter();

  reporter.track(handwashing.chapterId, 52);
  unmount();

  await waitFor(() =>
    expect(sent()).toEqual([
      { url: progressUrl, method: 'POST', body: { chapterId: handwashing.chapterId, percent: 52 }, keepalive: true },
    ]),
  );
  window.dispatchEvent(new Event('pagehide'));
  expect(sent()).toHaveLength(1);
});

test('a session that changed meanwhile is read again and the report sent once more', async () => {
  host.mockImplementationOnce(async () => refusal(409, 'learning.session-changed'));
  const { reporter, queryClient } = renderReporter();
  const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

  reporter.report(introduction.chapterId, 40);

  await waitFor(() =>
    expect(bodies()).toEqual([
      { chapterId: introduction.chapterId, percent: 40 },
      { chapterId: introduction.chapterId, percent: 40 },
    ]),
  );
  expect(invalidate).toHaveBeenCalledWith(expect.objectContaining({ queryKey: sessionKey }));
  expect(reporter.failed.value).toBe(false);
});

test('a session that changed again is not sent a third time', async () => {
  host.mockImplementation(async () => refusal(409, 'learning.session-changed'));
  const { reporter } = renderReporter();

  reporter.report(introduction.chapterId, 40);

  await waitFor(() => expect(reporter.failed.value).toBe(true));
  expect(bodies()).toHaveLength(2);
});

test('a finished session stops the reporting', async () => {
  host.mockImplementation(async () => refusal(409, 'learning.session-finished'));
  const { reporter } = renderReporter();

  reporter.report(introduction.chapterId, 40);
  await waitFor(() => expect(reporter.finished.value).toBe(true));
  reporter.report(introduction.chapterId, 60);
  reporter.track(introduction.chapterId, 70);
  window.dispatchEvent(new Event('pagehide'));
  await reporter.flush();

  expect(bodies()).toEqual([{ chapterId: introduction.chapterId, percent: 40 }]);
  expect(reporter.failed.value).toBe(false);
});

test.each([
  ['the host fails', async () => new Response('', { status: 503 })],
  ['the chapter is not in the course (422)', async () => refusal(422, 'learning.chapter-not-in-course')],
  [
    'the network fails',
    async (): Promise<Response> => {
      throw new TypeError('Failed to fetch');
    },
  ],
])('when %s the percent stays pending and goes out with the next report', async (_case, failing) => {
  host.mockImplementationOnce(failing);
  const { reporter } = renderReporter();

  reporter.report(introduction.chapterId, 40);
  await waitFor(() => expect(reporter.failed.value).toBe(true));
  reporter.report(introduction.chapterId, 45);

  await waitFor(() =>
    expect(bodies()).toEqual([
      { chapterId: introduction.chapterId, percent: 40 },
      { chapterId: introduction.chapterId, percent: 45 },
    ]),
  );
  await waitFor(() => expect(reporter.failed.value).toBe(false));
});

test('Save now (flush) sends the pending percent again, and the failure clears on the next 200', async () => {
  host.mockImplementationOnce(async () => new Response('', { status: 503 }));
  const { reporter } = renderReporter();

  reporter.report(introduction.chapterId, 40);
  await waitFor(() => expect(reporter.failed.value).toBe(true));
  await reporter.flush();

  await waitFor(() => expect(reporter.failed.value).toBe(false));
  expect(bodies()).toEqual([
    { chapterId: introduction.chapterId, percent: 40 },
    { chapterId: introduction.chapterId, percent: 40 },
  ]);
});

test('a failed report is sent again with keepalive when the page is hidden', async () => {
  host.mockImplementationOnce(async () => new Response('', { status: 503 }));
  const { reporter } = renderReporter();

  reporter.report(introduction.chapterId, 40);
  await waitFor(() => expect(reporter.failed.value).toBe(true));
  window.dispatchEvent(new Event('pagehide'));

  await waitFor(() => expect(sent().map(({ keepalive }) => keepalive)).toEqual([false, true]));
});

test('a chapter that becomes done is announced by its title, once', async () => {
  const { reporter } = renderReporter();

  reporter.report(introduction.chapterId, 100);

  await waitFor(() =>
    expect(announce.mock.calls).toEqual([[t('learning.player.chapterDone', { title: introduction.title })]]),
  );
});

test('a chapter that was done already is not announced again', async () => {
  const { reporter, queryClient } = renderReporter(
    session({ [introduction.chapterId]: { percent: 100, completed: true } }),
  );

  reporter.report(handwashing.chapterId, 30);

  // Once the answer is in the cache, the reporter has read it.
  await waitFor(() =>
    expect(queryClient.getQueryData<LearningSessionDto>(sessionKey)?.chapters).toContainEqual(
      expect.objectContaining({ chapterId: handwashing.chapterId, progressPercent: 30 }),
    ),
  );
  expect(announce).not.toHaveBeenCalled();
});

test('the last chapter done announces it and that the content is completed', async () => {
  const { reporter } = renderReporter(
    session({
      [introduction.chapterId]: { percent: 100, completed: true },
      [handwashing.chapterId]: { percent: 100, completed: true },
    }),
  );
  host.mockImplementation(async (_input, init) => storing(JSON.parse(String(init?.body)), 'ContentCompleted'));

  reporter.report(summary.chapterId, 100);

  await waitFor(() =>
    expect(announce.mock.calls).toEqual([
      [t('learning.player.chapterDone', { title: summary.title })],
      [t('learning.player.contentCompleted')],
    ]),
  );
});

test('the reporter keeps nothing in the browser’s storage', async () => {
  const setItem = vi.spyOn(Storage.prototype, 'setItem');
  const open = vi.fn();
  vi.stubGlobal('indexedDB', { open });
  onTestFinished(() => {
    setItem.mockRestore();
    vi.unstubAllGlobals();
    vi.stubGlobal('fetch', host);
  });
  host.mockImplementationOnce(async () => new Response('', { status: 503 }));
  const { reporter, unmount } = renderReporter();

  reporter.report(introduction.chapterId, 40);
  await waitFor(() => expect(reporter.failed.value).toBe(true));
  reporter.track(introduction.chapterId, 60);
  unmount();
  await waitFor(() => expect(bodies()).toHaveLength(2));

  expect(setItem).not.toHaveBeenCalled();
  expect(open).not.toHaveBeenCalled();
  expect(localStorage.length).toBe(0);
  expect(sessionStorage.length).toBe(0);
});
