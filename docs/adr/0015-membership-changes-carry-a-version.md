# 0015: Membership changes carry a version

**Status:** Accepted (#29). The owner decided on 2026-11-21, on #29, that a change of a member's role reaches Compliance, carrying a version, and that erasure stays with #115 (decision (b)).

## Context

- Compliance decides who may assign courses in a pharmacy from its own copy of the caller's membership (ADR 0003). Until #29 that copy was `MembershipStartedV1` alone: Compliance stored it once and never changed it, so a manager demoted in Organisation kept assigning, and an employee promoted there could not.
- Organisation raised `MembershipChanged` but published nothing for it. The event carried neither the member's name nor any order: two changes of one membership could arrive at Compliance in either order, and a change could arrive before the start it changes, since Wolverine delivers one queue's messages in parallel and retries a failed one later.
- `MembershipStartedV1` is published and may not be edited (AGENTS.md, integration events). A membership starts exactly once (`Founder` or `Activate`; an ended membership is never reactivated, a re-invited person gets a new `MembershipId`), and every later change goes through `Pharmacy.ChangeMembership`.
- Compliance's copies are insert-only (#28): a member, an ending, a course version and a retirement are each stored once as they arrived, and two deliveries of one event race only on its primary key.

## Decision

- **D1. `MembershipChangedV1` carries the membership's version; the start is version 1.** `src/Contracts/Organisation/MembershipChangedV1.cs`: `(MembershipId, PersonId, PharmacyId, DisplayName, JobFunction, Role, Version)`. `Version` counts the membership's states from 1 at its start, so the first change is 2. Person and pharmacy are carried so a change that overtakes its start is a complete row by itself, in the shape of `MembershipEndedV1`. `MembershipStartedV1` stays as it is: the only event without a version is the one that is always first.
- **D2. Organisation keeps the version on the membership.** `Membership.Version` (`MembershipVersion`, the shape of `InvitationVersion`) is `First` when the membership is invited, founded or activated, and `Change` moves it to the next; a change to the values a membership already has changes nothing and keeps the version. `MembershipChanged` carries the name and the version. Column `organisation.memberships.version`, default 1: no change was announced before, so every existing row is at version 1. `ChangeMembership` publishes `MembershipChangedV1` before `TryCommitAsync`, so a save that lost a race saves and announces nothing, and two changes of one pharmacy serialise on its row version (ADR 0002): one membership never announces one version twice. The broker needs nothing by hand (ADR 0011 D2): the new exchange is Organisation's because the event is in its contract namespace, and Compliance's queue is bound to it because Compliance has a consumer.
- **D3. Compliance stores each change as a row of its own and resolves the current state when it reads.** `compliance.membership_changes`, key `(membership_id, version)`, no foreign key to `members`. `MembershipChangedV1Consumer` refuses a version below 2 and deduplicates on the key. `CurrentMembership.Of(member, changes)` takes name, job function and role from the change with the highest version, else from the member at version 1; person and pharmacy come from the member. So a change older than one already stored is kept but never wins, and a change that overtakes its start wins as soon as the member arrives. `Capability` and `RoleCapabilities` in Compliance's Domain answer what the current role grants, the same way Organisation's do (ADR 0003).

## Rejected

- **`MembershipStartedV2` with a version.** It would double Organisation's publishing and every consumer of the start (Compliance, Certification) for a value that is always 1.
- **Updating Compliance's member in place.** It needs a conditional update ("only if the stored version is lower") per field, and a change that overtakes its start would be lost or would need a placeholder member when the two consumers interleave. Insert-only rows race only on their primary key, as #28's copies do.

## Consequences

- Certification may consume `MembershipChangedV1` later the same way, once it needs a member's current name or role.
- A member's name now lives in a second Compliance table, `membership_changes.display_name`. The erasure of #115 must erase it there too.
- Reading a membership joins its changes; every read is by membership, the key's prefix, so no further index is needed. The rows of a membership are few: one per change of job function or role.
- `0012` and `0014` were held by open branches when this was written. If `0015` is taken by the time #29 merges, this file and every reference to it (`ChangeMembershipHandler`'s doc, `MembershipChangedV1Consumer`'s doc, `CurrentMembership`'s doc, `IMemberStore`'s doc, the domain README and reference) are renumbered.
