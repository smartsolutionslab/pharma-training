import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import page from '../../index.html?raw';
import { i18n } from './index';
import { applyLanguage, type Language, preferredLanguage, storedLanguage, storeLanguage } from './language';

// The script index.html runs before the app, so <html lang> is the user's language from the first paint.
const script = /<script>([\s\S]*?)<\/script>/.exec(page)?.[1] ?? '';

function runScript() {
  // index.html's own text, run in the page's global scope as the browser runs it.
  new Function(script)();
}

/** The browser's preferred languages, most preferred first, as navigator reports them. */
function browserPrefers(...languages: string[]) {
  vi.stubGlobal('navigator', { languages, language: languages[0] ?? '' });
}

function blockStorage() {
  const blocked = () => {
    throw new DOMException('Storage is blocked.', 'SecurityError');
  };
  vi.spyOn(Storage.prototype, 'getItem').mockImplementation(blocked);
  vi.spyOn(Storage.prototype, 'setItem').mockImplementation(blocked);
}

// The test setup applies German before every test; these tests start from a page that has no language yet.
beforeEach(() => {
  document.documentElement.removeAttribute('lang');
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  localStorage.clear();
  document.documentElement.removeAttribute('lang');
  document.documentElement.removeAttribute('class');
});

// What the browser prefers, and the language the app starts in without a stored choice.
const detection: [string, string[], Language][] = [
  ['German from Austria is German', ['de-AT'], 'de-DE'],
  ['plain German is German', ['de'], 'de-DE'],
  ['American English is British English', ['en-US'], 'en-GB'],
  ['plain English is British English', ['en'], 'en-GB'],
  ['French falls back to German', ['fr'], 'de-DE'],
  ['only the most preferred language counts', ['fr-FR', 'en'], 'de-DE'],
  ['a browser that names no language falls back to German', [], 'de-DE'],
];

describe('the language the browser prefers', () => {
  test.each(detection)('%s', (_, browser, expected) => {
    browserPrefers(...browser);

    expect(preferredLanguage()).toBe(expected);
  });

  test.each(detection)('without a stored choice, %s', (_, browser, expected) => {
    browserPrefers(...browser);

    expect(storedLanguage()).toBe(expected);
  });
});

// Some webviews and older browsers have navigator.language but no navigator.languages; the app must still start.
describe('a browser without navigator.languages', () => {
  test('its one language decides', () => {
    vi.stubGlobal('navigator', { language: 'en-US' });

    expect(preferredLanguage()).toBe('en-GB');
  });

  test('with neither it falls back to German', () => {
    vi.stubGlobal('navigator', {});

    expect(preferredLanguage()).toBe('de-DE');
  });

  test("index.html's script agrees", () => {
    vi.stubGlobal('navigator', { language: 'en-US' });

    runScript();

    expect(document.documentElement.lang).toBe('en-GB');
  });
});

describe('the stored choice', () => {
  test('a stored choice wins over the browser', () => {
    browserPrefers('de-DE');
    localStorage.setItem('language', 'en-GB');

    expect(storedLanguage()).toBe('en-GB');
  });

  test('a stored German wins over an English browser', () => {
    browserPrefers('en-US');
    localStorage.setItem('language', 'de-DE');

    expect(storedLanguage()).toBe('de-DE');
  });

  test('a stored value that is no language of the app is ignored', () => {
    browserPrefers('en-US');
    localStorage.setItem('language', 'fr-FR');

    expect(storedLanguage()).toBe('en-GB');
  });

  test('with storage blocked the browser decides', () => {
    browserPrefers('en-US');
    localStorage.setItem('language', 'de-DE');
    blockStorage();

    expect(storedLanguage()).toBe('en-GB');
  });

  test('storing a language remembers it under language', () => {
    storeLanguage('en-GB');

    expect(localStorage.getItem('language')).toBe('en-GB');
  });

  test('storing a language with storage blocked does not fail', () => {
    blockStorage();

    expect(() => {
      storeLanguage('en-GB');
    }).not.toThrow();
  });

  test('applying a language sets <html lang>', () => {
    applyLanguage('en-GB');
    expect(document.documentElement.lang).toBe('en-GB');

    applyLanguage('de-DE');
    expect(document.documentElement.lang).toBe('de-DE');
  });
});

describe("index.html's script before the first paint", () => {
  test('the page is German until the script runs', () => {
    expect(page).toMatch(/<html lang="de-DE">/);
  });

  test('the script reads the stored choice', () => {
    expect(script).toContain("localStorage.getItem('language')");
  });

  test.each(detection)('without a stored choice, %s', (_, browser, expected) => {
    browserPrefers(...browser);

    runScript();

    expect(document.documentElement.lang).toBe(expected);
  });

  test('a stored choice wins over the browser', () => {
    browserPrefers('de-DE');
    localStorage.setItem('language', 'en-GB');

    runScript();

    expect(document.documentElement.lang).toBe('en-GB');
  });

  test('a stored value that is no language of the app is ignored', () => {
    browserPrefers('en-US');
    localStorage.setItem('language', 'fr-FR');

    runScript();

    expect(document.documentElement.lang).toBe('en-GB');
  });

  test('with storage blocked the browser decides', () => {
    browserPrefers('en-US');
    blockStorage();

    runScript();

    expect(document.documentElement.lang).toBe('en-GB');
  });

  test('the theme script still runs beside it', () => {
    browserPrefers('en-US');
    localStorage.setItem('theme', 'dark');

    runScript();

    expect(document.documentElement.classList.contains('dark')).toBe(true);
    expect(document.documentElement.lang).toBe('en-GB');
  });
});

// Whatever changes the locale, setLanguage or code that sets vue-i18n's locale itself, <html lang> says the language
// the page is in: a screen reader reads the page in the wrong voice otherwise.
describe('<html lang> and the locale', () => {
  test('follows a locale set on vue-i18n directly', async () => {
    i18n.global.locale.value = 'en-GB';

    await vi.waitFor(() => expect(document.documentElement.lang).toBe('en-GB'));

    i18n.global.locale.value = 'de-DE';

    await vi.waitFor(() => expect(document.documentElement.lang).toBe('de-DE'));
  });
});
