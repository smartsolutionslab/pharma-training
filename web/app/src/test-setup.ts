import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/vue';
import { config } from '@vue/test-utils';
import { afterEach, beforeEach } from 'vitest';
import * as axeMatchers from 'vitest-axe/matchers';
import { expect } from 'vitest';
import { i18n, setLanguage } from '@/i18n';

expect.extend(axeMatchers);

// jsdom measures nothing: the command palette scrolls the item it highlights into view.
Element.prototype.scrollIntoView ??= () => undefined;

// Every render has vue-i18n, also one that passes plugins of its own: Vue Test Utils, which Testing Library mounts
// through, merges the lists.
config.global.plugins.push(i18n);

// Every test starts in German, the fallback language, whatever jsdom's navigator (en-US) prefers. A test about English
// sets it with setLanguage('en-GB', { remember: false }).
beforeEach(() => {
  setLanguage('de-DE', { remember: false });
});

// Vitest runs without globals, so Testing Library cannot register its own cleanup.
afterEach(cleanup);

afterEach(() => {
  localStorage.removeItem('language');
  document.documentElement.removeAttribute('lang');
});
