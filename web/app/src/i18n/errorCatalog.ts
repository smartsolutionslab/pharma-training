// The checks behind src/i18n/errorCatalog.test.ts (#147): every code the host exports has a text in each catalog under
// errors.<module>.<name> (a module's code) or errors.<code> (the host's own), no text there belongs to no code, and
// errorValues fills every placeholder of those texts.

/** Catalogs by name (de, en): nested sections of texts. */
type Catalogs = Readonly<Record<string, object>>;

/** Values by code, as errorValues holds them. */
type Values = Readonly<Partial<Record<string, Readonly<Record<string, unknown>>>>>;

/** The path of a code's text: errors, then the module and its name, or the host's code alone. */
const pathOf = (code: string): string[] => ['errors', ...code.split('.')];

/** The text at a path of the catalog, or undefined where a section or nothing is. */
function textAt(catalog: object, path: readonly string[]): string | undefined {
  let node: unknown = catalog;
  for (const key of path) {
    if (typeof node !== 'object' || node === null || !Object.hasOwn(node, key)) {
      return undefined;
    }
    node = (node as Record<string, unknown>)[key];
  }
  return typeof node === 'string' ? node : undefined;
}

/** Every text of a section under its dotted key, the section's prefix included. */
function keysUnder(node: unknown, prefix: string): string[] {
  if (typeof node !== 'object' || node === null) {
    return [prefix];
  }
  return Object.entries(node).flatMap(([key, value]) => keysUnder(value, `${prefix}.${key}`));
}

/** The named placeholders of a text: {max}, { min }; a literal such as {'@'} is none. */
function placeholdersOf(text: string): string[] {
  return [...new Set([...text.matchAll(/\{\s*([A-Za-z_]\w*)\s*\}/g)].flatMap(([, name]) => name ?? []))];
}

/** Each code without a text in a catalog (a blank one or a section counts as none), and that catalog. */
export function missingTranslations(codes: readonly string[], catalogs: Catalogs): { catalog: string; code: string }[] {
  return Object.entries(catalogs).flatMap(([catalog, messages]) =>
    codes.filter((code) => (textAt(messages, pathOf(code))?.trim() ?? '') === '').map((code) => ({ catalog, code })),
  );
}

/** Each key under errors that is no code's, and its catalog. */
export function staleTranslations(codes: readonly string[], catalogs: Catalogs): { catalog: string; key: string }[] {
  const keys = new Set(codes.map((code) => pathOf(code).join('.')));
  return Object.entries(catalogs).flatMap(([catalog, messages]) =>
    Object.hasOwn(messages, 'errors')
      ? keysUnder((messages as Record<string, unknown>).errors, 'errors')
          .filter((key) => !keys.has(key))
          .map((key) => ({ catalog, key }))
      : [],
  );
}

/** Each placeholder of a code's text that its values do not fill, with the code and the catalog. */
export function unfilledPlaceholders(
  codes: readonly string[],
  catalogs: Catalogs,
  values: Values,
): { catalog: string; code: string; placeholder: string }[] {
  return Object.entries(catalogs).flatMap(([catalog, messages]) =>
    codes.flatMap((code) =>
      placeholdersOf(textAt(messages, pathOf(code)) ?? '')
        .filter((placeholder) => values[code]?.[placeholder] === undefined)
        .map((placeholder) => ({ catalog, code, placeholder })),
    ),
  );
}

/** Each code with values that no code in the list is. */
export function staleValues(codes: readonly string[], values: Values): string[] {
  const known = new Set(codes);
  return Object.keys(values).filter((code) => !known.has(code));
}
