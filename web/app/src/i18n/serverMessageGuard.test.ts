import { describe, expect, test } from 'vitest';

// ApiError.serverMessage is the host's English fallback for a code the catalog lacks (#147, ADR 0007): only the helper,
// src/i18n/errorText.ts, reads it, so a refusal is shown by its code everywhere else. baseApi.ts sets it.
const sources = import.meta.glob<string>('../**/*.{ts,vue}', { query: '?raw', import: 'default', eager: true });

const readers = new Set(['src/api/baseApi.ts', 'src/i18n/errorText.ts']);

/** The file's code without its comments: a comment may name serverMessage. */
function code(source: string): string {
  return source
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/(^|[^:\\])\/\/.*$/gm, '$1');
}

/**
 * Whether the code reads serverMessage: as a property (error.serverMessage, state?.serverMessage, x['serverMessage']) or
 * by destructuring ({ serverMessage } = error). Naming it as a key of an object it builds, codeMessage's
 * { serverMessage: refusal.message }, is no read.
 */
function readsServerMessage(source: string): boolean {
  const text = code(source);
  return (
    /\??\.\s*serverMessage\b/.test(text) ||
    /\[\s*['"`]serverMessage['"`]\s*\]/.test(text) ||
    /\{[^{}]*\bserverMessage\b(?!\s*:)[^{}]*\}\s*=[^=>]/.test(text)
  );
}

/** src-relative paths of every file under src that is no test, with its source. */
const files = Object.entries(sources)
  // import.meta.glob names files of this test's own folder ./, all others ../: both become src/ paths.
  .map(([path, source]) => [path.replace(/^\.\.\//, 'src/').replace(/^\.\//, 'src/i18n/'), source] as const)
  .filter(([path]) => !/\.test\.ts$/.test(path));

test('only the helper reads ApiError.serverMessage', () => {
  const reading = files
    .filter(([path, source]) => readsServerMessage(source) && !readers.has(path))
    .map(([path]) => path);

  expect(reading, 'files reading serverMessage outside src/i18n/errorText.ts').toEqual([]);
});

test('the helper reads it', () => {
  const helper = files.find(([path]) => path === 'src/i18n/errorText.ts');

  expect(helper, 'src/i18n/errorText.ts').toBeDefined();
  expect(readsServerMessage(helper?.[1] ?? '')).toBe(true);
});

// The detector itself, on code it must and must not count as a read of serverMessage.
describe('readsServerMessage', () => {
  test.each([
    ['a property read', 'const words = error.serverMessage;'],
    ['an optional property read', 'const words = state?.serverMessage;'],
    ['an element read', "const words = error['serverMessage'];"],
    ['destructuring', 'const { serverMessage } = error;'],
    ['renamed destructuring', 'const { serverMessage: words } = error;'],
    ['parameter destructuring', 'const show = ({ serverMessage }: ApiError) => serverMessage;'],
    ['renamed parameter destructuring', 'const show = ({ serverMessage: words }: ApiError) => words;'],
    ['destructuring assignment', '({ serverMessage: words } = error);'],
    [
      'a read after a /* inside a string',
      "const routes = import.meta.glob('./*/routes.ts');\nconst words = error.serverMessage;\n/** A comment. */",
    ],
    ['a read after a // inside a string', "const path = 'a//b'; const words = error.serverMessage;"],
    ['a read in a template', '<template>\n  <p>{{ error.serverMessage }}</p>\n</template>\n'],
    [
      "a read in a component's script",
      '<script setup lang="ts">\nconst words = error.serverMessage;\n</script>\n\n<template>\n  <p />\n</template>\n',
    ],
  ])('counts %s', (_name, source) => {
    expect(readsServerMessage(source)).toBe(true);
  });

  test.each([
    ['a key of an object literal it builds', 'codeMessage(code, fallback, { serverMessage: refusal.message });'],
    ['a line comment', '// error.serverMessage\nconst words = 1;'],
    ['a block comment', '/* error.serverMessage */\nconst words = 1;'],
    ['a doc comment', '/** Reads error.serverMessage elsewhere. */\nconst words = 1;'],
    ['a string', "const words = 'error.serverMessage';"],
    ['a comment in a template', '<template>\n  <!-- error.serverMessage -->\n  <p />\n</template>\n'],
  ])('does not count %s', (_name, source) => {
    expect(readsServerMessage(source)).toBe(false);
  });
});
