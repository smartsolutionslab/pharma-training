# 0002: Lost updates answer 409 through the use case

**Status:** Accepted

## Context

- An aggregate's rules can span several of its rows: a course version's chapters and their order (#14), a pharmacy's memberships and its last manager (#11). A use case loads the aggregate, checks the rule on what it loaded and saves. Two requests that load the same aggregate both pass the check on their own snapshot and both save: chapters are added twice at one position, a chapter is removed twice (the second answers `500`), two managers end each other's membership and leave none. A change that writes only child rows never touches the root's row, so nothing in the database notices.
- The fabric ADR `docs/adr/fabric/backend-data-access-ef-core.md` keeps every database failure but a unique violation at `500`, a lost update (`DbUpdateConcurrencyException`) included. AGENTS.md asks for a `Result` failure for anything a caller should expect, and an exception only for bugs and broken infrastructure. Once an aggregate has a row version, losing a race on it is expected.
- The Application project cannot see EF Core. The unit of work is where the save and the use case meet.

## Decision

- **The aggregate root is the consistency boundary, and it carries a row version.** `HasRowVersion()` (`src/BuildingBlocks/Persistence/Wiring/RowVersion.cs`) gives an entity a shadow `uint` property `RowVersion`, mapped with `IsRowVersion()`, which Npgsql maps to PostgreSQL's `xmin` system column (`xid`). Every UPDATE of the row moves it on; EF Core adds `WHERE xmin = @loaded` to every update and delete of the row. The property is a shadow one, so the Domain stays free of primitives and setters. Its migration adds no column (Npgsql's SQL generator skips system columns); the snapshot holds the property, so the MigrationRunner sees no pending model change.
- **A change to a child updates its root.** `RowVersionBump` (`src/BuildingBlocks/Persistence/RowVersionBump.cs`), a save interceptor every `ModuleDbContext` gets, detects changes, then follows the foreign keys of every added, modified or deleted entry to the tracked principals, through principals without a row version, and marks each unchanged principal with a row version Modified. Of two saves that loaded the same root, the second updates no row and fails, even when both changed only children. It does nothing in a model without a row version. A context's own `SaveChangesAsync` override (Editorial writes its chapter positions there) runs before it, so what the override changes is bumped too.
- **The use case returns the lost race as a failure.** `ModuleUnitOfWork<TContext>.TryCommitAsync` commits like `CommitAsync` and returns `false` on `DbUpdateConcurrencyException`. Nothing is saved or sent then: the save's transaction, with the outbox's envelopes, rolls back, and the outbox flushes only after a successful save. A module opts in by declaring `TryCommitAsync` on its own `I<Module>UnitOfWork`; Editorial's declaration has a default implementation that commits and returns `true`, so a test double of the interface keeps compiling. A handler that loads an aggregate with a row version calls it and maps `false` to its module's error: `editorial.course-version-changed` (`CourseVersionFailures.Changed`), and for #11 `organisation.pharmacy-changed`, both `409`.
- **No exception handler maps the race.** The fabric ADR's "a lost update stays `500`" holds for every save that does not opt in: `CommitAsync` is unchanged, and a race nobody expected is still a fault.

## Consequences

- Every content change of a course version, chapter-only ones included, updates its `course_versions` row. A change loaded before a publish therefore cannot commit after it (#18).
- Concurrent edits of one aggregate serialise: of two, one wins and the other answers `409` and must load again, even when the two changes would not have conflicted (two additions of different chapters). That is the price of one row version per aggregate.
- A handler that loads such an aggregate and calls `CommitAsync` instead of `TryCommitAsync` answers a lost race with `500`. Every new use case of Editorial's course versions, and #9 and #10's membership changes in Organisation, uses `TryCommitAsync`.
- A root's bump is a full UPDATE of its columns with their loaded values, not a single-column write. The rows are small; a root with large columns would want a narrower bump.
- `xmin` is PostgreSQL's. Another database is a decision that replaces the mapping in `RowVersion`.
