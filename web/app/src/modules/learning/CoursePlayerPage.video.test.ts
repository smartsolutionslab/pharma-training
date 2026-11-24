import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { render, screen } from '@testing-library/vue';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { createMemoryHistory } from 'vue-router';
import type { LearningMediaUrlDto } from '@/api/generated';
import { createAppRouter } from '@/router';
import {
  type ChapterProgress,
  courseVersion,
  handwashing,
  session,
  sessionId,
  versionId,
} from './coursePlayerFixtures';
import CoursePlayerPage from './CoursePlayerPage.vue';
import { FakeIntersectionObserver } from './fakeIntersectionObserver';
import { FakeMedia } from './fakeMedia';

// The page shows a video chapter with VideoChapter (#44) and reports it through useProgressReporter: at a pause and at
// the end at once, every 30 s while playing (none while paused), and what the chapter announces goes to the page's one
// polite status region. The handwashing chapter is a video of 10:00 by the editor's duration.
FakeMedia.install();
FakeIntersectionObserver.install();
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

const signedUrl = 'https://storage.example/media/handwashing.mp4?sig=page';
let progress: Record<string, ChapterProgress>;

beforeEach(() => {
  vi.useFakeTimers();
  FakeIntersectionObserver.reset();
  progress = {};
  host.mockImplementation(async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    const method = init?.method ?? 'GET';
    if (method === 'GET' && url.pathname === `/api/catalog/course-versions/${versionId}`) {
      return Response.json(courseVersion());
    }
    if (method === 'POST' && url.pathname === '/api/learning/sessions') {
      return Response.json(session(progress), { status: 201 });
    }
    if (method === 'GET' && url.pathname === `/api/learning/sessions/${sessionId}`) {
      return Response.json(session(progress));
    }
    if (method === 'GET' && url.pathname === `/api/learning/media/${handwashing.chapterId}`) {
      const answer: LearningMediaUrlDto = {
        url: signedUrl,
        expiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
        contentType: 'video/mp4',
        kind: 'Video',
      };
      return Response.json(answer);
    }
    if (method === 'POST' && url.pathname === `/api/learning/sessions/${sessionId}/progress`) {
      const body = JSON.parse(String(init?.body)) as { chapterId: string; percent: number };
      progress = { ...progress, [body.chapterId]: { percent: body.percent, completed: body.percent >= 95 } };
      return Response.json(session(progress));
    }
    return new Response('', { status: 404 });
  });
});
afterEach(() => {
  host.mockReset();
  vi.useRealTimers();
});

/** What the page reported, in order. */
const reports = () =>
  host.mock.calls
    .filter(([input]) => String(input).endsWith('/progress'))
    .map(([, init]) => JSON.parse(String(init?.body)) as unknown);

/** The page on the video chapter, its metadata loaded; resolves to the fake behind its <video>. */
async function renderVideoChapter() {
  const router = createAppRouter(createMemoryHistory());
  await router.push(`/learning/courses/${versionId}?chapter=${handwashing.chapterId}`);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const { container } = render(CoursePlayerPage, {
    global: { plugins: [router, [VueQueryPlugin, { queryClient }]] },
  });
  const video = await vi.waitFor(() => {
    const element = container.querySelector('video');
    expect(element?.getAttribute('src') ?? element?.querySelector('source')?.getAttribute('src')).toBe(signedUrl);
    return element ?? document.createElement('video');
  });
  expect(video).toHaveAccessibleName(`Video: ${handwashing.title}`);
  const media = FakeMedia.of(video);
  media.loadMetadata(600);
  return media;
}

/** The polite status region a text is in, once it is there. */
const politely = (text: string) =>
  vi.waitFor(() => {
    const region = screen.getByText(text).closest('[role="status"]');
    expect(region).not.toBeNull();
    return region;
  });

test('the video chapter is shown in the page, with the signed URL', async () => {
  await renderVideoChapter();
});

test('a pause reports the percent watched at once', async () => {
  const media = await renderVideoChapter();
  media.play();

  media.playFor(12);
  media.pause();

  await vi.waitFor(() => expect(reports()).toEqual([{ chapterId: handwashing.chapterId, percent: 2 }]));
});

test('while playing the page reports every 30 s, and nothing while paused', async () => {
  const media = await renderVideoChapter();
  media.play();

  for (let elapsed = 0; elapsed < 30; elapsed += 10) {
    media.playFor(10);
    await vi.advanceTimersByTimeAsync(10_000);
  }
  expect(reports()).toEqual([{ chapterId: handwashing.chapterId, percent: 5 }]);

  for (let elapsed = 30; elapsed < 60; elapsed += 10) {
    media.playFor(10);
    await vi.advanceTimersByTimeAsync(10_000);
  }
  expect(reports()).toEqual([
    { chapterId: handwashing.chapterId, percent: 5 },
    { chapterId: handwashing.chapterId, percent: 10 },
  ]);

  media.pause();
  await vi.advanceTimersByTimeAsync(60_000);
  expect(reports()).toHaveLength(2);
});

test('a seek past what was watched is refused in the polite status region', async () => {
  const media = await renderVideoChapter();
  media.play();
  media.playFor(30);

  media.seekTo(500);

  await vi.waitFor(() => expect(media.currentTime).toBe(30));
  await politely('Vorspulen ist nur bis 0:30 möglich.');
});

test("the video resumes at the session's percent and says so in the polite status region", async () => {
  progress = { [handwashing.chapterId]: { percent: 40, completed: false } };
  const media = await renderVideoChapter();

  await vi.waitFor(() => expect(media.currentTime).toBe(240));
  await politely('Fortgesetzt bei 4:00.');
});

test('a message stays the same element in the polite status region while newer ones come, so it is not heard again', async () => {
  const media = await renderVideoChapter();
  media.play();
  const refusedAt = async (watched: number, said: string) => {
    media.playFor(10);
    media.seekTo(500);
    await vi.waitFor(() => expect(media.currentTime).toBe(watched));
    return politely(said);
  };
  await refusedAt(10, 'Vorspulen ist nur bis 0:10 möglich.');
  await refusedAt(20, 'Vorspulen ist nur bis 0:20 möglich.');
  const region = await refusedAt(30, 'Vorspulen ist nur bis 0:30 möglich.');
  const second = screen.getByText('Vorspulen ist nur bis 0:20 möglich.');

  await refusedAt(40, 'Vorspulen ist nur bis 0:40 möglich.');

  // The oldest message went; the ones still there are the elements they were, with the words they had.
  expect(second.isConnected).toBe(true);
  expect(second).toHaveTextContent('Vorspulen ist nur bis 0:20 möglich.');
  expect(region).not.toHaveTextContent('Vorspulen ist nur bis 0:10 möglich.');
});
