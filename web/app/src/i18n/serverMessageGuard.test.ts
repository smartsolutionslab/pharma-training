import ts from 'typescript';
import { describe, expect, test } from 'vitest';

// ApiError.serverMessage is the host's English fallback for a code the catalog lacks (#147, ADR 0007): only the helper,
// src/i18n/errorText.ts, reads it, so a refusal is shown by its code everywhere else. baseApi.ts sets it.
const sources = import.meta.glob<string>('../**/*.{ts,vue}', { query: '?raw', import: 'default', eager: true });

const readers = new Set(['src/api/baseApi.ts', 'src/i18n/errorText.ts']);

/** A .vue file: its script blocks are TypeScript, the rest is markup. */
const scriptBlocks = /<script\b[^>]*>([\s\S]*?)<\/script>/g;
const isComponent = (source: string) => /^\s*(<!--[\s\S]*?-->\s*)*<(script|template)\b/.test(source);

/** Whether the object or array literal is a pattern being assigned to: ({ serverMessage: words } = error). */
function isAssignedTo(literal: ts.Node): boolean {
  let node = literal;
  while (
    ts.isPropertyAssignment(node.parent) ||
    ts.isShorthandPropertyAssignment(node.parent) ||
    ts.isSpreadAssignment(node.parent) ||
    ts.isSpreadElement(node.parent) ||
    ts.isObjectLiteralExpression(node.parent) ||
    ts.isArrayLiteralExpression(node.parent) ||
    ts.isParenthesizedExpression(node.parent)
  ) {
    node = node.parent;
  }
  const { parent } = node;
  return (
    (ts.isBinaryExpression(parent) &&
      parent.operatorToken.kind === ts.SyntaxKind.EqualsToken &&
      parent.left === node) ||
    ((ts.isForOfStatement(parent) || ts.isForInStatement(parent)) && parent.initializer === node)
  );
}

/** Whether the name is a key written in an object literal the code builds: { serverMessage: refusal.message }. */
function isWrittenKey(name: ts.Node): boolean {
  const { parent } = name;
  return (
    ts.isPropertyAssignment(parent) &&
    parent.name === name &&
    ts.isObjectLiteralExpression(parent.parent) &&
    !isAssignedTo(parent.parent)
  );
}

/**
 * Whether the node names serverMessage as a read: any identifier serverMessage but a key written in an object
 * literal (destructuring, renamed or not, reads it), and the string 'serverMessage' as an element or a pattern's key.
 */
function isRead(node: ts.Node): boolean {
  if (ts.isIdentifier(node)) {
    return node.text === 'serverMessage' && !isWrittenKey(node);
  }
  if (ts.isStringLiteralLike(node) && node.text === 'serverMessage') {
    const { parent } = node;
    return (
      (ts.isElementAccessExpression(parent) && parent.argumentExpression === node) ||
      (ts.isBindingElement(parent) && parent.propertyName === node) ||
      (ts.isPropertyAssignment(parent) && parent.name === node && !isWrittenKey(node))
    );
  }
  return false;
}

/** Whether TypeScript reads serverMessage. Parsed, not matched: comments and strings are never code. */
function scriptReads(script: string): boolean {
  const file = ts.createSourceFile('script.ts', script, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS);
  const visit = (node: ts.Node): boolean => isRead(node) || (ts.forEachChild(node, visit) ?? false);
  return visit(file);
}

/**
 * Whether the code reads serverMessage. A component's scripts are parsed as TypeScript; its markup, without its
 * comments, reads it wherever it names it, as a template only reads.
 */
function readsServerMessage(source: string): boolean {
  if (!isComponent(source)) {
    return scriptReads(source);
  }
  const scripts = [...source.matchAll(scriptBlocks)].map(([, script]) => script ?? '');
  const markup = source.replace(scriptBlocks, '').replace(/<!--[\s\S]*?-->/g, '');
  return scripts.some(scriptReads) || /\bserverMessage\b/.test(markup);
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
