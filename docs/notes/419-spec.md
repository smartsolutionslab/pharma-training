# #419 — Export page: a manager sees and changes the pharmacy's retention period

Lane: frontend · Context: certification · Depends on: #391 (PR #418, open and unmerged on 2026-11-25)
Prepared on `prep/419` (main + local merge of `origin/agent/391`, codegen run). Nothing committed.

## Spec

### The contract (#418, ADR 0039), as built

| | |
|---|---|
| Read | `GET /certification/pharmacies/{id}/retention-period` → `200 CertificationRetentionPeriodDto { years: number; isDefault: boolean }`. `isDefault` is true only while the pharmacy has stored nothing (then `years` = 5). |
| Set | `PUT` same route, body `SetCertificationRetentionPeriodRequest { years }` → **`204`, no body**. Nothing on the page can come from the answer; the page reads the GET again. |
| Who | Both routes use `.RequireAuthorization()` only. The handler decides with `Capability.ManageRetention` from Certification's copy of the membership (ADR 0003). Only the manager role has it. |
| Refusals | No token: `401`. Token without a person: `403 certification.identity-incomplete`. Non-member, or Certification has not seen the membership yet: `404 certification.pharmacy-not-found`. Member without the capability (an employee): `403 certification.capability-missing`. Missing years, or years outside 2–30 (PUT only): `400 certification.invalid-retention-period`. |
| Concurrency | Last write wins: one upsert, no version, no `409` (ADR 0039 "Last write wins"). The page needs no conflict handling. |
| Default | 5 years (`RetentionPeriod.Default`). Setting 5 explicitly stores a row: `isDefault` becomes false, and "Standard" goes away. That is correct. |
| Generated client | `useGetCertificationPharmaciesIdRetentionPeriod(id, { query })`, `getGetCertificationPharmaciesIdRetentionPeriodQueryKey(id)`, `usePutCertificationPharmaciesIdRetentionPeriod()` with `mutateAsync({ id, data: { years } })`. |
| Texts that exist | `errors.certification.invalid-retention-period` in de/en with `{min}`/`{max}`, filled from `retentionYears` through `errorValues`. `errors.certification.capability-missing`, `…pharmacy-not-found` and `…identity-incomplete` are already in the catalogs. |

### Behaviour

1. **Placement.** `ExportCertificatesPage.vue` gets a `<section aria-labelledby>` below the export form, under its own `h2` "Aufbewahrungsfrist" / "Retention period". It lives in the new component `RetentionPeriodSection.vue` (two words), which the page renders with the current pharmacy.
2. **Shown only when it can be used.** The section renders only while the GET's `isSuccess` is true for the current pharmacy:
   - nothing renders while the GET is pending;
   - nothing renders when the GET fails for any reason (403, 404, 5xx, network), and no error is shown. The export above stays the page's job, and it already reports `pharmacy-not-found` itself.
   - The GET runs only while a pharmacy is current: `enabled: computed(() => current.value !== undefined)`, id `() => current.value?.id ?? ''`, as `useTeam` does it.
   - It does not retry a 4xx: `retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 3`. A 403 is final, so it is not asked four times.
   - Hiding the section is a convenience. The handler decides (ADR 0003).
3. **Content of the section:**
   - an intro naming the pharmacy;
   - a status line: "Derzeit {years} Jahre" / "Currently {years} years", followed by " (Standard)" / " (default)" when `isDefault`;
   - the retroactive note (wording below);
   - the form.
4. **The form.** vee-validate with `toTypedSchema(retentionSchema)` has one field, `years`:
   - `<Input type="number" inputmode="numeric" step="1" :min="2" :max="30">`, id `retention-years`, label "Aufbewahrungsfrist in Jahren" / "Retention period in years";
   - `aria-describedby` points at the status line and the note (and at the error while one shows), with `class="max-w-40 tabular-nums"`;
   - the initial value is the GET's `years`. The form is reset with `resetForm({ values: { years } })` whenever the GET's `years` or `isDefault` change, which covers a pharmacy switch and the re-read after a save. A field error is cleared with it.
