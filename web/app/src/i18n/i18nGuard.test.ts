import { expect, test } from 'vitest';

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
