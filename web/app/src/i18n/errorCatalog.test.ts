import { readFileSync } from 'node:fs';
import { describe, expect, test } from 'vitest';
import { z } from 'zod';
import { de } from './de';
import { en } from './en';
import { missingTranslations, staleTranslations, staleValues, unfilledPlaceholders } from './errorCatalog';
import { errorValues } from './errorValues';
import { i18n, type Language } from './index';

// A refusal is shown by its code (#147): the catalogs hold a German and an English text for every code the host
// exports to src/i18n/error-codes.json (#145), under errors.<module>.<name> for a module's code and errors.<code> for
// the host's own. A new or renamed code fails here until both texts come with it, in the same pull request.

/** The pieces a fixture catalog is made of: a text, or a section of further keys. */
type Fixture = { [key: string]: string | Fixture };

const byJson = <T>(items: readonly T[]) =>
  [...items].sort((left, right) => JSON.stringify(left).localeCompare(JSON.stringify(right)));

describe('missingTranslations', () => {
  const codes = ['organisation.invalid-pharmacy-name', 'invalid-argument'];

  test('finds nothing when every catalog has a text for every code, a module code nested under its module', () => {
    const catalog: Fixture = {
      errors: { 'invalid-argument': 'Ungültig.', organisation: { 'invalid-pharmacy-name': 'Name ungültig.' } },
    };

    expect(missingTranslations(codes, { de: catalog, en: catalog })).toEqual([]);
  });

  test('names each code and the catalog that lacks its text', () => {
    const german: Fixture = { errors: { 'invalid-argument': 'Ungültig.' } };
    const english: Fixture = { errors: { organisation: { 'invalid-pharmacy-name': 'Invalid name.' } } };

    expect(byJson(missingTranslations(codes, { de: german, en: english }))).toEqual(
      byJson([
        { catalog: 'de', code: 'organisation.invalid-pharmacy-name' },
        { catalog: 'en', code: 'invalid-argument' },
      ]),
    );
  });

  test('finds every code missing in a catalog without an errors section', () => {
    expect(byJson(missingTranslations(codes, { de: {} }))).toEqual(
      byJson([
        { catalog: 'de', code: 'invalid-argument' },
        { catalog: 'de', code: 'organisation.invalid-pharmacy-name' },
      ]),
    );
  });

  test.each<[string, Fixture]>([
    ['a blank text', { errors: { organisation: { 'invalid-pharmacy-name': '  ' } } }],
    ['a section in place of a text', { errors: { organisation: { 'invalid-pharmacy-name': { short: 'Name.' } } } }],
    ['a dotted key in place of the module section', { errors: { 'organisation.invalid-pharmacy-name': 'Name.' } }],
    ['a module code outside errors', { organisation: { 'invalid-pharmacy-name': 'Name.' } }],
  ])('counts %s as missing', (_, catalog) => {
    expect(missingTranslations(['organisation.invalid-pharmacy-name'], { de: catalog })).toEqual([
      { catalog: 'de', code: 'organisation.invalid-pharmacy-name' },
    ]);
  });
});

describe('staleTranslations', () => {
  test('finds nothing when every text under errors belongs to a code', () => {
    const catalog: Fixture = {
      app: { name: 'pharma-training' },
      errors: { 'invalid-argument': 'Ungültig.', organisation: { 'invalid-pharmacy-name': 'Name ungültig.' } },
    };

    expect(
      staleTranslations(['invalid-argument', 'organisation.invalid-pharmacy-name'], { de: catalog, en: catalog }),
    ).toEqual([]);
  });

  test('names each key under errors that no code has, and its catalog', () => {
    const german: Fixture = {
      errors: { organisation: { 'invalid-pharmacy-name': 'Name ungültig.', 'removed-code': 'Weg.' } },
    };
    const english: Fixture = {
      errors: { 'retired-host-code': 'Gone.', organisation: { 'invalid-pharmacy-name': 'Invalid name.' } },
    };

    expect(byJson(staleTranslations(['organisation.invalid-pharmacy-name'], { de: german, en: english }))).toEqual(
      byJson([
        { catalog: 'de', key: 'errors.organisation.removed-code' },
        { catalog: 'en', key: 'errors.retired-host-code' },
      ]),
    );
  });

  test('ignores keys outside errors, whatever they are named', () => {
    const catalog: Fixture = { organisation: { 'removed-code': 'Weg.' }, app: { name: 'pharma-training' } };

    expect(staleTranslations([], { de: catalog })).toEqual([]);
  });
});