5. **Validation (Zod).** `retentionSchema = z.object({ years: z.number().int().min(min).max(max) })`. Every failure gives one message, the catalog key `errors.certification.invalid-retention-period`, so the client and the host's 400 show the same sentence.
   - That covers an empty field (`''`), `NaN`, 2.5, 1 and 31.
   - The field shows it through `messageText({ key, values: retentionYears })`, under the field with `role="alert"`, with `aria-invalid` on the field.
   - On a client refusal the focus goes to the field and nothing is sent.
   - The bounds: per `errorValues.ts`'s own rule ("where a form holds a value to a bound of its own, the bound comes from that form's module"), `retentionYears` moves into `web/app/src/modules/certification/retentionSchema.ts` under the same name. `errorValues.ts` imports it and still maps `'certification.invalid-retention-period': retentionYears`.
6. **Saving:**
   - **Busy.** While the PUT is out, the button has `aria-disabled="true"` and `aria-busy="true"`, reads "Wird gespeichert…" / "Saving…", and a press sends nothing. It stays enabled, so it keeps keyboard focus, as the export button does. A `role="status"` sr-only line says "Wird gespeichert…".
   - **Success.** `notify.success(t('certification.retention.saved', { years }))`, then `invalidateQueries({ queryKey: getGetCertificationPharmaciesIdRetentionPeriodQueryKey(id) })` for the pharmacy the PUT was for. The re-read updates the status line ("Standard" goes away) and resets the form.
   - **Refusals by code** (ADR 0027, `retentionRefusal(error)` in `retentionErrors.ts`, by `ApiError.code`):
     - `certification.invalid-retention-period`: the field error with the catalog text, and focus to `retention-years`.
     - `certification.capability-missing`, `certification.identity-incomplete`, `certification.pharmacy-not-found`: `notify.failure(<catalog errors.<code> text>)`, then invalidate the GET. On the re-read the GET fails, so the section disappears, which is why the refusal goes to a toast and not into the section. `pharmacy-not-found` also invalidates `getGetOrganisationPharmaciesMineQueryKey()`, as the export does.
     - Anything else (network, 5xx, a 400 without a code): `notify.failure(t('certification.retention.failed'))`. The form keeps the value, so pressing again retries.
   - There is no conflict handling (last write wins).
7. **Texts** (new keys under `certification.retention`, de and en, in `src/i18n/de.ts` and `en.ts`):

| key | de | en |
|---|---|---|
| `title` | Aufbewahrungsfrist | Retention period |
| `intro` | Wie viele Jahre nach ihrer Ausstellung die Teilnahmebestätigungen von {pharmacy} im Export enthalten sind. | How many years after their issue {pharmacy}'s certificates are included in the export. |
| `current` | Derzeit {years} Jahre | Currently {years} years |
| `default` | (Standard) | (default) |
| `retroactive` | Die Frist gilt rückwirkend für alle Bestätigungen: Verlängern Sie sie, sind ältere Bestätigungen wieder im Export enthalten; verkürzen Sie sie, fehlen die älteren. Gelöscht wird nichts. | The period applies retroactively to every certificate: lengthening it includes older certificates in the export again; shortening it leaves the older ones out. Nothing is deleted. |
| `years` | Aufbewahrungsfrist in Jahren | Retention period in years |
| `submit` | Frist speichern | Save period |
| `saving` | Wird gespeichert… | Saving… |
| `saved` | Die Aufbewahrungsfrist beträgt jetzt {years} Jahre. | The retention period is now {years} years. |
| `failed` | Die Aufbewahrungsfrist konnte nicht gespeichert werden. Versuchen Sie es erneut. | The retention period could not be saved. Try again. |

   `{years}` is always at least 2, so no plural forms are needed. Numbers go through `n(…, 'integer')`, as `messageText` formats them.

### Out of scope

