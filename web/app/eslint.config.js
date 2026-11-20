import js from '@eslint/js';
import { defineConfigWithVueTs, vueTsConfigs } from '@vue/eslint-config-typescript';
import pluginVue from 'eslint-plugin-vue';
import globals from 'globals';

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
          patterns: [
            // Each chosen library bans its alternatives.
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
          ],
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
);
