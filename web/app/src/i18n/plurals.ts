/**
 * Whether a message's "|" is a deliberate plural, as the catalogs write one: two (one, several) or three (none, one,
 * several) choices separated by " | ", none holding another "|", the last naming {count}, the number it is chosen by.
 * vue-i18n reads any other "|" as a plural's separator too, which would show the reader half a sentence.
 */
export function isDeliberatePlural(message: string): boolean {
  const choices = message.split(' | ');
  if (choices.length < 2 || choices.length > 3) {
    return false;
  }
  if (choices.some((choice) => choice.includes('|'))) {
    return false;
  }
  const last = choices[choices.length - 1] ?? '';
  const earlier = choices.slice(0, -1);
  return last.includes('{count}') && earlier.every((choice) => !choice.includes('{count}'));
}
