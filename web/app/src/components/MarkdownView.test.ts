import { render } from '@testing-library/vue';
import { expect, test } from 'vitest';
import MarkdownView from './MarkdownView.vue';

// The one component that puts HTML on the page (v-html), and only what renderMarkdown (src/lib/markdown.ts) made of the
// Markdown: sanitised, its headings below the page's, styled by the .markdown block in src/index.css.

test('it shows the rendered Markdown inside a .markdown block', () => {
  const { container } = render(MarkdownView, { props: { source: '# Willkommen\n\nDas ist **wichtig**.' } });

  const block = container.querySelector('.markdown');
  expect(block).not.toBeNull();
  expect(block?.querySelector('strong')?.textContent).toBe('wichtig');
  expect(block?.textContent).toContain('Willkommen');
});

test('its headings are shifted by the offset it is given', () => {
  const { container } = render(MarkdownView, { props: { source: '# Willkommen', headingOffset: 2 } });

  expect(container.querySelector('h1')).toBeNull();
  expect(container.querySelector('h3')?.textContent).toBe('Willkommen');
});

test('a script in the Markdown is shown as text and never becomes an element', () => {
  const { container } = render(MarkdownView, {
    props: { source: 'Vorher <script>window.hacked = true</script> nachher' },
  });

  expect(container.querySelector('script')).toBeNull();
  expect(container.textContent).toContain('<script>window.hacked = true</script>');
  expect((window as unknown as { hacked?: boolean }).hacked).toBeUndefined();
});

test('an event handler in raw HTML never reaches the page', () => {
  const { container } = render(MarkdownView, {
    props: { source: '<img src=x onerror="window.hacked = true">\n\n<p onmouseover="alert(1)">x</p>' },
  });

  expect(container.querySelector('img')).toBeNull();
  expect(container.querySelector('[onerror], [onmouseover]')).toBeNull();
});

test('a javascript: link has no such target on the page', () => {
  const { container } = render(MarkdownView, { props: { source: '[Klick](javascript:alert(1))' } });

  const hrefs = [...container.querySelectorAll('[href]')].map((element) => element.getAttribute('href') ?? '');
  expect(hrefs.filter((href) => /^javascript:/i.test(href.trim()))).toEqual([]);
});

test('it renders again when the Markdown changes', async () => {
  const { container, rerender } = render(MarkdownView, { props: { source: '**eins**' } });

  await rerender({ source: '_zwei_' });

  expect(container.querySelector('strong')).toBeNull();
  expect(container.querySelector('em')?.textContent).toBe('zwei');
});
