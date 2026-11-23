# 0017: Learning admits active members from a copy without names

**Status:** Accepted (#194). The owner chose on 2026-11-23, on #194, that Learning keeps its own copy of the memberships (option (b)).

## Context

- Until #194 every learner endpoint was open to any signed-in person: `LearningSessionEndpoints` carried only `RequireAuthorization()`, and its summary said "Open to every signed-in person, since Learning holds no memberships". The issue asks that only active pharmacy members start and work on learning sessions, and that a session already running stays readable to its owner.
- Membership is no realm role, so the check belongs in the handler, not in a policy (ADR 0003; `OnlyModulePoliciesCheckRoles`).
- A session is a person's, not a pharmacy's: `LearningSession` carries no pharmacy, and its id is derived from learner, version and run only (ADR 0010). The domain says the same: "Nachweise gehören der Person".
- Organisation publishes `MembershipStartedV1`, `MembershipChangedV1` (version 2 and up; version 1 is the start) and `MembershipEndedV1`. A membership starts once, an ended one is never reactivated, and only an active one is changed (ADR 0015). A person may be an active member of several pharmacies at once.
- Compliance already keeps an insert-only copy of the memberships (ADR 0015 D3). Learning's Domain is event-sourced and references only the BCL, Kernel and `BuildingBlocks.EventSourcing`, so Compliance's `Entity<T>` members cannot move into it as they are.

## Decision

- **D1. An active member is a person with at least one membership, in any pharmacy, that has started and not ended, in Learning's copy.** A membership has started once its `MembershipStartedV1` or any of its `MembershipChangedV1` has arrived: Organisation changes only active memberships, and a change names person and pharmacy, so it proves the start by itself, and a late or dead-lettered start does not lock a member out. A membership has ended once its `MembershipEndedV1` has arrived, whatever arrived before or after it. The pharmacy is not consulted: sessions are not pharmacy-scoped (ADR 0010), so a person who moves from one pharmacy to another keeps working. `PersonMemberships` (Application) decides it from the person's rows, as a pure function; `LearnerAccess.RequireActiveMemberAsync` asks it.
- **D2. Refusal.** Anyone else is refused with `403 learning.membership-missing` ("You are not an active member of any pharmacy; a membership that just started may not have reached Learning yet."). It carries no person id: the caller is themself.
- **D3. What an ended member may still do.**

  | Endpoint | Active member | Ended member, own session | Never a member, or someone else's or an unknown id |
  | --- | --- | --- | --- |
  | `POST /learning/sessions` (start or resume) | as before | 403 | 403 |
  | `GET /learning/sessions/{id}` | as before | 200 (running or finished) | 403 (an active member gets 404, as before) |
  | `POST /learning/sessions/{id}/progress` (#206) | as before | 403 | 403 |

  Reading stays: the session is the person's own record, and hiding it serves nobody. Progress and resume stop: they are "work", and progress leads to an examination and a certificate, which a person who has left every pharmacy should not produce. Start checks the membership before it looks the version up, so a non-member cannot probe which versions Learning holds. Read loads the session first and answers the caller's own session whatever their membership; only on the other path is the membership read, so a non-member gets the same 403 for every id but their own.
- **D4. The copy.** Three insert-only tables in schema `learning`, with no foreign keys between them, each with an index on `person_id`:

  | Table | Key | Columns |
  | --- | --- | --- |
  | `members` | `id` | `id`, `person_id`, `pharmacy_id` |
  | `membership_changes` | `(membership_id, version)` | `membership_id`, `version`, `person_id`, `pharmacy_id` |
  | `membership_endings` | `id` | `id`, `person_id`, `pharmacy_id` |

  Each consumer deduplicates on its primary key (membership id, or membership id and version), not on `Metadata.EventIdentifier`, so a republish under a new identifier is absorbed too; the loser of a race fails its insert and Wolverine's retry finds the row. The changed consumer refuses a version below 2. Nothing Learning keeps changes with a version, since person and pharmacy are fixed for a membership's life: the version is stored only as the second half of a change's key, and no "current" state is resolved (Compliance's `CurrentMembership.Of` is not copied).
- **Not stored:** display name, job function and role. MVP 1 gates no Learning feature by role, and Compliance decides who is assigned what. Without a name Learning's tables hold nothing for the erasure of #115.

## Rejected

- **Asking Organisation or Compliance at request time.** Services do not call each other in process (topology: services); a synchronous call would make learning depend on another service being up.
- **A policy over a realm role.** Membership is per pharmacy and changes at runtime; it is no realm role (ADR 0003).
- **Scoping the check to a pharmacy.** A session carries no pharmacy (ADR 0010); "member of any pharmacy" is the rule that matches it.

## Consequences

- The events' JSON, with the member's name, still sits in Learning's Wolverine inbox (`wolverine.wolverine_incoming_envelopes`) until it is cleaned, and in `wolverine.wolverine_dead_letters` when one is dead-lettered: places the erasure of #115 must reach, as for Compliance (ADR 0015).
- A membership started before this was deployed is unknown to Learning until it is republished. No deployed database exists yet.
- Between accepting an invitation and the start reaching Learning, the new member is refused for a moment; the message says so.
- A future use of role or job function in Learning needs the copy widened and the memberships republished.
- Learning's broker user gains read and configure on the three Organisation exchanges by derivation (ADR 0011 D2), with no AppHost change.
- `0016` was held by #206 when this was written. If `0017` is taken by the time #194 merges, this file and every reference to it (the doc comments in Learning's Application and Api) are renumbered.
