# 0016: Lost appends to a stream answer 409 through the use case

**Status:** Accepted (#40); amended for #277: a sweep announces a stored completion or pass whose announcement never left.

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

## Decision (#277): a lost announcement is announced again

- **The marker.** Each module that announces a stored event records it in `announced_events` (`stream_id`, `stream_version`, `announced_at`; primary key on the first two), in the same outbox commit as the announcement. `stream_version` is Marten's version of the event in its stream, counted from 1: the aggregate's version before the save plus the event's place among the pending events. Learning records each `ContentCompleted` it announces as `ContentCompletedV1` (`ReportProgressHandler`), Assessment each `ExaminationPassed` it announces as `TestPassedV1` (`SubmitAttemptHandler`). A marker exists exactly when the envelope committed, and a committed envelope is delivered by Wolverine's outbox.
- **The sweep.** Every `Interval`, each service sends its module's sweep command (`ReannounceContentCompletions`, `ReannounceTestPasses`) through a `PeriodicCommandService` (`src/BuildingBlocks/Messaging/`). A pass announces every stored `ContentCompleted` (`ExaminationPassed`) stored between `Lookback` and `GracePeriod` ago that has no marker, oldest first and at most `BatchSize` a pass, with the handler's own mapping, records the marker in the same commit, one commit per event, and logs a Warning naming the stream and version. The settings are read once at startup from `<Module>:AnnouncementSweep`; a bad value stops the host:

  | Key | Default | Accepted |
  | --- | --- | --- |
  | `Interval` | `00:01:00` | `00:00:01` to `01:00:00` |
  | `GracePeriod` | `00:02:00` | `00:00:01` to `01:00:00` |
  | `Lookback` | `7.00:00:00` | `01:00:00` to `90.00:00:00`, and longer than `GracePeriod` |
  | `BatchSize` | `100` | `1` to `1000` |

- **Exactly once.** The marker's primary key lets one of the handler, the sweep and every replica's sweep commit an event's announcement; the others roll back with their envelope, and a pass that loses so is logged at Information. No lock and no leader election.
- **Why not re-announce every terminal event in a window** and rely on the consumers' idempotency: a re-announce is a new publish under a new `EventIdentifier` and a later `OccurredAt`. Assessment unlocks an exhausted examination on a completion later than the exhaustion (ADR 0012), and Compliance's register keeps the latest completion or lock (ADR 0023); both would be misled by a second announcement of a completion that did leave. It would also announce every terminal event on every tick.
- **Why not Wolverine's envelope storage:** a sent envelope's row is deleted once the broker took it, and both announcements leave the service, so "no envelope" means both "sent" and "never stored".
- **Backfill.** The migration that adds `announced_events` (`AddAnnouncedEvents`, in each module) marks every terminal event already stored as announced, because the store cannot tell a lost announcement from a delivered one. A completion or pass lost before #277 stays lost.

## Consequences

- Of any reports racing on one session, one append lands, so each chapter completes and the content completes at most once per cycle, and only the winner publishes `ContentCompletedV1`.
- The announcement (`ContentCompletedV1`, and `LearningSessionStartedV1` for a new session) is committed to the outbox in a second transaction after the append, and the request's cancellation no longer reaches it: a caller who goes away after the save does not lose it.
- A crash or failure between the append and the outbox commit delays `ContentCompletedV1` or `TestPassedV1` by at most `GracePeriod + Interval` (3 minutes by default), logged as a Warning per re-announced event. The request that failed still answers `500`, as before.
- `LearningSessionStartedV1` (Learning) and, once #305 lands, `TestFailedV1` and `AttemptsExhaustedV1` (Assessment) are still announced in a second transaction without a marker; a crash there still loses them, until they adopt the same marker.
- Publishing from what the stream stored (Wolverine's Marten outbox or a Marten subscription, in the append's own transaction) would remove both the gap and the sweep; it stays the alternative, a decision of its own.
- In the rare case where the sweep commits first and the handler's late commit hits the marker, that request answers `409 already-exists` although its change and announcement stand; the grace period makes this practically unreachable.
- A learner's client sees an occasional `409 learning.session-changed` when two of its reports overlap; it reports again.
- Assessment's SubmitAttempt opted in the same way with #271 (`assessment.examination-changed`).
- A Marten upgrade may rename the refusal's exception; the integration test above fails with a `500` if it does.
