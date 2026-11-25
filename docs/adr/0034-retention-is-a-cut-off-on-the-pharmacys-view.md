# 0034: Retention is a cut-off on the pharmacy's view, not a deletion

**Status:** Accepted (#53). Amends ADR 0032, which said "Retention (#53) is untouched: nothing is written".

## Context

- The decision "Aufbewahrung" (docs/domain/README.md) keeps certificates for the pharmacy five years from issue, then the pharmacy's copy goes; the person's certificates stay in their account. #53 asked for "a daily job [that] removes the pharmacy's export entries five years after issue".
- ADR 0032 (#52) stores no pharmacy copy. The export is computed on each request from `certification.certificates`, the person's own certificates, and Certification's copies of the memberships. There is nothing pharmacy-owned for a job to delete: deleting certificates would take them from the person, and deleting memberships would break the access checks and is not per certificate.
- The owner decided on 2026-11-24 (issue #53, option A): retention is a cut-off on the view, using `TimeProvider`, with no job and no migration. Storing a per-pharmacy copy with a daily purge (option B) was rejected because it rebuilds #52.

## Decision

- **A pharmacy sees a certificate until five years after its issue, to the instant.** `Certificate.IsKeptByPharmaciesAt(now)` is true while `IssuedAt + 5 years > now` (`DateTimeOffset.AddYears(5)`, so 29 February counts to 28 February five years on). At `IssuedAt + 5 years` exactly the certificate is out. `now` comes from `TimeProvider`, which `ExportPharmacyCertificatesHandler` takes by constructor. Tests use `FakeTimeProvider`: a certificate at 4 years 364 days is in, and one at exactly 5 years is out.
- **The export applies it whatever period is asked for.** A certificate the pharmacy no longer keeps is left out like one outside the membership. A period that lies wholly before the cut-off is valid and gives an empty export (`index.csv` with its header alone), not an error.
- **The cut-off applies before the 2000-certificate limit is counted.** A certificate past the cut-off neither appears in the export nor makes it `400 certification.export-too-large`. Since the selection moved into the database (#367, PR #383, which reads at most 2001 rows), the cut-off is part of that query, so the 2001 counts only certificates kept: `ICertificateStore.IssuedInPharmacyAsync` takes `now` and applies `(issued_at AT TIME ZONE 'UTC') + make_interval(years => 5) > (now AT TIME ZONE 'UTC')`, which is `IssuedAt.AddYears(5) > now` in SQL (PostgreSQL, like `AddYears`, takes 29 February to 28 February). It is not written as the lower bound `issued_at > now - 5 years`, because around 29 February no single lower bound matches `AddYears`: issued on 28 February 2024 at 13:00 UTC, a certificate is kept until 28 February 2029 at 13:00, while one issued on 29 February 2024 at 01:00 is kept only until 28 February 2029 at 01:00. Computing the bound from `now` instead would drop a certificate on 28 February of a leap year and show it again on 29 February. The condition cannot use an index on `issued_at`. The period's range on `issued_at` and the membership check narrow the rows anyway.
- **Nothing is deleted, stored or scheduled.** There is no job, no table and no migration. The person's certificates, their PDFs, their verification (`/certification/verify/{code}`) and "Meine Bestätigungen" are not affected. Deleting the person's data (DSGVO) stays #115.
- **Five years is fixed for now.** The decision also allows a longer period chosen by the pharmacy (never under two years). That setting is a follow-up issue: set by the manager through a capability, default 5, minimum 2, with an upper bound for the owner to decide. Until then `Certificate` holds the five years as a constant.

## Consequences

- The retention boundary moves with the clock alone. Nothing has to run for it to hold, and nothing is lost if the service is down.
- A pharmacy that exported before the cut-off keeps whatever it downloaded. The cut-off governs what the service hands out, not files outside it.
- Any future pharmacy view of certificates (a list, a dashboard) applies `IsKeptByPharmaciesAt` too. The person's own views never do.
- The per-pharmacy period replaces the constant with a value read per pharmacy. Its default of five keeps today's behaviour.
