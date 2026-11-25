# 0040: Login events expire after 30 days

**Status:** Accepted (#448; the owner decided on 2026-11-25 that login events expire, and left the period to this ADR).

## Context

- The published realm records user events (`eventsEnabled`), so a failed login, a lockout or a password reset can be traced.
- Each event names the user, their username and the IP address the request came from. That is personal data.
- Keycloak keeps such events for ever unless the realm sets `eventsExpiration`, in seconds. The published realm set none.
- A person erased under GDPR Art. 17 (#115) would then stay in Keycloak's event store with their username and addresses, long after their account and every module's data are gone.
- Storage limitation (GDPR Art. 5(1)(e)) asks that such data be kept no longer than its purpose needs. The purpose here is security monitoring: noticing a brute-force or credential-stuffing attempt, and following up an incident someone reports.

## Decision

- **Both realms set `eventsExpiration` to 2,592,000 seconds, 30 days.**
  - `src/AppHost/keycloak/realm.json` is the published realm.
  - `src/AppHost/realms/realm.json` is the development realm. It records no events, and keeps the setting so the two realms stay equal beyond their pinned differences (`PublishedRealmTests`).
- **Why 30 days:**
  - **Long enough for monitoring.** An attack against logins shows within hours, and brute-force detection locks an account out at once (`failureFactor`). The time that matters is the time to notice and follow up: someone reports a strange login, or an operator reads the events after a lockout. Thirty days covers a month's reporting cycle, a holiday and a weekend on either side.
  - **Short enough to forget.** An erased person's last events are gone within a month of the erasure. Longer periods, 90 days or a year, would serve forensic work that the application has no process for. They would keep every user's addresses for a quarter or a year for that.
- **What enforces it:** `PublishedRealmTests.Login_events_expire_within_the_period_the_adr_records` fails, for either realm, if the setting is missing or longer than 30 days. A shorter period passes. A longer one needs this ADR changed first.
- **The owner may change it.** This period is the ADR's choice, recorded with its reason. A different one is a change to this ADR and to the test's constant together.

## Consequences

- Keycloak removes user events older than 30 days on its own, in its scheduled clean-up. Nothing else in the application reads them.
- **The realm is imported once,** on Keycloak's first start, so an environment whose realm was imported before this change keeps no expiry until it is set by hand. The runbook says how (README, "What to check after the first deploy", check 13): `./kcadm.sh update realms/pharma-training -s eventsExpiration=2592000`.
- **Admin events** (`adminEventsEnabled`, with details) are not covered. Keycloak keeps them under a setting of their own (`adminEventsExpiration`), and they record what an administrator changed, sometimes with a user's representation. Whether they expire, and when, is a separate decision. It is left open here, and the runbook does not set it.
- **An investigation that needs events older than 30 days cannot have them.** If one is ever needed, the events must be exported before they expire, by hand, and kept under that investigation's own retention.
