# 0019: Test access follows the latest lock or relearned content

**Status:** Accepted (#32). The owner decided on 2026-11-23, on #32, that an assignment locked after three failed attempts stays locked until the learner has worked through the content again (`ContentCompleted`), not until a new session starts (decision (b)). This overrides the domain README where they differed.

## Context

- Compliance shows an assignment as locked after `AttemptsExhaustedV1` (Assessment) and must show it unlocked again once `ContentCompletedV1` (Learning) reports the content worked through again. It also puts an assignment in progress on `LearningSessionStartedV1` (Learning).
- None of the three events carries a cycle or attempt counter, and the `SessionId` does not tell cycles apart: after `AttemptsExhausted` the learner goes on in the same session, Learning reopens the failed chapters and raises a new `ContentCompleted` there (ADR 0010; ADR 0012 on `agent/45`: one examination per session). Within a session the true order is `CC1 < AE1 < CC2 < AE2 < …`, but the payload does not say which cycle a `ContentCompletedV1` belongs to.
- Wolverine delivers one queue's messages in parallel and retries a failed one later, and a dead letter can be replayed: the events may arrive in any order, twice, or long after.
- Every event carries `Metadata.OccurredAt`, set by the publisher's unit of work when its handler publishes; a redelivery or a replay carries the same metadata. Each step is caused by the one before it through a broker hop and a human action (Assessment opens the examination on `CC_n`, the learner fails three attempts; Learning reopens chapters on `AE_n`, the learner works through them again), so the gaps are seconds to minutes, while the publishers' NTP-synced container clocks differ by milliseconds. Compliance already compares another service's clock with its own: `Assignment.Complete` refuses a certificate issued before the assignment.

## Decision

- **D1. Test access is a last-writer-wins register over `Metadata.OccurredAt`.** `Assignment.TestAccessChangedAt` (column `compliance.assignments.test_access_changed_at`, nullable) holds the instant of the lock or unlock last applied. `MarkLocked(at)` (`AttemptsExhaustedV1`) applies unless `at` is earlier than it; `Unlock(at)` (`ContentCompletedV1`) applies unless `at` is earlier than it or equal to it. On equal instants the lock wins. So a `ContentCompletedV1` of the same cycle never unlocks a later lock, whatever order they arrive in, and every set of these events ends in the same state in every order of arrival; a second delivery or an old replay changes nothing.
- **D2. A start no longer unlocks.** `MarkStarted(at)` (`LearningSessionStartedV1`) puts an open assignment in progress; the earliest start wins.
- **D3. Events that happened before the assignment was assigned do not concern it**, and a completed or cancelled assignment ignores all three. A lock while the assignment is still open is kept: it shows once the assignment is in progress, so a lock that overtakes the start is harmless.
- **D4. An event reaches every active assignment of the person and course, in every pharmacy.** One learning session serves the learner's assignments of the course in all their pharmacies, as a certificate completes them all. `SessionId` and `CourseVersionId` select nothing (any version's session counts), but are guarded as not empty. With no active assignment the consumer returns without committing.
- **D5. The consumers do not deduplicate on `Metadata.EventIdentifier`.** The register makes a redelivery, a replay and a reordering change nothing by themselves, which also covers an old event under a new identifier. A save that lost a race to another event of the same assignment (row version, ADR 0002) throws, and Wolverine's retry reloads and applies again.

## Rejected

- **A cycle number in V2 events.** It would change the contracts and their publishers in Learning and Assessment, so it needs an owner decision, and it buys nothing over `OccurredAt` at today's clocks.
- **Counting deduplicated `ContentCompleted` and `AttemptsExhausted` events per session.** It is clock-free, but it needs an insert-only table, a rule for several sessions of one course (two versions, a later run), and a rule for which sessions belong to which assignment, which needs time again.

## Consequences

- Clock skew between Learning and Assessment larger than the human-scale gap between a completion and the lock that follows it would misorder them. Should that ever be possible, the fix is the owner's decision on V2 events with a cycle number, not a change in Compliance.
- A session started before the assignment was assigned leaves the assignment open until a certificate arrives, because Learning announces a session once, at its creation. A lock or unlock after the assignment is still recorded and shows once the assignment is in progress.
- No integration event goes out: none is announced for a start, a lock or an unlock.
- `0012`, `0016`, `0017` and `0019` were held by open branches when this was written. `0018` was taken by #244 before #32 merged, so this is `0019`; if `0019` is taken by the time #32 merges, this file and every reference to it (`Assignment`'s and the three consumers' docs, the domain README and reference) are renumbered.
