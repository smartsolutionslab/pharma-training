# 0027: Error codes are the contract

**Status:** Accepted (#145)

## Context

- The server's refusals mixed languages: 60 messages were German, the rest English. 31 English messages interpolated their constructor's arguments (an id, a status, a date, a count), and five codes had two messages, one per layer that could refuse them (`editorial.chapter-not-found`, `editorial.question-not-found`, `organisation.no-open-invitation`, `organisation.no-active-membership`, `editorial.invalid-question-count`).
- The UI speaks German and English (ADR 0007) and translates a refusal by its code (#147). It needs the list of codes a caller can receive, with their statuses, and a fallback text for a code it does not know yet.
- The owner decided on 2026-11-22 (#145, #147): the code is the contract; the server's message is one English fallback per code.

## Decision

1. **Every message is English.** Every non-abstract `ApiError` and `DomainError` of the modules' Domain and Application projects and of `src/BuildingBlocks` has an English `Message`: printable ASCII (0x20 to 0x7E) and none of the common German words the rule lists.
2. **One message per code, independent of the instance.** A message never reads a constructor argument; it may read a compile-time constant (`{QuestionCount.Maximum}`). Every type carrying a code has the same message, whichever layer it is in: where an Application record shares a code with a domain error, it takes the code and message from that error (`private static readonly <DomainError> Refusal = new(...)`, as `InvitationLinkNotFound` does). The caller already knows the ids it sent, and no frontend code parses a message.
3. **One status per code**, whichever way a caller receives it.
4. **Codes are stable.** A released code is never renamed or removed. A new code is an API change and shows up in the diff of the export; review of that file enforces this, not a test.
5. **The export.** `web/app/src/i18n/error-codes.json` lists every code a caller can receive. A test writes it and fails while the committed copy is stale, as `OpenApiDocumentTests` does for `web/app/openapi.json`; CI runs it through `.github/scripts/test.sh`. The file is in `web/app/.prettierignore`.

**What a caller can receive.** Every non-abstract `ApiError` of the module assemblies, with its own status. A `DomainError` only where a public static method of a `*Failures` class in its module's Application maps it to an `ApiError`, tried in this order: a method taking its exact type, one taking a `[Union]` that has it as a case, and only then one taking `DomainError`; a method that throws for it does not map it. The host's generic refusals are sealed `ApiError` records in `src/BuildingBlocks`, so the export sees them: `invalid-argument` (400, `InvalidArgument`), `already-exists` (409, `AlreadyExists`) and `model-not-configured` (503, `ModelNotConfigured`); their handlers take the title, status and code from the records and keep their own `detail`. Problems without a code (a 401, a policy's 403, a binding 400, a 500) are outside the contract; the frontend tells them apart by status. The Kernel's `DomainFailure` only wraps a domain error's code and is not scanned. A domain code no endpoint answers yet has no status and is left out by an explicit list in the export test, which says why for each: today Assessment's four examination errors (mapped by #271) and `organisation.invitation-link-limit-reached`, which only a message consumer throws.

**The file.** One JSON array, sorted by code ordinally, one object per code with `code`, `status` (an integer) and `message`; written with `System.Text.Json`, indented, with `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` so `'` and the backtick stay readable, and ending in one line feed.

**The server's message is only a fallback.** The frontend translates every code in the file (#147); it shows the server's English message only for a code it does not know.

## Guards

- The architecture rules `ErrorMessagesAreEnglish` and `EveryErrorCodeHasOneMessageAndStatus` probe every error type twice, with empty and with filled constructor arguments; a message that differs between the two, or a constructor that throws on them, reads its arguments.
- `ErrorCodesExportTests` rewrites and checks the export, checks the host's generic codes, and fails for a code the API can report without a status unless the exclusion list names it.
- The integration tests read the expected English message from the export (`ErrorCodeCatalog.MessageOf(code)`), so a rewording touches only the file.

## Consequences

- A new code, or a new way of receiving one, shows up in the diff of `error-codes.json` and is reviewed as an API change.
- Until #147 ships, the German UI shows the English server message for a module code a page does not translate itself.
- An open pull request that adds or rewords a message breaks the rules once this merges, and rewords its messages to follow them; a domain code it newly maps to a status leaves the exclusion list.
