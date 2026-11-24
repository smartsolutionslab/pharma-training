import { cleanup, render, screen, waitFor } from '@testing-library/vue';
import { afterEach, beforeEach, expect, test } from 'vitest';
import { setLanguage, t } from '@/i18n';
import { FakeIntersectionObserver } from './fakeIntersectionObserver';
import TextChapter from './TextChapter.vue';

// A text chapter (#44): the editor's Markdown, sanitised, through MarkdownView (renderMarkdown) below the chapter's h2,
// so its headings start at h3. An IntersectionObserver watches an end marker; once the marker is in view, the chapter
// emits progress 100, once, also for a text that fits on the screen, and no percent in between. The hint shows until
// the chapter is done.
//
// <TextChapter :text="chapter.text" :completed="chapter.completed" @progress="(percent) => report(chapterId, percent)" />
FakeIntersectionObserver.install();
beforeEach(() => FakeIntersectionObserver.reset());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const text = '# Willkommen\n\nDas ist **wichtig**: [die Leitlinie](https://example.org/leitlinie).';

function renderChapter(props: { text: string; completed: boolean } = { text, completed: false }) {
  return render(TextChapter, { props });
}

/** The percents the chapter emitted, in order. */
function progressOf(emitted: () => Record<string, unknown[][]>): unknown[] {
  return (emitted().progress ?? []).map(([percent]) => percent);
}

test("the chapter shows the editor's Markdown through MarkdownView, its headings below the chapter's h2", () => {
  const { container } = renderChapter();

  expect(container.querySelector('.markdown')).not.toBeNull();
  expect(screen.getByRole('heading', { level: 3, name: 'Willkommen' })).toBeInTheDocument();
  expect(screen.getByText('wichtig').tagName).toBe('STRONG');
  expect(screen.getByRole('link', { name: 'die Leitlinie' })).toHaveAttribute('href', 'https://example.org/leitlinie');
});

test('a script, an image with onerror and a javascript: link in the Markdown are shown inert', () => {
  const hostile = [
    '<script>window.hacked = true</script>',
    '',
    '<img src="x" onerror="window.hacked = true">',
    '',
    '[Klick](javascript:window.hacked=true)',
    '',
    '<a href="javascript:window.hacked=true">Roh</a>',
  ].join('\n');

  const { container } = renderChapter({ text: hostile, completed: false });

  expect(container.querySelector('script')).toBeNull();
  expect(container.querySelector('img')).toBeNull();
  expect(container.querySelector('[onerror]')).toBeNull();
  expect([...container.querySelectorAll('a')].filter((link) => /^\s*javascript:/i.test(link.href))).toEqual([]);
  expect(container.querySelector('[href^="javascript:" i]')).toBeNull();
  // What was raw HTML is text the learner can read, not markup.
  expect(container.textContent).toContain('<script>window.hacked = true</script>');
  expect(Reflect.get(window, 'hacked')).toBeUndefined();
});

test('the chapter watches an end marker after its text', async () => {
  renderChapter();

  await waitFor(() => expect(FakeIntersectionObserver.observedCount()).toBeGreaterThan(0));
});

test('once the end is in view the chapter reports 100 at once, a single time', async () => {
  const { emitted } = renderChapter();
  await waitFor(() => expect(FakeIntersectionObserver.observedCount()).toBeGreaterThan(0));

  FakeIntersectionObserver.show();
  FakeIntersectionObserver.show();

  await waitFor(() => expect(progressOf(emitted)).toEqual([100]));
});

test('an end out of view reports nothing, and no percent in between', async () => {
  const { emitted } = renderChapter();
  await waitFor(() => expect(FakeIntersectionObserver.observedCount()).toBeGreaterThan(0));

  FakeIntersectionObserver.show(false);

  expect(progressOf(emitted)).toEqual([]);
});

test('the hint to scroll to the end shows until the chapter is done', async () => {
  const { rerender } = renderChapter();

  expect(screen.getByText(t('learning.player.text.hint'))).toBeInTheDocument();

  await rerender({ text, completed: true });

  expect(screen.queryByText(t('learning.player.text.hint'))).toBeNull();
});

test('the hint is in English once switched', () => {
  setLanguage('en-GB', { remember: false });
  renderChapter();

  expect(screen.getByText('Scroll to the end to complete the chapter.')).toBeInTheDocument();
});
