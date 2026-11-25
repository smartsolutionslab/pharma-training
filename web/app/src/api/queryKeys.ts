/** A key as TanStack Query keeps it: a readonly array of strings, numbers and plain objects. */
export type QueryKey = readonly unknown[];

/** The value in a form whose JSON is the same for the same contents: object fields in order of their names. */
function canonical(value: unknown): unknown {
  if (Array.isArray(value)) {
    return value.map(canonical);
  }
  if (value !== null && typeof value === 'object') {
    return Object.fromEntries(
      Object.entries(value)
        .filter(([, field]) => field !== undefined)
        .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
        .map(([name, field]) => [name, canonical(field)]),
    );
  }
  return value;
}

const same = (a: unknown, b: unknown) => JSON.stringify(canonical(a)) === JSON.stringify(canonical(b));

/**
 * Whether a cached query's key falls under a key a generated key function gives (`getGet…QueryKey(id)`): the key starts
 * with every element of the prefix, an object compared by its contents. Code that finds a query uses this with the
 * generated function, never a position in the key, so a change of the generated key's shape cannot go unnoticed (#426).
 */
export function startsWithKey(key: QueryKey | undefined, prefix: QueryKey): boolean {
  if (key === undefined || key.length < prefix.length) {
    return false;
  }
  return prefix.every((element, index) => same(key[index], element));
}
