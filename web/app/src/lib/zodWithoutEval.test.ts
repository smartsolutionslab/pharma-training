import { expect, test } from 'vitest';
import { z } from 'zod';
import main from '@/main.ts?raw';

// The edge's Content-Security-Policy has no 'unsafe-eval' (docs/adr/0005): Zod must never probe for eval with
// Function(''), so it is told to parse without it before any other module of the app can use it.

test('main.ts imports zodWithoutEval before anything else', () => {
  const firstImport = main.match(/^\s*import\s[^;]*;/m)?.[0] ?? '';

  expect(firstImport).toMatch(/^\s*import\s+['"](?:@|\.)\/lib\/zodWithoutEval['"];$/);
});

test('zodWithoutEval tells Zod to parse without eval', async () => {
  expect(z.config().jitless).not.toBe(true);

  await import('./zodWithoutEval');

  expect(z.config().jitless).toBe(true);
});
