import js from '@eslint/js';
import vueI18n from '@intlify/eslint-plugin-vue-i18n';
import { defineConfigWithVueTs, vueTsConfigs } from '@vue/eslint-config-typescript';
import pluginVue from 'eslint-plugin-vue';
import globals from 'globals';

/**
 * Where @intlify/vue-i18n/no-raw-text looks besides a template's text: the attributes a reader hears or sees, on any
 * element. Text of punctuation, digits and spaces alone is no language.
 */
const rawTextOptions = {
  attributes: {
    '/.+/': [
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
    ],
  },
  ignorePattern: '^[-–—·…/:()#&|+%\\d\\s]+$',
};

/** Each chosen library bans its alternatives, in every file. */
const libraryBans = [
  {
    group: ['unplugin-vue-router', 'unplugin-vue-router/*'],
    message: 'Routes are declared in src/router.ts and src/modules/*/routes.ts (docs/adr).',
  },
  {
    group: ['vuex', 'mobx', 'mobx-*', 'swrv', '@apollo/*', '@vue/apollo-*', '@pinia/colada'],
    message: 'Server data: the generated TanStack Query hooks; client state: Pinia stores (docs/adr).',
  },
  {
    group: [
      'primevue',
      'vuetify',
      'quasar',
      'element-plus',
      'ant-design-vue',
      'naive-ui',
      '@headlessui/vue',
      'radix-vue',
    ],
    message: 'Use the shadcn-vue components in src/components/ui (docs/adr).',
  },
  {
    group: ['@formkit/*', '@tanstack/vue-form', '@vee-validate/zod'],
    message: 'Use vee-validate with toTypedSchema from src/forms/toTypedSchema (docs/adr).',
  },
  { group: ['yup', 'joi', 'valibot'], message: 'Use zod (docs/adr).' },
  { group: ['ag-grid-*'], message: 'Use DataTable from src/components/ui (docs/adr).' },
  {
    group: ['chart.js', 'vue-chartjs'],
    message: 'Use DataChart from src/components/DataChart.vue (docs/adr).',
  },
];

/** vue-sonner's toast gives a toast no role: every toast goes through notify (src/lib/notify.ts). */
const toastBan = {
  name: 'vue-sonner',
  importNames: ['toast'],
  message: 'Report with notify.success or notify.failure from @/lib/notify, which gives the toast its role.',
};

