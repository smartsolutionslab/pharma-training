# 0007: The UI speaks German and English, with German as the fallback

**Status:** Accepted

## Context

- The domain spec decided "UI auf Deutsch" (`docs/domain/README.md`, "Sprache im Code"). On 2026-11-22 the owner rewrote #146: the UI speaks German and English, German is the fallback.
- fabric generates the web app in English: the shell, the shadcn-vue components, the generated module pages and their tests all carry English literals.
- Dates and numbers read differently per language (`22.11.2026` and `22/11/2026`, `1.312` and `1,312`, `80 %` and `80%`), so formatting them by hand would be wrong in one of the two.
- The server answers refusals with problem details in its own words. Translating them by their codes is #147, after #145 publishes the codes; until then the UI cannot translate them.

## Decision

- **vue-i18n, installed through its provider.** `src/providers/i18n.ts` installs it, first in `main.ts`'s providers. The catalogs are TypeScript, `src/i18n/de.ts` and `src/i18n/en.ts`; the German one defines the schema (`MessageSchema`) and the English one is typed by it, so a missing or extra key fails `vue-tsc`. German (`de-DE`) is the fallback locale for a missing key.
- **The language.** On the first visit it is the browser's preferred one if that is German or English (`de` gives `de-DE`, `en` gives `en-GB`), and German otherwise. The user menu switches it, and `localStorage['language']` remembers the choice. `index.html` sets `<html lang>` before the first paint, as it sets the theme.
- **Every user-visible text is a key**: in templates, in attributes (`aria-label`, `title`, `placeholder`), in `notify` calls, in zod messages and in route `titleKey`s. A form's zod messages are keys, translated where the error is shown (`t(errors.name)`), so a message already shown follows a switch of the language. `<html lang>` follows every change of vue-i18n's locale (`src/i18n/index.ts` watches it). German uses the glossary and addresses the user as "Sie"; English uses the glossary's code column. The brand, `pharma-training`, is the key `app.name`, the same in both catalogs.
- **Dates and numbers** go through vue-i18n's `d()` and `n()` with the formats in `src/i18n/formats.ts`, for `de-DE` and `en-GB`, in the browser's time zone. A calendar date (`yyyy-mm-dd`) goes through `calendarDate()`, so it never moves to another day.
- **Server messages** are shown as the server sends them until #147 translates them by their codes, and only a module's own refusal: problem details below 500 whose `code` is a module's code (`<module>.<code>`, such as `organisation.invalid-pharmacy-name`), its `detail`, else its `title` (`ApiError.serverMessage`, `src/api/baseApi.ts`). The framework's titles (`Bad Request`, `Forbidden`, a 500's), a framework code without a module (`already-exists`) and a failed guard's detail (`invalid-argument`) are English and never shown; the catalog's text for the failure shows instead.
- **Code stays English**: identifiers, test names and comments.

## Guards

- The lint rule `@intlify/vue-i18n/no-raw-text` on every component in `src`, in the text and in the attributes a reader hears or sees (`container-aria-label` included), never turned off; `src/i18n/i18nGuard.test.ts` asks ESLint for the resolved config and fails when it is. A comment cannot turn it off in a component either: the components' block sets `linterOptions.noInlineConfig` and turns `vue/comment-directive` off, so neither `eslint-disable` (with or without a rule list) nor a config comment has an effect there.
- `src/i18n/catalogs.test.ts`: both catalogs have the same keys, no empty message, and the same placeholders per key.
- Every screen's axe check runs in both languages.
- Lint bans, in every file under `src` but the tests, a literal passed to `notify`, formatting with `toLocale*String`, and any `Intl` constructor, with or without `new`; there is no exemption, `src/i18n/formats.ts` only uses `Intl`'s option types. It bans importing vue-sonner's `toast` anywhere but `src/lib/notify.ts`, beside the library bans every file keeps.

## Consequences

- A new screen, or a module from `fabric add-module`, comes with its keys in both catalogs in the same pull request; the lint rule fails it otherwise.
- `fabric upgrade` may bring English literals back into `src/components/ui` or the generated pages; the lint rule catches them.
- The certificate PDF stays German (#50).
- A third language is a third catalog, and the parity test then compares three.
- Keycloak's login pages are Keycloak's own UI, configured in the realm, not here.
