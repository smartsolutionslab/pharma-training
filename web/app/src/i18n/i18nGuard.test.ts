import { ESLint } from 'eslint';
import { describe, expect, test } from 'vitest';

// Every text a user sees comes from a key (ADR 0007): the lint rule @intlify/vue-i18n/no-raw-text fails a raw text in a
// template. These tests keep the rule from being turned off, in the config or in a file.
const configs = import.meta.glob<string>('../../eslint.config.js', { query: '?raw', import: 'default', eager: true });
const sources = import.meta.glob<string>('../**/*.{ts,vue}', { query: '?raw', import: 'default', eager: true });

// Built from pieces, so this file does not name what it looks for.
const directive = ['eslint', 'disable'].join('-');
const rules = [['@intlify', 'vue-i18n'].join('/'), ['no', 'raw', 'text'].join('-')];

test('the lint config fails a raw text in a template as an error', () => {
  const config = Object.values(configs)[0] ?? '';

  const setting = `'${rules[0]}/${rules[1]}': ['error'`;

  expect(config.includes(setting), `eslint.config.js sets ${setting}`).toBe(true);
});

test('no file under src turns the raw-text rule off', () => {
  const disabling = Object.entries(sources).flatMap(([path, source]) =>
    source
      .split('\n')
      .map((line, index) => ({ line, number: index + 1 }))
      .filter(({ line }) => line.includes(directive) && rules.some((rule) => line.includes(rule)))
      .map(({ line, number }) => `${path}:${number}: ${line.trim()}`),
  );

  expect(disabling, `${directive} comments naming ${rules.join(' or ')}`).toEqual([]);
});

// The config as ESLint resolves it for a file, whatever the order and the overrides of its blocks: what the text
// checks above cannot see.
// Vitest runs in web/app, where eslint.config.js is (pnpm test, from web or from web/app).
const eslint = new ESLint({ cwd: process.cwd() });
const rawText = rules.join('/');

/** Where the raw-text rule looks besides a template's text, on any element. */
const heardOrSeen = [
  'title',
  'aria-label',
  'aria-description',
  'aria-placeholder',
  'aria-roledescription',
  'aria-valuetext',
  'placeholder',
  'alt',
  'label',
  'description',
  'heading',
  // vue-sonner's Toaster names its region with it (App.vue).
  'container-aria-label',
];
const ignorePattern = '^[-\u2013\u2014\u00b7\u2026/:()#&|+%\\d\\s]+$';

/** What ESLint reports for the source, as if it were the file at the path (relative to web/app). */
async function lint(path: string, source: string) {
  const [result] = await eslint.lintText(source, { filePath: path });
  return result?.messages ?? [];
}

describe('the resolved lint config', () => {
  test.each(['src/App.vue', 'src/modules/catalog/CatalogPage.vue', 'src/components/ui/dialog/DialogContent.vue'])(
    'fails a raw text in %s as an error, in the attributes a reader hears or sees',
    async (file) => {
      const config = (await eslint.calculateConfigForFile(file)) as {
        rules?: Record<string, unknown>;
        linterOptions?: { noInlineConfig?: boolean };
      };

      expect(config.rules?.[rawText]).toEqual([
        2,
        {
          attributes: { '/.+/': expect.arrayContaining(heardOrSeen) as unknown },
          ignorePattern,
        },
      ]);
      // A comment in a component cannot turn it off: not eslint-disable without a rule list, not a config comment.
      expect(config.linterOptions?.noInlineConfig).toBe(true);
    },
  );

  test('leaves a test file alone', async () => {
    const config = (await eslint.calculateConfigForFile('src/modules/catalog/CatalogPageSamples.test.ts')) as {
      rules?: Record<string, unknown>;
    };

    expect(config.rules?.[rawText]).toBeUndefined();
  });
});

describe('the raw-text rule, run on a component', () => {
  const component = 'src/modules/catalog/GuardProbe.vue';
  const rawTextIn = async (source: string) =>
    (await lint(component, source)).filter((message) => message.ruleId === rawText).map((message) => message.line);

  test('reports a raw text in a template', async () => {
    expect(await rawTextIn('<template>\n  <p>Hallo Welt</p>\n</template>\n')).toEqual([2]);
  });

  test('reports a raw region name on the Toaster', async () => {
    expect(await rawTextIn('<template>\n  <Toaster container-aria-label="Notifications" />\n</template>\n')).toEqual([
      2,
    ]);
  });

  test.each([
    ['eslint-disable without a rule list', '<!-- eslint-disable -->'],
    ['eslint-disable-next-line without a rule list', '<!-- eslint-disable-next-line -->'],
    ['a config comment turning the rule off', `<!-- eslint ${rawText}: off -->`],
  ])('reports a raw text behind %s', async (_comment, comment) => {
    expect(await rawTextIn(`<template>\n  ${comment}\n  <p>Hallo Welt</p>\n</template>\n`)).toEqual([3]);
  });
});

// Toasts go through notify (src/lib/notify.ts), which gives each a role; dates and numbers through d() and n().
describe('the bans in src', () => {
  const banned = async (path: string, source: string) =>
    (await lint(path, source))
      .filter((message) => message.ruleId === 'no-restricted-syntax' || message.ruleId === 'no-restricted-imports')
      .map((message) => message.line);

  test.each([
    [
      'Intl.NumberFormat',
      'src/modules/catalog/formatting.ts',
      "export const amount = Intl.NumberFormat('de-DE').format(1312);\n",
    ],
    [
      'Intl.DateTimeFormat',
      'src/modules/catalog/formatting.ts',
      "export const day = Intl.DateTimeFormat('de-DE').format(0);\n",
    ],
    [
      'Intl.NumberFormat',
      'src/modules/catalog/GuardProbe.vue',
      '<script setup lang="ts">\nconst amount = Intl.NumberFormat(\'de-DE\').format(1312);\n</script>\n\n<template>\n  <p>{{ amount }}</p>\n</template>\n',
    ],
  ])('%s without new is reported in %s', async (_call, path, source) => {
    expect((await banned(path, source)).length).toBeGreaterThan(0);
  });

  test.each([
    [
      'src/modules/catalog/report.ts',
      "import { toast } from 'vue-sonner';\n\nexport const report = () => toast('x');\n",
      1,
    ],
    [
      'src/modules/catalog/GuardProbe.vue',
      "<script setup lang=\"ts\">\nimport { toast } from 'vue-sonner';\n\ntoast('x');\n</script>\n\n<template>\n  <p>{{ 1 }}</p>\n</template>\n",
      2,
    ],
  ])("%s may not import vue-sonner's toast", async (path, source, line) => {
    expect(await banned(path, source)).toEqual([line]);
  });

  test("notify itself may import vue-sonner's toast", async () => {
    const notify = sources['../lib/notify.ts'] ?? '';
    expect(notify).toContain("from 'vue-sonner'");

    expect(await banned('src/lib/notify.ts', notify)).toEqual([]);
  });

  // A ban added for src must not replace the library bans the config already has for every file.
  test('a src file still may not import a library another one was chosen over', async () => {
    expect(
      await banned(
        'src/modules/catalog/store.ts',
        "import { createStore } from 'vuex';\n\nexport const store = createStore({});\n",
      ),
    ).toEqual([1]);
  });
});
