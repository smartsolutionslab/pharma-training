import DOMPurify from 'dompurify';
import MarkdownIt from 'markdown-it';

/** How renderMarkdown renders: headingOffset shifts every heading down, so content never adds a second h1. */
export interface MarkdownOptions {
  headingOffset?: number;
}

// Raw HTML is text (html: false), a plain address stays text (linkify: false), and quotes stay as written.
const markdown = new MarkdownIt({ html: false, linkify: false, typographer: false });

const scheme = /^([a-z][a-z\d+.-]*):/i;
const safeSchemes = new Set(['http', 'https', 'mailto']);

// A link or image takes a relative address or one of the safe schemes; any other (javascript:, vbscript:, file:,
// data:, ...) leaves it as text.
markdown.validateLink = (url) => {
  const found = scheme.exec(url.trim());
  return found === null || safeSchemes.has((found[1] ?? '').toLowerCase());
};

/** The deepest heading HTML has. */
const deepest = 6;

/**
 * An editor's Markdown as HTML that is safe to put on the page: markdown-it without raw HTML, links only to safe
 * schemes and opened without handing over the app, headings shifted by headingOffset (capped at h6), and DOMPurify
 * over the result in case the parser lets something through.
 */
export function renderMarkdown(text: string, { headingOffset = 0 }: MarkdownOptions = {}): string {
  const env = {};
  const tokens = markdown.parse(text, env);
  for (const token of tokens) {
    if (token.type === 'heading_open' || token.type === 'heading_close') {
      const level = Number(token.tag.slice(1)) + headingOffset;
      token.tag = `h${Math.min(Math.max(level, 1), deepest)}`;
    }
    for (const child of token.children ?? []) {
      if (child.type === 'link_open') {
        child.attrSet('rel', 'noopener noreferrer');
      }
    }
  }
  const html = markdown.renderer.render(tokens, markdown.options, env);
  return DOMPurify.sanitize(html, { USE_PROFILES: { html: true } });
}

/**
 * The level of the text's shallowest heading (1 for `#`), or undefined for a text without one: a page lifts the text's
 * headings by it, so the text's top heading sits right below the page's own and no heading level is skipped.
 */
export function shallowestHeading(text: string): number | undefined {
  const levels = markdown
    .parse(text, {})
    .filter((token) => token.type === 'heading_open')
    .map((token) => Number(token.tag.slice(1)));
  return levels.length === 0 ? undefined : Math.min(...levels);
}
