/** A number that shrinks as time goes on, padded so that it sorts as text: a newer run's name sorts first. */
function countdown() {
  return String(9_999_999_999_999 - Date.now()).padStart(13, '0');
}

/** A short random suffix, so two tests that start in the same millisecond (several workers) still differ. */
function suffix() {
  return String(Math.floor(Math.random() * 1e4)).padStart(4, '0');
}

/**
 * A name no earlier run used, which a list by name (20 to a page) shows before every name an earlier run made: it
 * starts with a digit, and its number shrinks as time goes on, so the newest one is on the first page.
 */
export function newestFirst(label: string) {
  // The countdown before the label: by name, the newest of any spec comes first.
  return `0 E2E ${countdown()}${suffix()} ${label}`;
}
