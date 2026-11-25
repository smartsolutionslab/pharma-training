# 0042: Demo data is seeded through the edge: by the AppHost locally, by a staging-only workflow on staging

**Status:** Accepted (#457; the owner decided the open questions on 2026-11-25). This ADR covers both environments. The local part shipped first (#460): the seeder, the AppHost's run-mode resource and the program's guard. The second pull request adds the staging part: the auth proxy's admin prefixes, the staging workflow and its deploy job, the production guard against the edge's reported deployment, and the checks of every address a credential is sent to.

## Context

- A demo, a manual test or a screenshot needs pharmacies, members, published courses and assignments in every state. Until now they were made by hand, each time, on each machine and on staging.
- The services own their data in databases of their own ("Topology: services"), and learn of each other only through integration events. A script that wrote rows into those databases would skip every rule the modules keep, and every event the other services need: Compliance would never hear of a course, Learning of a member, Certification of a pass.
- The published realm refuses the password grant on every client (`PublishedRealmTests`). So a seeder cannot sign users in with a direct grant on staging.
- Compliance refuses a due date before today in Germany (`DueDateInThePast`), and no endpoint backdates an assignment.
- On staging the auth proxy forwards the realm and its static resources only; `/admin/**` answers 404 (`src/AppHost/Wiring/AuthProxy.cs`). The admin API cannot be reached from a GitHub runner, so neither the seeder nor X1 can create users there.
- No endpoint deletes pharmacies, courses, assignments or certificates.

## Decision

- **A console app, `src/DemoSeeder`, seeds through the edge's public API alone.** Every call goes to `{EdgeUrl}/api/...`, as the person the data names, with that person's own token: the author drafts, the manager invites and assigns, the learner learns and takes the examination. It references no module, Api or Contracts project, and speaks HTTP with JSON it reads by field, as the e2e helpers do. Every route it calls is in `web/app/openapi.json`, which a unit test checks. No step writes a database, so every rule and every event applies to demo data as to real data.
- **The plan is data:** `SeedPlan.Demo` lists the users, pharmacies, courses and scenarios. Everything is named as demo data: users `demo-*`, addresses `demo-*@example.com`, pharmacies `Demo-…`, courses `Demo: …`. Nothing collides with the e2e or integration users.
- **Users are ensured through Keycloak's admin API:** created if missing, enabled, with a verified address, their names and their realm role. Locally that is done as the master realm's administrator (`admin-cli`), whose password is the AppHost's generated `keycloak-password` parameter. On staging it is done as a confidential client of its own.
- **Owner decisions (2026-11-25):**
  1. **Admin API on staging.** The auth proxy forwards exactly `/admin/realms/pharma-training/users/**` and `/admin/realms/pharma-training/roles/**`, and no other admin path, with a test pinning the prefixes. Such a call still needs a token with `manage-users`. This also fixes X1's user setup, which has the same gap.
  2. **Sign-in is the authorization-code flow with PKCE, locally and on staging.** The seeder signs each user in against the realm's web client (the authority and client id come from the edge's `/config.json`) without a browser. It fetches the login page, posts the username and password to the form's action with the page's cookies, reads the code from the redirect without following it, and exchanges the code with the verifier. It never uses the password grant, so the published realm stays as it is, and a local run exercises the staging path. A page other than the redirect fails the run, naming the user and the page's title, never its content.
  3. **Overdue is "due today".** An assignment the plan wants overdue is assigned due today in Germany. It shows Open on the day of the seed and Overdue from the next day. Backdating is not part of this decision.
  4. **A separate `demo-seeder` client on staging.** It is created by the runbook with `kcadm.sh` inside staging's Keycloak, and its service account holds realm-management's `manage-users` and `view-users` alone; the seeder asks for its token with the `client_credentials` grant at the realm's token endpoint. A realm role is granted from the roles the user may still be given (`role-mappings/realm/available`), never read by name, which would ask `view-realm` too. Its secret is a staging environment secret, and it never appears in a realm file. A client separate from `x1-runner` lets either be rotated or removed alone.
  5. **One password for the staging demo users:** a generated `DEMO_USERS_PASSWORD`, set on create and reset on every run, so rotating the secret takes effect at the next run. Locally, like the realm's local users, a demo user's password is their username.
- **Settings and exit codes.** The seeder reads `DemoSeeder:*`: `Target`, `EdgeUrl`, `WebOrigin`, `MailpitUrl`, `MailpitAuth`, `KeycloakAdminUrl`, `AdminUser`, `AdminPassword`, `ClientId`, `ClientSecret`, `UserPassword` and `PollTimeout`. It exits 0 when seeded, or when there was nothing to seed. It exits 1 when a step failed, naming the call, the user and the error code; whatever else a step throws (a timeout, a number or address that is not one, a JSON value of another kind) also ends with 1, named by the last request's method and path and the kind of failure, never the runtime's 134 and never the exception's message. It exits 2 when the settings or the edge's configuration were refused, naming the key and never its value. Its last line on success is `Demo data: <n> created`.
- **Production guard, in the program.** `Target` must be `Local` or `Staging`. Anything else, `Production` included, and an absent target, exits 2 before any call. Production's edge is https as staging's is, so the target alone cannot tell them apart:
  - **The edge reports its deployment.** `/api/config.json` answers `environment`, from the edge's `Edge:Environment`, which the AppHost sets in every mode: the environment's name when published (`aspire publish -e staging|production`), `local` in run mode, and `unknown` when unset. With the target `Staging`, the seeder's first request is `GET /api/config.json`, and it exits 2 unless `environment` is `staging`, before any credential is sent.
