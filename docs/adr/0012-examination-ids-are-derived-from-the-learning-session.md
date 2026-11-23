# 0012: Examination ids are derived from the learning session

**Status:** Accepted (#45).

## Context

- The README's aggregate boundary for Assessment is one examination per learning session ("Aggregatgrenze ist die Prüfung pro LearningSession", `docs/domain/README.md:214`). Learning's `ContentCompletedV1` opens it the first time and unlocks it again after three failed attempts.
- RabbitMQ delivers at least once, and Wolverine retries a consumer that throws. Two deliveries of the first `ContentCompletedV1` can run at the same time, and every handler must be idempotent and tolerate events out of order (`docs/domain/README.md:178`).
- AGENTS.md (Marten) says that a command which creates an aggregate looks for its id first, and that the store refuses a second stream with the same id. That backstop works only when both creators create the *same* stream id. With random ids (`ExaminationId.New()`), two concurrent first deliveries would open two examinations for one session.
- `Examination.Unlock` is a no-op unless the examination is exhausted. The state alone does not make the consumer idempotent: a redelivery, or a replay of a dead-lettered `ContentCompletedV1` (`replayable = true`), that arrives after the examination became exhausted would grant three attempts the learner did not earn. The reference's `Open`/`Unlock` signatures, which the reference tests fix, cannot carry the event identifier into the stream.
- `TestDefinitionPublishedV1` carries every question's solution. Assessment keeps it locally, since it calls no other service synchronously, and the definition must never leave the service.

## Decision

- **An examination's id is derived from its session.** `ExaminationId.For(LearningSessionId session)` is a name-based UUID version 8 (RFC 9562, section 5.8), the algorithm of ADR 0010 with one input. It hashes, with SHA-256, Assessment's fixed namespace Guid `03b091d0-68d3-44ab-8dd0-8d6ac1bff3aa` followed by the session's Guid, both big-endian, 16 bytes each. It keeps the first 16 bytes and sets the version nibble to `8` and the variant bits to `10`. A unit test pins one vector: session `0192f5d4-7c3a-7b2e-9a41-5e6f7d8c9b0a` gives `a4743f21-f877-839d-befa-d536a329cd5a`.
- The id is the examination's Marten stream id. Of two concurrent first deliveries, both derive the same id; the second `StartStream` collides (`ExistingStreamIdCollisionException`), Wolverine retries it, and the retry loads the stream. A later status read is a direct load by the derived id, without a projection. `ExaminationId.New()` and `From(Guid)` stay for tests and for reading.
- **`ContentCompletedV1` is deduped on its event identifier** in `assessment.handled_content_completions` (`event_identifier` primary key, `session_id`, `handled_at`). The consumer returns at once for an identifier it has handled. Otherwise it opens the examination, or checks that the stored learner, course and version match the message (a mismatch is a `GuardException` and dead-letters) and calls `Unlock`. It **saves the stream first and then records the identifier**, committing once. A crash in between reruns the consumer, which finds the stream and calls an `Unlock` that is a no-op, since the examination is no longer exhausted. The reverse order could record an identifier whose examination was never saved. Of two deliveries of one identifier racing, the primary key refuses the second insert, and its retry finds the row.
- **The test definition is copied once per course version**, deduped on the version id, not on the event identifier: a version's test is published once and is immutable, so the first copy wins, also for a republish under a new identifier. The copy lives in EF tables in schema `assessment` (`test_definitions`, `test_definition_questions`, `test_definition_options`), with the primary key as the backstop, as Learning's `CourseVersionCopies` does (ADR 0010's precedent). It is for scoring only; the architecture rule `AssessmentResponsesCarryNoSolutions` and an OpenAPI integration test keep correctness out of every response and published event.

### Rejected alternatives

- **`ExaminationId.New()` plus a lookup by session** (a projection or table keyed by session): two concurrent first deliveries can both find nothing before either saves, and nothing collides, so one session could get two examinations. A unique index over a projection would need a hook in the shared `ModuleEventStore`.
- **The session's own Guid as the examination's id:** simpler, but it makes two identities of two services share one value, while the reference models them as distinct types created independently. A derived id keeps them apart and still collides on concurrent openings.
- **Relying on the aggregate's state alone for idempotency:** a redelivered or replayed completion after exhaustion would unlock it.

## Consequences

- **The namespace Guid in `ExaminationId` must never change**, and neither may the byte layout of the derivation. Every stored examination's stream id was derived from them; a change would open a second examination for a session that already has one.
- Examination ids are not time-ordered. Nothing sorts by them.
- `handled_content_completions` grows by one row per completion. That is small for MVP 1's volume; pruning, if ever needed, is a later decision.
- `ContentCompletedV1` may arrive before the version's `TestDefinitionPublishedV1`. Opening needs no definition, so it opens anyway; starting an attempt (#46) reports a missing definition.
