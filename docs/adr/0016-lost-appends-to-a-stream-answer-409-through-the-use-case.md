# 0016: Lost appends to a stream answer 409 through the use case

**Status:** Accepted (#40)

## Context

- An event-sourced use case loads an aggregate from its stream, checks a rule on what it loaded and appends the events it raised at the version it loaded (`MartenAggregateStore.SaveAsync`: `Append(id, version + n, events)`). Two requests that load the same version both pass the check on their own snapshot; Marten's expected-version check lets the first append land and refuses the second.
- Learning's progress reports (#40) race like that by design: the player reports on pause, at a chapter's end and every 30 s, from more than one device. Two reports that each complete the last open chapter would otherwise both raise `ContentCompleted`, and Assessment would hear of one completion twice in a cycle.
- The fabric ADR `docs/adr/fabric/module-event-store-marten.md` keeps a lost append at `500` unless the use case opts in. ADR 0002 decided the same question for EF Core row versions: a lost race the use case expects is a `Result` failure mapped to the module's own `409`, with no exception handler. AGENTS.md (Event store: Marten) asks for it too: "a save that lost a race fails, and the command reports it rather than retrying silently".
- Observed before the change (T4 of #40): twenty concurrent reports on one session through the edge answered `500` (`Concurrent_reports_on_one_session_answer_200_or_409_and_never_500`).

## Decision

- **`MartenAggregateStore.TrySaveAsync(IEventSourced, CancellationToken)`** (`src/BuildingBlocks/EventStore/`) saves like `SaveAsync` and returns `false` when the append lost the race. Nothing is stored then (the session's transaction, with the inline projections' writes, rolls back) and the aggregate is not marked stored. It counts as a lost race:
  - `JasperFx.Events.EventStreamUnexpectedMaxEventIdException`, Marten 9.59's refusal of an append whose expected version the stream has moved past;
  - a PostgreSQL deadlock (`40P01`) or serialization failure (`40001`) anywhere in the exception's tree, as `ModuleUnitOfWork.TryCommitAsync` does (ADR 0002), since the inline projections' upserts share the append's transaction.
  - Not `ExistingStreamIdCollisionException`: two first starts of one session stay the host's `409 already-exists` (ADR 0010).
- **A module opts in** by declaring `Task<bool> TrySaveAsync(IEventSourced aggregate, CancellationToken cancellationToken)` on its own `I<Module>AggregateStore`, which the store's public method implements. `IAggregateStore` is unchanged, so a module that does not opt in, and its test doubles, are unaffected; its `SaveAsync` still lets a lost append through as a `500`, a race nobody expected.
- **Learning opts in for progress reports.** `ReportProgressHandler` maps `false` to `learning.session-changed` (`LearningSessionFailures.Changed`, `409`) and publishes nothing. The server does not retry; the client reports again, which is always safe because a report at or below the stored progress stores nothing.
- **No exception handler maps the race**, as in ADR 0002.

## Consequences

- Of any reports racing on one session, one append lands, so each chapter completes and the content completes at most once per cycle, and only the winner publishes `ContentCompletedV1`.
- The announcement (`ContentCompletedV1`, and `LearningSessionStartedV1` for a new session) is committed to the outbox in a second transaction after the append, and the request's cancellation no longer reaches it: a caller who goes away after the save does not lose it. A process crash between the append and the outbox commit still loses it, as the fabric ADR accepts.
- That second transaction can also fail on its own after the append landed (the outbox insert, the connection, a deadlock), not only by a crash: the request then answers `500`, and the client's retry stores nothing new because the progress already stands, so the announcement is lost all the same. Publishing from what the stream stored (a Marten subscription or outbox reading the appended `ContentCompleted` and `SessionStarted` events) closes this; it is a decision of its own, proposed with Assessment opening the examination (#40's T5).
- A learner's client sees an occasional `409 learning.session-changed` when two of its reports overlap; it reports again.
- Assessment's event-sourced use cases keep `500` for a lost append until they opt in the same way.
- A Marten upgrade may rename the refusal's exception; the integration test above fails with a `500` if it does.
