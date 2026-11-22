import { describe, expect, test } from 'vitest';
import { renderMarkdown } from './markdown';

// An editor's Markdown, as the app renders it for a reader (spec 2.5): markdown-it without raw HTML, links it refuses
// for a dangerous scheme, headings shifted below the page's own, and DOMPurify over the result. What it answers is HTML
// that MarkdownView puts on the page with v-html, so whatever an editor writes must not run in a reader's browser.

/** The rendered HTML as a document fragment, to ask what elements and attributes it holds. */
function rendered(markdown: string, options?: { headingOffset?: number }) {
  const template = document.createElement('template');
  template.innerHTML = renderMarkdown(markdown, options);
  return template.content;
}

/** Every attribute of every element, as name="value". */
const attributesOf = (fragment: DocumentFragment) =>
  [...fragment.querySelectorAll('*')].flatMap((element) =>
    [...element.attributes].map((attribute) => `${attribute.name}="${attribute.value}"`),
  );

describe('raw HTML is text, never markup', () => {
  test('a script tag renders as its text, and no script element', () => {
    const fragment = rendered('<script>alert(1)</script>');

    expect(fragment.querySelector('script')).toBeNull();
    expect(fragment.textContent).toContain('<script>alert(1)</script>');
  });

  test('an image with an event handler has no img element and no handler', () => {
    const fragment = rendered('<img src=x onerror=alert(1)>');

    expect(fragment.querySelector('img')).toBeNull();
    expect(attributesOf(fragment).filter((attribute) => attribute.startsWith('on'))).toEqual([]);
    expect(fragment.textContent).toContain('<img src=x onerror=alert(1)>');
  });

  test.each([
    ['<b>fett</b>', 'b'],
    ['<iframe src="https://example.org"></iframe>', 'iframe'],
    ['<a href="javascript:alert(1)">x</a>', 'a'],
    ['<div onclick="alert(1)">x</div>', 'div'],
    ['<svg onload=alert(1)></svg>', 'svg'],
    ['<style>body { display: none }</style>', 'style'],
  ])('%s is shown as text, with no %s element', (markdown, element) => {
    const fragment = rendered(markdown);

    expect(fragment.querySelector(element)).toBeNull();
    expect(fragment.textContent).toContain(markdown);
  });

  test('a raw HTML block inside a list is text too', () => {
    const fragment = rendered('- <img src=x onerror=alert(1)>\n- zwei');

    expect(fragment.querySelectorAll('li')).toHaveLength(2);
    expect(fragment.querySelector('img')).toBeNull();
  });
});

describe('links and images with a dangerous scheme have no target', () => {
  test.each([
    ['[x](javascript:alert(1))'],
    ['[x](JavaScript:alert(1))'],
    ['[x](  javascript:alert(1))'],
    ['[x](java\tscript:alert(1))'],
    ['[x](vbscript:msgbox(1))'],
    ['[x](file:///etc/passwd)'],
    ['[x](data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==)'],
    ['<javascript:alert(1)>'],
    ['[x][ref]\n\n[ref]: javascript:alert(1)'],
  ])('%s has no href with a script or a document in it', (markdown) => {
    const fragment = rendered(markdown);

    const hrefs = [...fragment.querySelectorAll('[href]')].map((element) => element.getAttribute('href') ?? '');
    expect(hrefs.filter((href) => /^\s*(javascript|vbscript|file|data):/i.test(href.replace(/\s/g, '')))).toEqual([]);
  });

  test('an image whose source is a script has no such source', () => {
    const fragment = rendered('![Bild](javascript:alert(1))');

    const sources = [...fragment.querySelectorAll('[src]')].map((element) => element.getAttribute('src') ?? '');
    expect(sources.filter((source) => /^javascript:/i.test(source))).toEqual([]);
  });
});

describe('what Markdown is for still renders', () => {
  test('a link to a web page keeps its target and opens without handing over the app', () => {
    const fragment = rendered('[Leitlinie](https://example.org/leitlinie)');

    const link = fragment.querySelector('a');
    expect(link?.getAttribute('href')).toBe('https://example.org/leitlinie');
    expect(link?.textContent).toBe('Leitlinie');
    expect(link?.getAttribute('rel')).toBe('noopener noreferrer');
  });

  test('strong text is a strong element', () => {
    const fragment = rendered('**b**');

    expect(fragment.querySelector('strong')?.textContent).toBe('b');
  });

  test('lists, code and paragraphs render as such', () => {
    const fragment = rendered('Absatz\n\n1. eins\n2. zwei\n\n`code`');

    expect(fragment.querySelector('p')?.textContent).toBe('Absatz');
    expect([...fragment.querySelectorAll('ol > li')].map((item) => item.textContent)).toEqual(['eins', 'zwei']);
    expect(fragment.querySelector('code')?.textContent).toBe('code');
  });

  test('a plain web address stays text: linkify is off', () => {
    const fragment = rendered('Siehe https://example.org');

    expect(fragment.querySelector('a')).toBeNull();
  });
});

describe('headings sit below the page', () => {
  test('with an offset of 2 a top-level heading is an h3', () => {
    const fragment = rendered('# A', { headingOffset: 2 });

    expect(fragment.querySelector('h1')).toBeNull();
    expect(fragment.querySelector('h3')?.textContent).toBe('A');
  });

  test('with an offset of 2 a second-level heading is an h4', () => {
    expect(rendered('## B', { headingOffset: 2 }).querySelector('h4')?.textContent).toBe('B');
  });

  test('the deepest heading stays an h6', () => {
    const fragment = rendered('###### F', { headingOffset: 2 });

    expect(fragment.querySelector('h6')?.textContent).toBe('F');
    expect(fragment.querySelector('h7, h8')).toBeNull();
  });

  test('a heading shifted past h6 is capped at h6', () => {
    expect(rendered('#### D', { headingOffset: 3 }).querySelector('h6')?.textContent).toBe('D');
  });
});
