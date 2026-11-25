# 0040: Login and admin events expire after 30 days

**Status:** Accepted (#448; the owner decided on 2026-11-25 that login events expire, and left the period to this ADR). Amended by #453: the owner decided the same day that admin events expire after 30 days too.

## Context

- The published realm records user events (`eventsEnabled`), so a failed login, a lockout or a password reset can be traced.
- Each event names the user, their username and the IP address the request came from. That is personal data.
- Keycloak keeps such events for ever unless the realm sets `eventsExpiration`, in seconds. The published realm set none.
- A person erased under GDPR Art. 17 (#115) would then stay in Keycloak's event store with their username and addresses, long after their account and every module's data are gone.
- The published realm also records admin events (`adminEventsEnabled`): what an administrator changed, sometimes with the user's representation (name, email). Keycloak keeps those for ever too unless the realm sets their own expiry, the realm attribute `adminEventsExpiration`, in seconds.
- Storage limitation (GDPR Art. 5(1)(e)) asks that such data be kept no longer than its purpose needs. The purpose here is security monitoring: noticing a brute-force or credential-stuffing attempt, and following up an incident someone reports.

## Decision

- **Both realms set `eventsExpiration` to 2,592,000 seconds, 30 days, and the realm attribute `adminEventsExpiration` to the same (#453).**
  - `src/AppHost/keycloak/realm.json` is the published realm.
  - `src/AppHost/realms/realm.json` is the development realm. It records no events, and keeps both settings so the two realms stay equal beyond their pinned differences (`PublishedRealmTests`).
  - Admin events' expiry is a realm attribute, `"attributes": { "adminEventsExpiration": "2592000" }`, not a field of its own: Keycloak 26.6 refuses a top-level `adminEventsExpiration` in an imported realm ("Unrecognized field"), and its event store reads the attribute when it removes expired admin events.
- **Why 30 days:**
  - **Long enough for monitoring.** An attack against logins shows within hours, and brute-force detection locks an account out at once (`failureFactor`). The time that matters is the time to notice and follow up: someone reports a strange login, or an operator reads the events after a lockout. Thirty days covers a month's reporting cycle, a holiday and a weekend on either side.
  - **Short enough to forget.** An erased person's last events are gone within a month of the erasure. Longer periods, 90 days or a year, would serve forensic work that the application has no process for. They would keep every user's addresses for a quarter or a year for that.
- **The same period for admin events.** They serve the same monitoring, after an incident or a strange change to a user, and hold the same kind of personal data, so the reasons above hold for them as well.
- **What enforces it:** `PublishedRealmTests.Login_events_expire_within_the_period_the_adr_records` and `Admin_events_expire_within_the_period_the_adr_records` fail, for either realm, if their setting is missing or longer than 30 days. A shorter period passes. A longer one needs this ADR changed first.
- **The owner may change it.** This period is the ADR's choice, recorded with its reason. A different one is a change to this ADR and to the tests' constant together.

## Consequences

- Keycloak removes user and admin events older than 30 days on its own, in its scheduled clean-up. Nothing else in the application reads them.
- **The realm is imported once,** on Keycloak's first start, so an environment whose realm was imported before this change keeps no expiry until it is set by hand. The runbook says how (README, "What to check after the first deploy", check 13): `./kcadm.sh update realms/pharma-training -s eventsExpiration=2592000 -s attributes.adminEventsExpiration=2592000`.
- **An admin event older than 30 days is gone too.** Who changed a role or a client a quarter ago cannot be read from Keycloak; a change that matters for longer belongs in the change's own record (its pull request, the deployment's log).
- **An investigation that needs events older than 30 days cannot have them.** If one is ever needed, the events must be exported before they expire, by hand, and kept under that investigation's own retention.