describe('unfilledPlaceholders', () => {
  const codes = ['organisation.invalid-pharmacy-name', 'editorial.invalid-question-count'];
  const catalog: Fixture = {
    errors: {
      organisation: { 'invalid-pharmacy-name': 'Ein Name hat 1 bis {max} Zeichen.' },
      editorial: { 'invalid-question-count': 'Ein Test hat { min } bis {max} Fragen.' },
    },
  };

  test('finds nothing when the values fill every placeholder', () => {
    const values = {
      'organisation.invalid-pharmacy-name': { max: 120 },
      'editorial.invalid-question-count': { min: 1, max: 50 },
    };

    expect(unfilledPlaceholders(codes, { de: catalog }, values)).toEqual([]);
  });

  test('names each placeholder the values do not fill, with its code and catalog', () => {
    const values = { 'editorial.invalid-question-count': { max: 50 } };

    expect(byJson(unfilledPlaceholders(codes, { de: catalog, en: catalog }, values))).toEqual(
      byJson([
        { catalog: 'de', code: 'organisation.invalid-pharmacy-name', placeholder: 'max' },
        { catalog: 'de', code: 'editorial.invalid-question-count', placeholder: 'min' },
        { catalog: 'en', code: 'organisation.invalid-pharmacy-name', placeholder: 'max' },
        { catalog: 'en', code: 'editorial.invalid-question-count', placeholder: 'min' },
      ]),
    );
  });

  test("does not take a literal such as {'@'} for a placeholder", () => {
    const literal: Fixture = { errors: { 'invalid-argument': "Eine Adresse braucht ein {'@'}." } };

    expect(unfilledPlaceholders(['invalid-argument'], { de: literal }, {})).toEqual([]);
  });
});

describe('staleValues', () => {
  test('names each code with values that no exported code has', () => {
    const values = { 'organisation.invalid-pharmacy-name': { max: 120 }, 'organisation.removed-code': { max: 3 } };

    expect(staleValues(['organisation.invalid-pharmacy-name'], values)).toEqual(['organisation.removed-code']);
  });

  test('finds nothing when every entry belongs to a code', () => {
    expect(
      staleValues(['organisation.invalid-pharmacy-name'], { 'organisation.invalid-pharmacy-name': { max: 120 } }),
    ).toEqual([]);
  });
});

// The codes as the host exports them (#145): an array of { code, status, message }, sorted by code, the host's own codes
// (src/BuildingBlocks: already-exists, invalid-argument, model-not-configured) among the modules'. The message is the
// host's English fallback for a code the catalog lacks; the catalogs never copy it.
const exported = z
  .array(z.object({ code: z.string().min(1), status: z.number().int(), message: z.string() }))
  .parse(JSON.parse(readFileSync('src/i18n/error-codes.json', 'utf8')));
const codes = exported.map(({ code }) => code);

const catalogs = { de, en };

describe('the catalogs', () => {
  test('have a German and an English text for every exported code', () => {
    expect(missingTranslations(codes, catalogs)).toEqual([]);
  });

  // Every page may meet them, so the export must hold them: a code without a module is the host's own.
  test("export the host's own codes beside the modules'", () => {
    expect(codes.filter((code) => !code.includes('.'))).toEqual([
      'already-exists',
      'invalid-argument',
      'model-not-configured',
    ]);
  });

  test('have no text under errors for a code the host does not answer', () => {
    expect(staleTranslations(codes, catalogs)).toEqual([]);
  });

  test('fill every placeholder of an error text from errorValues', () => {
    expect(unfilledPlaceholders(codes, catalogs, errorValues)).toEqual([]);
  });

  test('have values only for codes the host answers', () => {
    expect(staleValues(codes, errorValues)).toEqual([]);
  });

  // vue-i18n's own lookup, not the walk above: a hyphen is part of a key, a dot separates the module from the name.
  test.each<[Language]>([['de-DE'], ['en-GB']])(
    'let vue-i18n find errors.<code> in %s for every exported code',
    (locale) => {
      const unresolved = codes.filter((code) => !i18n.global.te(`errors.${code}`, locale));

      expect(unresolved).toEqual([]);
    },
  );
});
