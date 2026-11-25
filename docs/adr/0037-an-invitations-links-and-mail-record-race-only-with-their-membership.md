# 0037: An invitation's links and mail record race only with their membership

**Status:** Accepted (#405). Narrows ADR 0002 for these two changes: everything else under a pharmacy still moves the pharmacy's row version.

## Context

- Under ADR 0002, every change beneath an aggregate root moves the root's row version (`RowVersionBump`). Two saves that loaded the same pharmacy therefore race, and the loser answers `409 organisation.pharmacy-changed`.
- An invitation is committed by its request, and its mail is sent afterwards by `InvitationMailRequestedV1Consumer` (ADR 0004). That consumer saves the pharmacy twice: once to issue the invitation's link (its token hash), and once to record that the version was mailed (`RecordInvitationMailed`). It also saves once more to withdraw a link whose mail cannot have gone out.
- So a manager who sent two invitations in quick succession could see the second refused with `409 pharmacy-changed`. The first one's consumer moved the pharmacy's row between the second request's load and its save. Nobody had edited the pharmacy concurrently, so the 409 was correct by ADR 0002 but meaningless to the user. It was found in the review of #403. The owner decided on 2026-11-25 to move these writes out of the pharmacy's concurrency.

## Decision

- **A membership carries a row version of its own** (`xmin` on `organisation.memberships`, through `HasRowVersion()`).
- **Two changes are kept to it:**
  - adding or removing an invitation link (`invitation_links`, marked with `ChangesOnlyItsOwnersRowVersion()` on the owned type);
  - changing only `invitation_mailed_for` (the property is marked the same way).

  For such a change, `RowVersionBump` moves the nearest row version above the changed row, the membership's, and stops there. A save of only these changes checks and moves the membership's row and leaves the pharmacy's alone.
- **Everything else is unchanged.** An invite, renewal, accept, revoke, end or change of a membership modifies the membership or adds one, so it still moves the pharmacy's row, as ADR 0002 has it. Since the membership's row version is checked on every update of that row, these changes now also race with the consumer's writes to the same membership. Each such race is a real one: a renewal, accept or revoke and an issued link change the same invitation. Its loser answers or retries as before.
- **The rule is the building block's, not Organisation's:** `RowVersion.ChangesOnlyItsOwnersRowVersion` on a property or an owned type, and `RowVersionBump` honouring it. Another module may keep a change to an entity's own row the same way, with its own ADR. An entry is kept to its owner only when its type is marked, or when every property it changed is marked; one unmarked change in the same entry moves the root as before. The marker needs an entity type with exactly one foreign key, the one the change climbs through: with two, the nearest row version would depend on the model's key order and could be another aggregate's. `OwnersOnlyRowVersionCheck`, a convention every `ModuleDbContext` adds, refuses the marker on a type with more than one foreign key, or on a property of one, when the model is built (#413).
- **The Domain is untouched.** `Pharmacy.IssueInvitationLink`, `WithdrawInvitationLink` and `RecordInvitationMailed` keep their checks: the invitation must be open, at its current version, under the link cap. Only which row a save checks changes.

## Consequences

- Two invitations of one pharmacy, one after the other, are both created, whenever the first one's mail goes out. `OrganisationSequentialInvitationsTests` shows the consumer leaves the pharmacy's `xmin` unchanged and the second invitation answers 201. An attempt in which the mail was already recorded when the row was first read shows nothing, so it is started again with a new pharmacy, up to five times, and the test fails if none observed the consumer (#413). `InvitationMailRowVersionTests` shows which row each change updates, without a database.
- Concurrent invitations of one pharmacy still race one another on its row (`OrganisationInvitationBurstTests`). Their mail consumers no longer take part in that race.
- Within a single invitation, the consumer and a renewal, accept or revoke race on the membership's row instead of the pharmacy's. Whichever loses fails as before: the consumer's lost commit throws and Wolverine retries it, and the use case answers 409.
- Two consumers of different invitations in one pharmacy no longer race each other at all.
- The migration `GuardInvitationsByTheirMembership` only records `xmin` in the model. It is a system column, so no column is added.
