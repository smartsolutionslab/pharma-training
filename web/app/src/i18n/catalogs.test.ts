import { describe, expect, test } from 'vitest';
import { de } from './de';
import { en } from './en';

/** Every message of a catalog under its dotted key path (app.name, glossary.jobFunction.Pta, ...). */
function flatten(catalog: object, prefix = ''): Map<string, unknown> {
  const messages = new Map<string, unknown>();
  for (const [key, value] of Object.entries(catalog)) {
    const path = prefix === '' ? key : `${prefix}.${key}`;
    if (typeof value === 'object' && value !== null) {
      for (const [nested, message] of flatten(value, path)) {
        messages.set(nested, message);
      }
    } else {
      messages.set(path, value);
    }
  }
  return messages;
}

/** The named placeholders of a message: {name}, {module}; a literal such as {'@'} is none. */
function placeholders(message: unknown): string[] {
  const text = typeof message === 'string' ? message : '';
  return [...new Set([...text.matchAll(/\{\s*([A-Za-z_]\w*)\s*\}/g)].map(([, name]) => name))].sort();
}

const catalogs = { de: flatten(de), en: flatten(en) };
const keys = [...new Set([...catalogs.de.keys(), ...catalogs.en.keys()])].sort();

test('both catalogs have the same keys', () => {
  const onlyInGerman = [...catalogs.de.keys()].filter((key) => !catalogs.en.has(key)).sort();
  const onlyInEnglish = [...catalogs.en.keys()].filter((key) => !catalogs.de.has(key)).sort();

  expect({ onlyInGerman, onlyInEnglish }).toEqual({ onlyInGerman: [], onlyInEnglish: [] });
});

describe.each(Object.entries(catalogs))('the %s catalog', (_, messages) => {
  test('has messages', () => {
    expect(messages.size).toBeGreaterThan(0);
  });

  test('has no empty message', () => {
    const empty = [...messages].filter(([, message]) => typeof message !== 'string' || message.trim() === '');

    expect(empty.map(([key]) => key)).toEqual([]);
  });
});

test('a message has the same placeholders in German and English', () => {
  const differing = keys
    .filter((key) => catalogs.de.has(key) && catalogs.en.has(key))
    .map((key) => ({ key, de: placeholders(catalogs.de.get(key)), en: placeholders(catalogs.en.get(key)) }))
    .filter((message) => message.de.join() !== message.en.join());

  expect(differing).toEqual([]);
});

// Names that read the same in every language: the brand, and each language in its own words on the switch.
test.each([
  ['app.name', 'pharma-training'],
  ['language.de', 'Deutsch'],
  ['language.en', 'English'],
])('%s reads %s in both catalogs', (key, text) => {
  expect([catalogs.de.get(key), catalogs.en.get(key)]).toEqual([text, text]);
});
