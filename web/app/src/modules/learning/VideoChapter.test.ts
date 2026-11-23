import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { fireEvent, render, screen, within } from '@testing-library/vue';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import type { LearningMediaUrlDto } from '@/api/generated';
import { setLanguage } from '@/i18n';
import { handwashing, refusal } from './coursePlayerFixtures';
import { FakeMedia } from './fakeMedia';
import VideoChapter from './VideoChapter.vue';

// A video chapter (#44): a native <video controls preload="metadata" playsinline> playing the signed URL that
// GET /learning/media/{chapterId} (useGetLearningMediaChapterId) issues, labelled "Video: {title}" and described by the
// visible hint that skipping ahead stops at what was watched. No speed control: the rate stays 1. No auto-play.
//
// <VideoChapter
//   :chapter-id="chapter.chapterId" :title="chapter.title"
//   :duration-seconds="chapter.videoDurationSeconds"        the editor's duration: the percent's denominator
//   :progress-percent="chapter.progressPercent" :completed="chapter.completed"   the server's, as the session says
//   @progress="(percent) => reporter.report(chapterId, percent)"     sent at once: pause, end
//   @tracking="(percent) => reporter.track(chapterId, percent)"      while playing
//   @announce="(text) => ...the page's polite status region" />
//
// The watched maximum rises on timeupdate only by steps of at most 2 s while not seeking; a seek past it by more than
// 0.5 s snaps back to it and is announced. The percent is floor(max / videoDurationSeconds × 100); the end reached by
// playing gives 100. On loadedmetadata it resumes at percent/100 × duration, announced; within 1 s of the end it starts
// at 0. A done chapter seeks freely. The URL is read again 60 s before it expires, and a new URL keeps the position and
// whether it played; a media error reads it once more, then shows an alert with Retry; media-not-ready retries 5 times
// 2 s apart, then shows an alert with Retry.
FakeMedia.install();
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

const mediaPath = `/api/learning/media/${handwashing.chapterId}`;
const firstUrl = 'https://storage.example/media/handwashing.mp4?sig=first';
const secondUrl = 'https://storage.example/media/handwashing.mp4?sig=second';
/** The editor's duration, 10:00; the file's own may differ. */
const duration = 600;

/** The signed URL the host issues: valid 15 minutes from the (fake) now. */
function mediaUrl(url: string): LearningMediaUrlDto {
  return {
    url,
    expiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
    contentType: 'video/mp4',
    kind: 'Video',
  };
}

let issuing: () => Promise<Response>;

beforeEach(() => {
  vi.useFakeTimers();
  issuing = async () => Response.json(mediaUrl(firstUrl));
  host.mockImplementation(async (input) => {
    const url = new URL(String(input), 'http://localhost');
    if (url.pathname === mediaPath) {
      return issuing();
    }
    return new Response('', { status: 404 });
  });
});
afterEach(() => {
  host.mockReset();
  vi.useRealTimers();
});

const mediaReads = () => host.mock.calls.filter(([input]) => String(input).endsWith(mediaPath)).length;

interface ChapterProps {
  chapterId: string;
  title: string;
  durationSeconds: number;
  progressPercent: number;
  completed: boolean;
}

/** The chapter at a stored progress; resolves once its <video> plays the signed URL. */
async function renderChapter(progress: Partial<ChapterProps> = {}) {
  const props: ChapterProps = {
    chapterId: handwashing.chapterId,
    title: handwashing.title,
    durationSeconds: duration,
    progressPercent: 0,
    completed: false,
    ...progress,
  };
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const rendered = render(VideoChapter, { props, global: { plugins: [[VueQueryPlugin, { queryClient }]] } });
  const video = await vi.waitFor(() => {
    const element = rendered.container.querySelector('video');
    expect(element).not.toBeNull();
    expect(sourceOf(element)).toBe(firstUrl);
    return element ?? document.createElement('video');
  });
  return { ...rendered, video, media: FakeMedia.of(video), props };
}

