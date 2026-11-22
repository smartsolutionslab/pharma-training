import { ESLint } from 'eslint';
import { describe, expect, test } from 'vitest';

// HTML put on the page with v-html runs whatever it holds: an editor's Markdown reaches it only through MarkdownView
// (src/components/MarkdownView.vue), which renders it with renderMarkdown, sanitised. The lint rule vue/no-v-html fails
// a v-html anywhere else in src (spec 2.5). These tests keep the rule from being turned off, or widened past that file.
// Like i18nGuard.test.ts, they ask ESLint for the config it resolves, whatever the order and overrides of its blocks.

// Vitest runs in web/app, where eslint.config.js is (pnpm test, from web or from web/app).
const eslint = new ESLint({ cwd: process.cwd() });
const sources = import.meta.glob<string>('../**/*.vue', { query: '?raw', import: 'default', eager: true });

// Built from pieces, so this file does not name what it looks for.
const rule = ['vue', ['no', 'v', 'html'].join('-')].join('/');
const directive = ['v', 'html'].join('-');
const markdownView = 'src/components/MarkdownView.vue';

/** The rule's severity as ESLint resolves it for the file (0 off, 1 warn, 2 error), 0 when it is not set. */
async function severityFor(file: string) {
  const config = (await eslint.calculateConfigForFile(file)) as { rules?: Record<string, unknown> };
  const setting = config.rules?.[rule];
  const severity = Array.isArray(setting) ? (setting[0] as unknown) : setting;
  return severity === undefined ? 0 : severity;
}

/** The lines on which the rule reports the source, linted as if it were the file at the path (relative to web/app). */
async function reported(path: string, source: string) {
  const [result] = await eslint.lintText(source, { filePath: path });
  return (result?.messages ?? []).filter((message) => message.ruleId === rule).map((message) => message.line);
}

const probe =
  '<script setup lang="ts">\nconst html = \'\';\n</script>\n\n<template>\n  <div v-html="html" />\n</template>\n';

describe('the resolved lint config', () => {
  test.each([
    'src/App.vue',
    'src/modules/editorial/CourseVersionPage.vue',
    'src/modules/editorial/ChapterList.vue',
    'src/components/DataChart.vue',
    'src/components/ui/dialog/DialogContent.vue',
    'src/components/MarkdownViewer.vue',
  ])('fails a v-html in %s as an error', async (file) => {
    expect(await severityFor(file)).toBe(2);
  });

  test('allows v-html in MarkdownView alone, not in the components beside it', async () => {
    expect(await severityFor('src/components/MarkdownViewProbe.vue')).toBe(2);
    expect(await severityFor(markdownView)).toBe(0);
  });
});

describe('the rule, run on a component', () => {
  test('reports a v-html in a component of a module', async () => {
    expect(await reported('src/modules/editorial/GuardProbe.vue', probe)).toEqual([6]);
  });

  test('reports a v-html in a component beside MarkdownView', async () => {
    expect(await reported('src/components/GuardProbe.vue', probe)).toEqual([6]);
  });

  test('does not report the v-html of MarkdownView, while it reports the same source anywhere else', async () => {
    expect(await reported('src/components/ui/GuardProbe.vue', probe)).toEqual([6]);
    expect(await reported(markdownView, probe)).toEqual([]);
  });

  // A comment in a component cannot turn it off (noInlineConfig, vue/comment-directive off: ADR 0007).
  test.each([
    ['eslint-disable without a rule list', '<!-- eslint-disable -->'],
    ['eslint-disable-next-line naming the rule', `<!-- eslint-disable-next-line ${rule} -->`],
    ['a config comment turning the rule off', `<!-- eslint ${rule}: off -->`],
  ])('reports a v-html behind %s', async (_comment, comment) => {
    const source = probe.replace('  <div', `  ${comment}\n  <div`);

    expect(await reported('src/modules/editorial/GuardProbe.vue', source)).toEqual([7]);
  });
});

test('the only v-html under src is the one in MarkdownView', () => {
  const using = Object.entries(sources)
    .filter(([, source]) => source.includes(directive))
    .map(([path]) => path);

  expect(using).toEqual(['../components/MarkdownView.vue']);
});
