# 0007: The UI speaks German and English, with German as the fallback

**Status:** Accepted

## Context

- The domain spec decided "UI auf Deutsch" (`docs/domain/README.md`, "Sprache im Code"). On 2026-11-22 the owner rewrote #146: the UI speaks German and English, German is the fallback.
- fabric generates the web app in English: the shell, the shadcn-vue components, the generated module pages and their tests all carry English literals.
- Dates and numbers read differently per language (`22.11.2026` and `22/11/2026`, `1.312` and `1,312`, `80 %` and `80%`), so formatting them by hand would be wrong in one of the two.
- The server answers refusals with problem details in its own words, in whatever language the host wrote them: most are English, some German (`organisation.invalid-pharmacy-name`, `compliance.capability-missing`). #145 gives every code one stable message and exports the codes the API answers, with their status and message, to `web/app/src/i18n/error-codes.json`; #147 translates refusals by those codes.

## Decision

- **vue-i18n, installed through its provider.** `src/providers/i18n.ts` installs it, first in `main.ts`'s providers. The catalogs are TypeScript, `src/i18n/de.ts` and `src/i18n/en.ts`; the German one defines the schema (`MessageSchema`) and the English one is typed by it, so a missing or extra key fails `vue-tsc`. German (`de-DE`) is the fallback locale for a missing key.
- **The language.** On the first visit it is the browser's preferred one if that is German or English (`de` gives `de-DE`, `en` gives `en-GB`), and German otherwise. The user menu switches it, and `localStorage['language']` remembers the choice. `index.html` sets `<html lang>` before the first paint, as it sets the theme.
- **Every user-visible text is a key**: in templates, in attributes (`aria-label`, `title`, `placeholder`), in `notify` calls, in zod messages and in route `titleKey`s. A form's zod messages are keys, translated where the error is shown (`t(errors.name)`), so a message already shown follows a switch of the language. `<html lang>` follows every change of vue-i18n's locale (`src/i18n/index.ts` watches it). German uses the glossary and addresses the user as "Sie"; English uses the glossary's code column. The brand, `pharma-training`, is the key `app.name`, the same in both catalogs.
- **Dates and numbers** go through vue-i18n's `d()` and `n()` with the formats in `src/i18n/formats.ts`, for `de-DE` and `en-GB`, in the browser's time zone. A calendar date (`yyyy-mm-dd`) goes through `calendarDate()`, so it never moves to another day.
- **Refusals are shown by their code** (#147). `src/i18n/errorText.ts` is the one helper: it takes the problem details' code at any status (`ApiError.failureCode`) and shows, in this order, the page's own text for that code where its words depend on the step (an override), the catalog's `errors.<code>` (`errors.<module>.<name>` for a module's code, such as `errors.organisation.invalid-pharmacy-name`, and `errors.<code>` for the host's, such as `errors.invalid-argument`), and, for a module's code the catalog does not know, the host's own words, in whatever language the host wrote them (`ApiError.serverMessage`, `src/api/baseApi.ts`), logging the code once per page load in development. That happens only across version skew, a host newer than the SPA. Anything else, no code, a framework code without a text, an edge's 502 or 503, a network failure, shows the action's own failed text. A limit in a text is a placeholder that `src/i18n/errorValues.ts` fills from the frontend's own bounds; the host's `detail` is never parsed. Toasts take the text (`errorText`); an inline text keeps the message (`errorMessage`, `codeMessage`) and renders it with `messageText` in the template, so it follows a switch of the language. Only `errorText.ts` reads `serverMessage`.
- **Code stays English**: identifiers, test names and comments.

## Guards

- The lint rule `@intlify/vue-i18n/no-raw-text` on every component in `src`, in the text and in the attributes a reader hears or sees (`container-aria-label` included), never turned off; `src/i18n/i18nGuard.test.ts` asks ESLint for the resolved config and fails when it is. A comment cannot turn it off in a component either: the components' block sets `linterOptions.noInlineConfig` and turns `vue/comment-directive` off, so neither `eslint-disable` (with or without a rule list) nor a config comment has an effect there.
- `src/i18n/catalogs.test.ts`: both catalogs have the same keys, no empty message, and the same placeholders per key.
- `src/i18n/errorCatalog.test.ts` reads `error-codes.json`: every exported code has a German and an English text under `errors.`, the host's own codes, `invalid-argument`, `already-exists` and `model-not-configured`, have one each too, no key under `errors.` names a code the host does not answer, and `errorValues` fills every placeholder of an error text.
- `src/i18n/serverMessageGuard.test.ts`: only `errorText.ts` and `baseApi.ts` (which sets it) may read `ApiError.serverMessage`; any other file under `src` that reads it fails. It parses each script with TypeScript's parser, so a comment or a string never counts, and counts every `serverMessage` identifier but a key written in an object literal (`{ serverMessage: refusal.message }`): a property read, an element read and any destructuring, renamed or not, are reads.
- Every screen's axe check runs in both languages.
- Lint bans, in every file under `src` but the tests, a literal passed to `notify`, formatting with `toLocale*String`, and any `Intl` constructor, with or without `new`; there is no exemption, `src/i18n/formats.ts` only uses `Intl`'s option types. It bans importing vue-sonner's `toast` anywhere but `src/lib/notify.ts`, beside the library bans every file keeps.

## Consequences

- A new screen, or a module from `fabric add-module`, comes with its keys in both catalogs in the same pull request; the lint rule fails it otherwise.
- A new or renamed error code comes with its German and English texts under `errors.` in the same pull request, so a backend lane's pull request that adds a code also touches `web/app/src`; `errorCatalog.test.ts` fails it otherwise.
- The words a user reads for a code the SPA does not know yet are the host's, in whatever language the host wrote them, not the catalog's.
- `fabric upgrade` may bring English literals back into `src/components/ui` or the generated pages; the lint rule catches them.
- The certificate PDF stays German (#50).
- A third language is a third catalog, and the parity test then compares three.
- Keycloak's login pages are Keycloak's own UI, configured in the realm, not here.