/** The URL the element plays: its src, or its <source>'s. */
function sourceOf(video: HTMLVideoElement | null): string | null {
  return video?.getAttribute('src') ?? video?.querySelector('source')?.getAttribute('src') ?? null;
}

/** The percents the chapter emitted for an event, in order. */
function emittedOf(emitted: () => Record<string, unknown[][]>, event: string): unknown[] {
  return (emitted()[event] ?? []).map(([value]) => value);
}

/** Waits until the media's time is the given one and its seek has landed. */
async function landedAt(media: FakeMedia, seconds: number) {
  await vi.waitFor(() => {
    expect(media.currentTime).toBe(seconds);
    expect(media.seeking).toBe(false);
  });
}

/** The chapter loaded and resumed at 4:00 (40 % of 10:00), playing from there. */
async function playingFrom240() {
  const rendered = await renderChapter({ progressPercent: 40 });
  rendered.media.loadMetadata(duration);
  await landedAt(rendered.media, 240);
  rendered.media.play();
  return rendered;
}

test('the chapter plays the signed URL the host issued in a native video with controls, without auto-play', async () => {
  const { video, media } = await renderChapter();

  expect(video).toHaveAttribute('controls');
  expect(video).toHaveAttribute('preload', 'metadata');
  expect(video).toHaveAttribute('playsinline');
  expect(video).not.toHaveAttribute('autoplay');
  media.loadMetadata(duration);
  await vi.advanceTimersByTimeAsync(1_000);
  expect(media.playCalls).toBe(0);
  expect(media.paused).toBe(true);
});

test('the video is labelled with the chapter and described by the visible hint about skipping ahead', async () => {
  const { video } = await renderChapter();

  const hint = 'Sie können nur bis zur bereits gesehenen Stelle vorspulen.';
  expect(screen.getByText(hint)).toBeVisible();
  expect(video).toHaveAccessibleName(`Video: ${handwashing.title}`);
  expect(video).toHaveAccessibleDescription(hint);
});

test('the label and the hint are in English once switched', async () => {
  setLanguage('en-GB', { remember: false });
  const { video } = await renderChapter();

  expect(video).toHaveAccessibleName(`Video: ${handwashing.title}`);
  expect(video).toHaveAccessibleDescription('You can only skip ahead to the point you have already watched.');
});

test('there is no speed control, and a changed rate goes back to 1', async () => {
  const { video, media } = await renderChapter();
  media.loadMetadata(duration);

  expect(video.getAttribute('controlslist') ?? '').toMatch(/(^|\s)noplaybackrate(\s|$)/);
  video.playbackRate = 2;

  await vi.waitFor(() => expect(media.playbackRate).toBe(1));
});

test('a seek past the watched maximum snaps back to it and says so politely', async () => {
  const { media, emitted } = await playingFrom240();
  media.playFor(10);

  media.seekTo(400);

  await vi.waitFor(() => expect(media.currentTime).toBe(250));
  expect(emittedOf(emitted, 'announce')).toContain('Vorspulen ist nur bis 4:10 möglich.');
});

test('a script setting currentTime past the maximum is snapped back too', async () => {
  const { video, media } = await playingFrom240();
  media.playFor(4);

  video.currentTime = 590;

  await vi.waitFor(() => expect(media.currentTime).toBe(244));
});

test('a seek backwards, and one up to half a second past the maximum, is left alone', async () => {
  const { media, emitted } = await playingFrom240();
  media.playFor(10);

  media.seekTo(30);
  await vi.advanceTimersByTimeAsync(100);
  expect(media.currentTime).toBe(30);

  // Going back did not lower the maximum: a seek past it still stops there.
  media.seekTo(400);
  await vi.waitFor(() => expect(media.currentTime).toBe(250));
  const refusals = emittedOf(emitted, 'announce').filter((text) => String(text).startsWith('Vorspulen'));
  expect(refusals).toHaveLength(1);

  media.seekTo(250.5);
  await vi.advanceTimersByTimeAsync(100);
  expect(media.currentTime).toBe(250.5);
  expect(emittedOf(emitted, 'announce').filter((text) => String(text).startsWith('Vorspulen'))).toEqual(refusals);
});

