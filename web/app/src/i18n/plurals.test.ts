import { describe, expect, test } from 'vitest';
import { de } from './de';
import { en } from './en';
import { isDeliberatePlural } from './plurals';

// vue-i18n reads every "|" in a message as a plural's separator. One is deliberate only as the catalogs write a plural:
// two or three choices separated by " | ", the last naming {count}, the number it is chosen by. Any other "|" is a
// slip that would show the reader one half of a sentence (catalogs.test.ts reads each choice as written).

test.each([
  ['Mit dem Kapitel wird eine Frage gelöscht. | Mit dem Kapitel werden {count} Fragen gelöscht.'],
  ['One question about it is deleted with it. | {count} questions about it are deleted with it.'],
  ['Keine Fragen. | Eine Frage. | {count} Fragen.'],
])('"%s" is a deliberate plural', (message) => {
  expect(isDeliberatePlural(message)).toBe(true);
});

test.each([
  ['Speichern', 'no choice at all'],
  ['Speichern | Abbrechen', 'no {count} in the last choice'],
  ['{count} Fragen | gelöscht.', '{count} in the first choice only'],
  ['Eine Frage. | {count} Fragen. | Keine.', '{count} in a choice but the last'],
  ['Eine. | Zwei. | Drei. | {count} Fragen.', 'four choices'],
  ['Eine Frage.|{count} Fragen.', 'a "|" without the spaces around it'],
])('"%s" is no deliberate plural: %s', (message) => {
  expect(isDeliberatePlural(message)).toBe(false);
});

/** Every message of a catalog under its dotted key path. */
function flatten(catalog: object, prefix = ''): [string, unknown][] {
  return Object.entries(catalog).flatMap(([key, value]): [string, unknown][] => {
    const path = prefix === '' ? key : `${prefix}.${key}`;
    return typeof value === 'object' && value !== null ? flatten(value, path) : [[path, value]];
  });
}

/** The keys of the catalog's messages that hold a "|" vue-i18n reads as syntax (a literal {'|'} is none) and are no deliberate plural. */
const slips = (catalog: object) =>
  flatten(catalog)
    .filter(([, message]) => typeof message === 'string' && message.replace(/\{'[^']*'\}/g, '').includes('|'))
    .filter(([, message]) => !isDeliberatePlural(String(message)))
    .map(([key]) => key);

describe.each([
  ['de', de],
  ['en', en],
])('the %s catalog', (_, catalog) => {
  test('has a "|" only in deliberate plurals', () => {
    expect(slips(catalog)).toEqual([]);
  });

  test('would be caught with a " | " in a message whose last choice names no {count}', () => {
    const planted = { ...catalog, planted: { cancel: 'Abbrechen | Zurück', counted: '{count} Fragen | gelöscht.' } };

    expect(slips(planted)).toEqual(['planted.cancel', 'planted.counted']);
  });
});
