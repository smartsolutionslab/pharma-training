# 0010: Learning session ids are derived per learner, version and run

**Status:** Accepted (#39).

## Context

- The README's rule for Learning is one stream per person, course version and run ("Ein Stream pro Person × Schulungsversion und Durchlauf", `docs/domain/README.md:199`), with at most one unfinished session per person and version (`:210`). `POST /learning/sessions` starts or resumes a session, idempotently (`:297`).
- The rule spans streams, so the aggregate cannot hold it. `StartLearningSessionHandler` looks the learner's runs up first, in the inline Marten projection `LearningSessionView`. Two concurrent first starts can both find no unfinished run before either one saves.
- AGENTS.md (Marten) says that a command which creates an aggregate looks for its id first. If another save creates the same id in between, the store refuses the second stream, and the host answers `409 already-exists`. The handler must never catch that exception itself. That backstop works only when both starts create the *same* stream id. Random Guid v7 ids never collide, so with them two unfinished sessions could exist.
- A unique index over the projection's document (learner, version, not finished) would need a hook in the shared `ModuleEventStore`. Its violation would also be a 23505 from Marten, which the host does not map, so it would answer `500`.

## Decision

- **A new session's id is derived.** `LearningSessionId.For(PersonId learner, CourseVersionId version, SessionRun run)` is a name-based UUID version 8 (RFC 9562, section 5.8). It hashes, with SHA-256:
  - Learning's fixed namespace Guid;
  - the learner's Guid, the version's Guid (both big-endian, 16 bytes each);
  - the run as a 32-bit big-endian integer.

  It keeps the first 16 bytes and sets the version nibble to `8` and the variant bits to `10`.
- **`SessionRun`** (`IValueObject<int>`, at least 1, `First`, `Next()`) counts a learner's runs of one version. The handler starts run `n + 1`, where `n` is the number of runs the learner has on that version. Every run it finds is finished, since an unfinished one is resumed instead.
- Neither the run nor the derivation is stored in an event: `SessionStarted` carries the id as the reference has it. `LearningSessionId.New()` (a Guid v7) and `From(Guid)` stay for tests and for reading.
- Of two concurrent first starts, both derive the same id. The second `StartStream` collides (`ExistingStreamIdCollisionException`), and the host answers `409 already-exists` through `AddDuplicateStreamResponses`. A retry then resumes the session the first start created with `200`.
- AGENTS.md bans `Guid.NewGuid()` for random ids. A derived id is not random, so it neither breaks that rule nor needs `Guid.CreateVersion7()`.

## Consequences

- Session ids are not time-ordered, unlike the project's other ids. Nothing sorts by them, and the view keeps `StartedAt` for ordering.
- **The namespace Guid in `LearningSessionId` must never change.** Neither may the byte layout of the derivation. Every stored session's id was derived from them, and a change would let a learner's next start derive an id that belongs to no stream. That would allow a second unfinished session on the same run.
- The loser of a concurrent first start sees `409 already-exists`, whose generic detail says that retrying the same request is refused again. For this endpoint, a retry answers `200` with the session. The frontend (#44) retries once on `already-exists` here.
- The lookup reads the projection without an index on learner and version. That is enough for MVP 1's volume. An index needs a building-block hook and is left for when it is measured.
