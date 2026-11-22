/** A language of the app: the vue-i18n locale, the value stored under language and <html lang>, all at once. */
export type Language = 'de-DE' | 'en-GB';

export const languages: readonly Language[] = ['de-DE', 'en-GB'];

// index.html's script reads the same key and gives the same answers before the first paint.
const key = 'language';

function isLanguage(value: unknown): value is Language {
  return languages.some((language) => language === value);
}

/** The browser's most preferred language if it is German or English, and German, the fallback, otherwise. */
export function preferredLanguage(): Language {
  const preferred = navigator.languages?.[0] ?? navigator.language ?? '';
  const primary = preferred.split('-')[0]?.toLowerCase();
  return primary === 'en' ? 'en-GB' : 'de-DE';
}

/** The user's stored choice, or the browser's preferred language when there is none or storage is blocked. */
export function storedLanguage(): Language {
  try {
    const stored = localStorage.getItem(key);
    if (isLanguage(stored)) {
      return stored;
    }
  } catch {
    // Storage is blocked (privacy settings, a sandboxed frame): the browser decides.
  }
  return preferredLanguage();
}

/** Remembers the user's choice. */
export function storeLanguage(language: Language): void {
  try {
    localStorage.setItem(key, language);
  } catch {
    // Storage is blocked: the choice holds until the page is loaded again.
  }
}

/** Tells the browser and screen readers the page's language. */
export function applyLanguage(language: Language): void {
  document.documentElement.lang = language;
}
