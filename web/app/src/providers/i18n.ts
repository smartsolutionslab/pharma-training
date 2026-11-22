import type { App, Component } from 'vue';
import { i18n } from '@/i18n';
import { applyLanguage } from '@/i18n/language';

/** Every text the user sees comes from src/i18n's catalogs, in the language the user chose or the browser prefers. */
export const withI18n = (app: App, root: Component) => {
  app.use(i18n);
  applyLanguage(i18n.global.locale.value);
  return root;
};