- Showing the period anywhere else.
- Changing the export window (ADR 0032 keeps it at 5 years per request).
- An employee's view: the route's `audience: 'manager'` stays.

## Plan

ADRs relied on:

- **0003**: the handler decides by capability; the UI only hides.
- **0007**: de/en texts.
- **0027**: refusals by code, and texts from the catalog.
- **0032**: the export page and its window, unchanged.
- **0034**, amended by **0039**: the period, its range, default 5, retroactive effect, 204, last write wins.

Patterns copied:

- `ExportCertificatesPage.vue`: busy button, focus to the field, refusal by code, invalidating `mine` on `pharmacy-not-found`.
- `useTeam.ts`: an `enabled` query keyed on the current pharmacy.
- `useTestBlueprint.ts`: a PUT mutation, then invalidating the GET key.
- `testBlueprintSchema.ts` / `TestBlueprintForm.vue`: a whole-number field with `z.number().int()`.

New files in `web/app/src/modules/certification/`:

- `retentionSchema.ts` (+ `.test.ts`): `retentionYears`, `retentionSchema`.
- `retentionErrors.ts` (+ `.test.ts`): `retentionRefusal(error)`, returning `{ kind: 'field'; message }` or `{ kind: 'toast'; message; pharmacyGone }`.
- `useRetentionPeriod.ts`: the query (enabled, retry predicate) and `save(id, years)` over the mutation, which invalidates on settle.
- `RetentionPeriodSection.vue`: props `pharmacy: MyPharmacyDto`.
- `ExportCertificatesPage.retention.test.ts`: page-level tests with a fake `fetch` host, as `ExportCertificatesPage.test.ts` does it; that file's `hostFrom` already answers unknown routes with 404, so its existing tests keep the section hidden and stay green unchanged.
- `web/app/e2e/certification-retention.spec.ts`.

Changed files:

- `ExportCertificatesPage.vue`: renders the section when `current !== undefined`.
- `ExportCertificatesPage.data.ts`: adds the retention example.
- `ExportCertificatesPage.axe.test.ts`: adds the field-error state of the retention form.
- `src/i18n/errorValues.ts`: imports `retentionYears`.
- `src/i18n/de.ts`, `src/i18n/en.ts`.

Nothing changes in the backend, `openapi.json` or `generated.ts`.

## Tasks

Each task is one PR-sized commit. Write the red test first, run it, and quote the failure under `## Tests observed failing`. Commits are `…(certification): …` with `Agent-Executor:`.

### T0: Check the contract after #418 merges (no code)

Run this on `agent/419` from main once #391 is closed (`next.sh` waits for that).

1. `grep -n "retention-period" web/app/openapi.json`: GET answers 200 `CertificationRetentionPeriodDto` (`years` int, `isDefault` bool), and PUT takes `SetCertificationRetentionPeriodRequest` (`years` int, nullable) and answers 204.
2. `pnpm codegen` in `web/app`: `generated.ts` exports the four names in the contract table above.
3. `error-codes.json` has `certification.invalid-retention-period` with status 400, and `de.ts`/`en.ts` have its `errors.` text with `{min}`/`{max}`. `errorValues.ts` maps it to `retentionYears = { min: 2, max: 30 }`.

If any of these differs from the spec (for example, PUT now answers the DTO, or the code is renamed), stop: `escalate.sh 419 needs:decision "<the difference and the question>"`. Otherwise go on with T1.

### T1: `retentionSchema` and its bounds

- **Red.** Write `retentionSchema.test.ts`, then run `pnpm --filter app test retentionSchema` to see it fail on the missing module:
  - accepts 2, 5 and 30;
  - refuses 1, 31, 2.5, `''` and `NaN`, each with the message `errors.certification.invalid-retention-period`;
  - `retentionYears` is `{ min: 2, max: 30 }`.
- **Green.**
  - Create `retentionSchema.ts`.
  - Move `retentionYears` there and import it in `errorValues.ts`.
  - Run the existing `errorText.test.ts` / `errorCatalog.test.ts` unmodified, and quote them passing.