export default defineConfigWithVueTs(
  {
    ignores: [
      // Build output, then the files a generator writes.
      'dist',
      'src/api/generated.ts',
    ],
  },
  js.configs.recommended,
  pluginVue.configs['flat/essential'],
  vueTsConfigs.strict,
  {
    files: ['**/*.{ts,vue}'],
    languageOptions: { globals: globals.browser },
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: libraryBans,
        },
      ],
    },
  },
  {
    // A test waits for a condition, not a count: a fixed number of yields passes on a fast machine and fails on a
    // loaded CI runner. Necessary, not sufficient; docs/adr says what these selectors cannot see.
    files: ['**/*.test.ts'],
    rules: {
      'no-restricted-syntax': [
        'error',
        {
          selector:
            "ForStatement[test] AwaitExpression > NewExpression[callee.name='Promise'], " +
            "ForOfStatement AwaitExpression > NewExpression[callee.name='Promise'], " +
            "ForInStatement AwaitExpression > NewExpression[callee.name='Promise']",
          message:
            'A fixed count of timer yields is not a bound on work that advances in another phase of the event loop. ' +
            'Wait for the condition: findBy* or waitFor. To drive fake timers, use vi.advanceTimersByTimeAsync.',
        },
        {
          selector:
            ':matches(' +
            "ExpressionStatement[expression.type='AwaitExpression'][expression.argument.callee.name='Promise'], " +
            "ExpressionStatement[expression.type='AwaitExpression'][expression.argument.callee.name=/^(settle|sleep|delay|wait|pause|tick|nextTick|flushPromises|setTimeout)$/], " +
            "ExpressionStatement[expression.type='AwaitExpression'][expression.argument.callee.name='act'][expression.argument.arguments.0.body.body.length=0]" +
            ") + ExpressionStatement:has(CallExpression[callee.name='expect'])",
          message:
            'A settle may not immediately precede an assertion: it assumes the work finished in time. ' +
            'Wait for the condition: findBy* or waitFor.',
        },
      ],
    },
  },
  {
    // Every animation runs only without prefers-reduced-motion: reduce (docs/adr). An animate-* or transition* class
    // carries motion-safe: (or motion-reduce:), whether it is in a script's string or in a template.
    files: ['src/**/*.{ts,vue}'],
    ignores: ['**/*.test.*'],
    plugins: {
      motion: {
        rules: {
          'respect-reduced-motion': {
            meta: { type: 'problem' },
            create(context) {
              const unguarded =
                /(?<=^|\s)((?!\S*motion-(?:safe|reduce):)(?:\S*:)?(?:animate-(?!none(?:\s|$))\S+|transition(?:-(?!none(?:\s|$))\S+)?))(?=\s|$)/g;
              const check = (node, text) => {
                for (const [, name] of text.matchAll(unguarded)) {
                  context.report({
                    node,
                    message: `'${name}' animates whatever the reader's motion settings: write motion-safe:${name}.`,
                  });
                }
              };
              const script = {
                Literal: (node) => typeof node.value === 'string' && check(node, node.value),
                TemplateElement: (node) => check(node, node.value.raw),
              };
              // A .vue file's template is not the script's tree: its parser visits the template's attributes apart.
              const template = context.sourceCode.parserServices?.defineTemplateBodyVisitor;
              return template ? template({ ...script, VLiteral: (node) => check(node, node.value) }, script) : script;
            },
          },
        },
      },
    },
    rules: { 'motion/respect-reduced-motion': 'error' },
  },
  {
    // Every text a user sees comes from a key in both catalogs (docs/adr/0007): a raw text in a template, or in an
    // attribute a reader hears or sees, fails. Never turn it off: src/i18n/i18nGuard.test.ts fails the tests then.
    // A comment in a component cannot turn it off either: no eslint-disable, with or without a rule list, and no config
    // comment has an effect in one. noInlineConfig covers the script's comments; a template's <!-- eslint-disable -->
    // is eslint-plugin-vue's own, read by vue/comment-directive, which is off here so that it has no effect either.
    files: ['src/**/*.vue'],
    ignores: ['**/*.test.*'],
    linterOptions: { noInlineConfig: true },
    plugins: { '@intlify/vue-i18n': vueI18n },
    settings: { 'vue-i18n': { messageSyntaxVersion: '^11.0.0' } },
    rules: {
      '@intlify/vue-i18n/no-raw-text': ['error', rawTextOptions],
      'vue/comment-directive': 'off',
    },
  },
  {
    // HTML put on the page runs whatever it holds: an editor's Markdown reaches the page only through MarkdownView,
    // which renders it sanitised (src/lib/markdown.ts). src/lib/noVHtml.test.ts keeps this from being turned off.
    files: ['src/**/*.vue'],
    rules: { 'vue/no-v-html': 'error' },
  },
  {
    files: ['src/components/MarkdownView.vue'],
    rules: { 'vue/no-v-html': 'off' },
  },
  {
    // A toast's text and a formatted date or number come from the catalogs and their formats too (docs/adr/0007).
    files: ['src/**/*.{ts,vue}'],
    ignores: ['**/*.test.*'],
    rules: {
      'no-restricted-syntax': [
        'error',
        {
          selector: "CallExpression[callee.object.name='notify'] > :matches(Literal, TemplateLiteral)",
          message: 'notify takes t(...)',
        },
        {
          selector:
            "CallExpression[callee.property.name=/^toLocale(Date|Time)?String$/], NewExpression[callee.object.name='Intl'], " +
            "CallExpression[callee.object.name='Intl']",
          message: 'format with d()/n() (src/i18n/formats.ts)',
        },
      ],
      // The library bans stay: a rule's options in a later block replace the earlier ones whole.
      'no-restricted-imports': ['error', { patterns: libraryBans, paths: [toastBan] }],
    },
  },
  {
    // notify is the one place that calls vue-sonner's toast.
    files: ['src/lib/notify.ts'],
    rules: { 'no-restricted-imports': ['error', { patterns: libraryBans }] },
  },
);
