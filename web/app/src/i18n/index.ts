import { createI18n } from 'vue-i18n';
import { de, type MessageSchema } from './de';
import { en } from './en';
import { datetimeFormats, numberFormats } from './formats';
import { applyLanguage, type Language, storedLanguage, storeLanguage } from './language';

export type { MessageKey, MessageSchema } from './de';
export { applyLanguage, type Language, languages, storedLanguage } from './language';

/** The app's vue-i18n: German and English, German for a missing key. src/providers/i18n.ts installs it. */
export const i18n = createI18n<[MessageSchema], Language, false>({
  legacy: false,
  locale: storedLanguage(),
  fallbackLocale: 'de-DE',
  messages: { 'de-DE': de, 'en-GB': en },
  datetimeFormats,
  numberFormats,
});

/** Translates outside a component's setup: notify texts, zod messages. In a template, use the component's t. */
export const t = i18n.global.t;

/** Shows the app in the language, tells the browser, and remembers the choice unless told not to. */
export function setLanguage(language: Language, { remember = true }: { remember?: boolean } = {}): void {
  i18n.global.locale.value = language;
  applyLanguage(language);
  if (remember) {
    storeLanguage(language);
  }
}
