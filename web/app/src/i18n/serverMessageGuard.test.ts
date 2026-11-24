import { expect, test } from 'vitest';

// ApiError.serverMessage is the host's English fallback for a code the catalog lacks (#147, ADR 0007): only the helper,
// src/i18n/errorText.ts, reads it, so a refusal is shown by its code everywhere else. baseApi.ts sets it.
const sources = import.meta.glob<string>('../**/*.{ts,vue}', { query: '?raw', import: 'default', eager: true });

const readers = new Set(['src/api/baseApi.ts', 'src/i18n/errorText.ts']);

// The call sites that still read it, until #147's page tasks (T3 to T7) move each to the helper and take it off this
// list. A file on it that no longer reads serverMessage fails below, so the list only shrinks; once it is empty it goes.
const pending = new Set([
  // T3, the editorial test page
  'src/modules/editorial/useTestBlueprint.ts',
  'src/modules/editorial/TestBlueprintSection.vue',
  'src/modules/editorial/useQuestionMutations.ts',
  'src/modules/editorial/CourseTestPage.vue',
  // T4, the course version and editorial pages
  'src/modules/editorial/useChapterMutations.ts',
  'src/modules/editorial/useReorderChapters.ts',
  'src/modules/editorial/useDraftNextVersion.ts',
  'src/modules/editorial/useCreateCourse.ts',
  'src/modules/editorial/CourseVersionPage.vue',
  'src/modules/editorial/EditorialPage.vue',
  // T5, the workflow
  'src/modules/editorial/workflowErrors.ts',
  // T6, organisation
  'src/modules/organisation/teamErrors.ts',
  'src/modules/organisation/useRegisterPharmacy.ts',
  'src/modules/organisation/useAcceptInvitation.ts',
  'src/modules/organisation/AcceptInvitationPage.vue',
  // T7, compliance
  'src/modules/compliance/matrixErrors.ts',
  'src/modules/compliance/useAssignCourse.ts',
]);

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

test('only the helper and the files still to migrate read ApiError.serverMessage', () => {
  const reading = files
    .filter(([path, source]) => readsServerMessage(source) && !readers.has(path) && !pending.has(path))
    .map(([path]) => path);

  expect(reading, 'files reading serverMessage outside src/i18n/errorText.ts').toEqual([]);
});

test('the helper reads it', () => {
  const helper = files.find(([path]) => path === 'src/i18n/errorText.ts');

  expect(helper, 'src/i18n/errorText.ts').toBeDefined();
  expect(readsServerMessage(helper?.[1] ?? '')).toBe(true);
});

test('every file still to migrate reads it, so a migrated one leaves the list', () => {
  const migrated = [...pending].filter((path) => {
    const source = files.find(([file]) => file === path)?.[1];
    return source === undefined || !readsServerMessage(source);
  });

  expect(migrated, 'files on the pending list that no longer read serverMessage').toEqual([]);
});