- `feat(certification): the retention period form's schema`

### T2: `retentionRefusal` by code

- **Red.** Write `retentionErrors.test.ts`:
  - an `ApiError(400, …invalid-retention-period…)` gives `kind: 'field'` with the catalog text "Die Aufbewahrungsfrist ist eine ganze Zahl von 2 bis 30 Jahren.";
  - `capability-missing` and `identity-incomplete` give `kind: 'toast'` with their catalog text and `pharmacyGone: false`;
  - `pharmacy-not-found` gives `kind: 'toast'` with `pharmacyGone: true`;
  - a `TypeError`, a 500 and a 400 without a code give `kind: 'toast'` with `certification.retention.failed`.
- **Green.** Create `retentionErrors.ts` and add the `failed` key in de and en (the catalogs' parity test stays green).
- `feat(certification): say why a retention period is refused`

### T3: The section shows the period, to whoever may read it

- **Red.** Write `ExportCertificatesPage.retention.test.ts`. Its fake host answers `mine` and `GET …/retention-period`, and it should fail because no section exists yet:
  - with `{ years: 5, isDefault: true }`, it shows `h2` "Aufbewahrungsfrist", "Derzeit 5 Jahre (Standard)", the retroactive note, and the field "Aufbewahrungsfrist in Jahren" with the value 5;
  - with `{ years: 7, isDefault: false }`, it shows "Derzeit 7 Jahre" and no "Standard", with the field at 7;
  - the field's accessible description contains the note;
  - on a 403 `capability-missing`, there is no "Aufbewahrungsfrist" heading, no alert, and the GET is asked once (no retry);
  - on a 404 `pharmacy-not-found`, there is no heading and no alert from the section;
  - while the GET is pending, there is no heading;
  - with no pharmacy current, no retention GET is sent;
  - switching to another pharmacy shows that pharmacy's years;
  - switched to English, it shows "Retention period", "Currently 5 years (default)" and the English note.
- **Green.**
  - Create `useRetentionPeriod.ts` (query only) and `RetentionPeriodSection.vue` (display and field, no submit yet).
  - Render the section in `ExportCertificatesPage.vue`, and add the texts `title`, `intro`, `current`, `default`, `retroactive` and `years`.
  - Add the axe data to `ExportCertificatesPage.data.ts`, under the same pharmacy id as the membership example:
    ```ts
    useGetCertificationPharmaciesIdRetentionPeriod: { years: 5, isDefault: true },
    ```
    The existing "passes axe with its example data" tests (both languages, light and dark) now render the section. Quote them passing.
  - `ExportCertificatesPage.test.ts` passes unmodified.
- `feat(certification): show the pharmacy's retention period on the export page`

### T4: Saving a valid period

- **Red.** Add to `ExportCertificatesPage.retention.test.ts`:
  - typing 7 and pressing "Frist speichern" sends one `PUT …/retention-period` with the body `{"years":7}` for the current pharmacy;
  - after the 204, `notify.success` is called with "Die Aufbewahrungsfrist beträgt jetzt 7 Jahre.";
  - the GET is asked again; the host then answering `{ years: 7, isDefault: false }` shows "Derzeit 7 Jahre" without "Standard";
  - while the PUT is out, a double press sends once; the button is `aria-disabled` and `aria-busy` and reads "Wird gespeichert…", and a status region announces it;
  - a button pressed from the keyboard keeps the focus during and after the save.
- **Green.**
  - Add the mutation and `save` to `useRetentionPeriod.ts`, and `submit` to the section.
  - Add the texts `submit`, `saving` and `saved`.
- `feat(certification): a manager sets the pharmacy's retention period`

### T5: Refusals, in the form and from the host

- **Red.** Add to the same test file:
  - entering 31 and pressing the button shows "Die Aufbewahrungsfrist ist eine ganze Zahl von 2 bis 30 Jahren." at the field (`role="alert"`, `aria-invalid`, in the field's description), moves the focus to the field, and sends no PUT;
  - the same holds for 1, for an empty field and for 2.5;
  - in English, the English text shows;
  - a host 400 `invalid-retention-period` shows the same text at the field and focuses it;
  - a 403 `capability-missing` calls `notify.failure` with "Ihre Rolle in dieser Apotheke erlaubt das nicht."; the GET is read again (answering 403 now) and the section disappears;
  - a 404 `pharmacy-not-found` calls `notify.failure` with its text, and reads the GET and `organisation/pharmacies/mine` again;
  - a 500 or a network failure calls `notify.failure` with "Die Aufbewahrungsfrist konnte nicht gespeichert werden. Versuchen Sie es erneut.", the field keeps the value, and pressing again sends again;
  - a field error is cleared when another pharmacy is chosen.
- **Green.** Wire `retentionRefusal` and the focus into the section.
- **Axe.** Add one `test.each` to `ExportCertificatesPage.axe.test.ts`, modelled on the existing field-error test, for light and dark in both languages: with `examples`, fill "Aufbewahrungsfrist in Jahren" with 31, press "Frist speichern", wait for the alert with the catalog text, then run `expect(await axe(container)).toHaveNoViolations()`. The generated mock's mutation is idle, so nothing is sent.
- `feat(certification): say why a retention period was refused`

### T6: e2e `web/app/e2e/certification-retention.spec.ts`

Use the AppHost with no route mocked, and the German UI. The helpers come from `e2e/compliance.ts`: `pharmacyName`, `registerPharmacy`, `accessToken`, `signInTo`. Copy `openExport` from `certification-export.spec.ts` into the new spec, or move it to `e2e/certificationExport.ts` and import it from both.

**Wait for Certification.** It learns of the membership from an event. Poll `GET /api/certification/pharmacies/{id}/retention-period` as the developer until it answers 200, as `waitUntilCertificationKnows` does for the export.

**Test 1:** "a manager sees the default of five years, sets seven and still sees seven after a reload".

1. `registerPharmacy` (the developer becomes its Manager), wait, `openExport`.
2. Expect the heading "Aufbewahrungsfrist", the field at `5` and the text "(Standard)".
3. Fill 7, press "Frist speichern", and expect the toast "Die Aufbewahrungsfrist beträgt jetzt 7 Jahre." and "Derzeit 7 Jahre" without "(Standard)".
4. `page.reload()`, then expect the heading "Nachweise exportieren", the field at `7` and no "(Standard)". The pharmacy stays current through `localStorage`.

**Test 2:** "a period of 31 years shows the field's error and sends nothing".

1. Register a fresh pharmacy and wait.
2. Record every request whose method is `PUT` and whose URL contains `/retention-period`.
3. `openExport`, fill 31, press "Frist speichern".
4. Expect an alert with "Die Aufbewahrungsfrist ist eine ganze Zahl von 2 bis 30 Jahren.", the field with `aria-invalid="true"` and that text as its accessible description, the field focused, the button enabled, and no PUT recorded.

**Not covered**, which the spec's header comment says:

- an employee's 403 (the realm has no second member of a fresh pharmacy; T3/T5 and #418's integration tests cover it);
- the export's content under a changed period (#418's tests);
- two managers at once (last write wins, #418).

**Verify.** Run `pnpm e2e certification-retention` with the AppHost up. Quote the output under `## How it was verified`, together with a manual pass: change the period, reload, see the new value; then `curl` `PUT` with `{"years":31}` as the developer to see the 400 code.

Commit: `test(certification): a manager changes the retention period end to end`

### Done when

- T1–T5's component tests and the axe checks with example data and the retention field error pass in de and en, light and dark.
- T6 passes against the AppHost.
- `pnpm lint`, `pnpm format:check` and `pnpm build` are green.
- `ExportCertificatesPage.test.ts` and `certification-export.spec.ts` pass unmodified.