test('a leap of more than 2 s without a seek does not raise the maximum', async () => {
  const { media, emitted } = await playingFrom240();
  media.playFor(4);

  media.leapTo(420);
  media.pause();

  await vi.waitFor(() => expect(emittedOf(emitted, 'progress')).not.toHaveLength(0));
  // 244 of 600 seconds: 40 %, not the 70 % that 420 would be.
  expect(emittedOf(emitted, 'progress').at(-1)).toBe(40);
  media.seekTo(300);
  await vi.waitFor(() => expect(media.currentTime).toBe(244));
});

test("the percent is the watched maximum over the editor's duration, rounded down, reported at a pause", async () => {
  const { media, emitted } = await playingFrom240();

  media.playFor(5.75);
  media.pause();

  // floor(245.75 / 600 × 100) = 40; at 247 it is floor(41.17) = 41.
  await vi.waitFor(() => expect(emittedOf(emitted, 'progress')).toEqual([40]));
  media.play();
  media.playFor(1.25);
  media.pause();
  await vi.waitFor(() => expect(emittedOf(emitted, 'progress')).toEqual([40, 41]));
});

test("the editor's duration is the denominator, not the file's", async () => {
  const { media, emitted } = await renderChapter();
  // The file says 5:00; the editor typed 10:00.
  media.loadMetadata(300);
  media.play();

  media.playFor(150);
  media.pause();

  await vi.waitFor(() => expect(emittedOf(emitted, 'progress')).toEqual([25]));
});

test('while playing the chapter tracks its percent', async () => {
  const { media, emitted } = await playingFrom240();

  media.playFor(30);

  await vi.waitFor(() => expect(emittedOf(emitted, 'tracking')).toContain(45));
  expect(emittedOf(emitted, 'progress')).toEqual([]);
});

test('the end reached by playing reports 100', async () => {
  const { media, emitted } = await renderChapter({ progressPercent: 99 });
  media.loadMetadata(duration);
  await landedAt(media, 594);
  media.play();

  media.playFor(10);

  await vi.waitFor(() => expect(emittedOf(emitted, 'progress').at(-1)).toBe(100));
});

test('on load the chapter resumes at the stored percent of its duration and says so', async () => {
  const { media, emitted } = await renderChapter({ progressPercent: 40 });

  media.loadMetadata(duration);

  await vi.waitFor(() => expect(media.currentTime).toBe(240));
  expect(media.paused).toBe(true);
  expect(emittedOf(emitted, 'announce')).toContain('Fortgesetzt bei 4:00.');
});

test('the announcements are in English once switched', async () => {
  setLanguage('en-GB', { remember: false });
  const { media, emitted } = await playingFrom240();

  media.seekTo(500);

  await vi.waitFor(() => expect(media.currentTime).toBe(240));
  expect(emittedOf(emitted, 'announce')).toEqual(['Resumed at 4:00.', 'You can only skip ahead to 4:00.']);
});

test('a resume within a second of the end starts at 0, and seeking is free', async () => {
  const { media, emitted } = await renderChapter({ progressPercent: 100, completed: true });

  media.loadMetadata(duration);
  await vi.advanceTimersByTimeAsync(100);
  expect(media.currentTime).toBe(0);
  expect(emittedOf(emitted, 'announce')).toEqual([]);

  media.seekTo(500);
  await vi.advanceTimersByTimeAsync(100);
  expect(media.currentTime).toBe(500);
  expect(emittedOf(emitted, 'announce').filter((text) => String(text).startsWith('Vorspulen'))).toEqual([]);
});