- **Credential checks, in the program.** Every address that receives a credential is checked before it is sent, and a refusal is exit 2:
  - `Local`: the edge, `KeycloakAdminUrl` (the master realm's administrator), `MailpitUrl` and the `oidcAuthority` the edge reports (where every demo user's password goes) must be loopback.
  - `Staging`: the edge, `KeycloakAdminUrl` (staging's Keycloak behind the auth proxy) and `MailpitUrl` must be https; `MailpitAuth`, `ClientSecret` and `UserPassword` are required; the `oidcAuthority` the edge reports must be https on `KeycloakAdminUrl`'s host; and any administrator user or password is refused, since on staging users are administered through the client alone.
  - The AppHost refuses a `DemoSeeder:Enabled` that is not a bool, naming the key, rather than run with the seeder on or off by a guess.
- **Locally, the AppHost runs it.** `src/AppHost/Wiring/DemoSeeding.cs` adds the project resource `demo-seeder` inside the run-mode block only. It waits for the edge (which waits for every service), Keycloak and the web app, and gets the local settings. It is on by default and off with `DemoSeeder:Enabled=false`. The integration fixture switches it off; the seeder's own integration test runs it twice as a process against the fixture.
- **On staging, a staging-only workflow runs it:** `.github/workflows/demo-seed-staging.yml`, started by hand (`workflow_dispatch`) or by `deploy.yml`'s job `demo-seed` after the staging job (`workflow_call`), with no input, so nothing names another environment. Its one job names `environment: staging`, reads `contents` alone, runs one at a time (`demo-seed-staging`), and sets `DemoSeeder__Target: Staging` with the edge (`vars.FRONTEND_URL`, also the web origin), Keycloak and Mailpit (`vars.X1_KEYCLOAK_URL`, `vars.X1_MAILPIT_URL`) and the staging secrets `DEMO_SEEDER_SECRET`, `DEMO_USERS_PASSWORD` and `MAILPIT_UI_AUTH`, and no other secret. `deploy.yml` passes it no secrets, never `inherit`. It uploads nothing and exports no telemetry, since either would carry the secrets off the runner. The production job neither calls nor needs it. The architecture rules `DemoSeedRunsOnStagingAlone`, `StagingEndToEndRunKeepsNoTraces` and `StagingEndToEndRunTakesNoSecretsFromItsCaller` pin all of this.
- **Staging reads Mailpit with its basic auth** (`MailpitAuth`, `user:password`, as staging's Mailpit takes it), and sets every demo user's password to `UserPassword` on every run (decision 5).
- **Idempotency: look up, then act.** Each step reads through the API before it writes:
  - users by exact username;
  - pharmacies and memberships from `GET /organisation/pharmacies/mine`;
  - open invitations from the pharmacy's invitation list, accepted with the token of the newest mail, never invited again;
  - course versions by title and number, a draft completed with only its missing chapters and questions, matched by title and text;
  - certificates by course title;
  - assignments from the learner's `GET /compliance/me/assignments`;
  - examinations by session.
  - A seeded application therefore gets no write to the edge on a second run, and no mail is read. A run that stopped half-way is continued from what the API shows. An assignment InProgress counts as done for a learner who starts or fails once, and one Locked for a learner who exhausts the examination; such a session is never resumed with a POST. An exhausted examination never gets another progress report, which would open a new round.
- **Eventual consistency is polled, by code (docs/adr/0027), on the injected `TimeProvider`.** The seeder polls on these answers, with a back-off up to `PollTimeout`:
  - `404 compliance.course-not-found`, `403 learning.membership-missing`, `404 learning.course-version-not-found` and `404 assessment.examination-not-found`;
  - an examination still Locked;
  - a member Compliance does not list yet;
  - a mail Mailpit has not caught yet;
  - and, after a learner acted, the certificate with its document and Compliance's assignment state.
  - Every other refusal stops the run. A POST is sent once and never retried. A call answered 401 ran nothing, so it is sent again after a new sign-in.

## Consequences

- Demo data obeys every rule: four eyes holds whether it is on or off, since author, approver and publisher are three people. The blueprint is set before any approval (docs/adr/0007). Invitations stay within ADR 0004's default limits: 29 addresses, each mailed once, at most 22 a day per pharmacy and per manager.
- **Nothing is deleted.** Locally, starting over means removing the volumes. On staging, demo data stays until the environment is rebuilt, as X1's data does.
- **Gaps left open:**
  - No PDF or video chapters: those need a blob upload, and video progress is bounded by playback time (docs/adr/0024).
  - No content review or test draft, which call the model (docs/adr/0020).
  - No assignment overdue on the day of the seed.
- The recurring assignment's follow-up is created by Compliance itself (docs/adr/0026), once per completion. The seeder never creates it.
- The staging run trusts what staging's edge reports about itself. An edge that lied would have to be one the deploy published with `-e staging`, or one that answers in its place at `vars.FRONTEND_URL`; both are outside what the program can check, and the realm it names must still be https on Keycloak's host.
- A change to an endpoint the seeder calls shows in its unit tests, which fake the edge's routes, and in `DemoSeederTests`, which runs it against the whole application.
