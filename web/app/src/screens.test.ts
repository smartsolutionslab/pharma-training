import { expect, test } from 'vitest';

// Every module screen, <Name>Page.vue, has its axe check beside it in <Name>Page.axe.test.ts (AGENTS.md): add-module
// generates it with a module's page, and a screen written by hand gets one too. An axe assertion in another test does
// not count, since it can be skipped or commented out.
const screens = Object.keys(import.meta.glob('./modules/**/*Page.vue'));
const checks = import.meta.glob<string>('./modules/**/*Page.axe.test.ts', {
  query: '?raw',
  import: 'default',
  eager: true,
});

test('every module screen has an axe check beside it', () => {
  const unchecked = screens.filter(
    (screen) => !checks[screen.replace(/Page\.vue$/, 'Page.axe.test.ts')]?.includes('toHaveNoViolations()'),
  );

  expect(unchecked, 'module screens without <Name>Page.axe.test.ts checking them with axe').toEqual([]);
});

// The app speaks German and English (ADR 0007): each screen's axe check runs in both, since the texts differ and so may
// what axe finds in them.
test('every module screen is checked with axe in each language', () => {
  const monolingual = Object.entries(checks)
    .filter(([, check]) => !check.includes('describe.each(languages)'))
    .map(([check]) => check);

  expect(monolingual, '<Name>Page.axe.test.ts without describe.each(languages)').toEqual([]);
});