test('a done chapter seeks freely', async () => {
  const { media } = await renderChapter({ progressPercent: 96, completed: true });
  media.loadMetadata(duration);
  await landedAt(media, 576);

  media.seekTo(590);
  await vi.advanceTimersByTimeAsync(100);

  expect(media.currentTime).toBe(590);
});

test("progress made on another device raises the maximum: it is the larger of this one's and the server's", async () => {
  const { media, rerender, props } = await playingFrom240();
  media.pause();

  await rerender({ ...props, progressPercent: 60 });
  media.seekTo(355);
  await vi.advanceTimersByTimeAsync(100);
  expect(media.currentTime).toBe(355);

  media.seekTo(400);
  await vi.waitFor(() => expect(media.currentTime).toBe(360));
});

test('the URL is read again a minute before it expires; the new one keeps the position and the playing', async () => {
  const { video, media } = await playingFrom240();
  media.playFor(10);
  issuing = async () => Response.json(mediaUrl(secondUrl));

  await vi.advanceTimersByTimeAsync(13 * 60_000 + 59_000);
  expect(mediaReads()).toBe(1);
  await vi.advanceTimersByTimeAsync(1_000);
  await vi.waitFor(() => expect(sourceOf(video)).toBe(secondUrl));

  media.loadMetadata(duration);
  await vi.waitFor(() => expect(media.currentTime).toBe(250));
  expect(media.paused).toBe(false);
});

test('a paused video stays paused on the new URL', async () => {
  const { video, media } = await playingFrom240();
  media.playFor(10);
  media.pause();
  issuing = async () => Response.json(mediaUrl(secondUrl));

  await vi.advanceTimersByTimeAsync(14 * 60_000);
  await vi.waitFor(() => expect(sourceOf(video)).toBe(secondUrl));
  media.loadMetadata(duration);

  await vi.waitFor(() => expect(media.currentTime).toBe(250));
  expect(media.paused).toBe(true);
});

test('a media error reads the URL once more; a second shows an alert, whose Retry reads it again', async () => {
  const { media } = await renderChapter();
  media.loadMetadata(duration);

  media.fail();
  await vi.waitFor(() => expect(mediaReads()).toBe(2));
  expect(screen.queryByRole('alert')).toBeNull();

  media.fail();
  const alert = await vi.waitFor(() => screen.getByRole('alert'));
  expect(alert).toHaveTextContent('Das Video konnte nicht abgespielt werden.');
  await fireEvent.click(within(alert).getByRole('button', { name: 'Erneut versuchen' }));

  await vi.waitFor(() => expect(mediaReads()).toBe(3));
});

test('a file not ready yet is asked for 5 more times 2 s apart, then an alert with Retry says so', async () => {
  issuing = async () => refusal(409, 'learning.media-not-ready');
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(VideoChapter, {
    props: {
      chapterId: handwashing.chapterId,
      title: handwashing.title,
      durationSeconds: duration,
      progressPercent: 0,
      completed: false,
    },
    global: { plugins: [[VueQueryPlugin, { queryClient }]] },
  });

  await vi.advanceTimersByTimeAsync(9_000);
  expect(screen.queryByRole('alert')).toBeNull();
  await vi.advanceTimersByTimeAsync(2_000);
  expect(mediaReads()).toBe(6);
  const alert = await vi.waitFor(() => screen.getByRole('alert'));
  expect(alert).toHaveTextContent('Die Datei wird noch bereitgestellt. Versuchen Sie es in einem Moment erneut.');

  issuing = async () => Response.json(mediaUrl(firstUrl));
  await fireEvent.click(within(alert).getByRole('button', { name: 'Erneut versuchen' }));

  await vi.waitFor(() => expect(sourceOf(document.querySelector('video'))).toBe(firstUrl));
  expect(screen.queryByRole('alert')).toBeNull();
});
