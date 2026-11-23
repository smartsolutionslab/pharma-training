# 0020: Learning notes each exhaustion in the session's stream

**Status:** Accepted (#42)

## Context

- Assessment announces a session's outcome with `AttemptsExhaustedV1` (three failed attempts, with the chapters of the wrong answers in `FailedChapterIds`) and `TestPassedV1`. Learning reacts to both: the lock reopens the failed chapters, and the pass finishes the session. Both events carry Learning's own `SessionId` (ADR 0012).
- `LearningSession.ReopenChapters` deduplicated by state only: it reopened the failed chapters that were completed *now*. Consider this sequence: an exhaustion is delivered, the learner works the reopened chapters through again, and the same exhaustion is delivered again (a broker redelivery, a Wolverine retry after a crash between the append and the inbox mark, or a dead letter replayed with `replayable = true`). The redelivery finds the chapters completed and reopens them a second time, sending the learner back for nothing. By state alone it cannot be told apart from a real second exhaustion. AGENTS.md (integration events) asks consumers to deduplicate on `Metadata.EventIdentifier`; ADR 0012 made the same observation for Assessment's unlock.
- Both events arrive on Learning's one queue from two exchanges, and Wolverine handles a listener's messages in parallel, so their order is not guaranteed. In the real flow a pass follows an exhaustion only after the exhaustion was applied and the content completed again (Assessment refuses attempts until a new `ContentCompletedV1`); anything else is a duplicate or a fault.
- The learner's progress reports append to the same stream concurrently. A lost append in a consumer is `SaveAsync` throwing (ADR 0016), which Wolverine retries with backoff and then dead-letters.
- A session's stream is saved before its `ContentCompletedV1` is published, and Assessment opens an examination only from that event, so an outcome naming a session Learning does not hold cannot become valid later.

## Decision

- **D1: a lock is noted in the session's own stream, on the event's identifier.** The value object `ExhaustionNotice` holds the `Metadata.EventIdentifier` of the `AttemptsExhaustedV1`. `ReopenChapters(notice, failedChapters, now)` checks, in this order:
  1. the session is finished: succeed and raise nothing, not even a note;
  2. the notice is already noted: succeed and raise nothing;
  3. a failed chapter is not in the session: fail with `ChapterNotInCourse` (`learning.chapter-not-in-course`) and raise nothing;
  4. otherwise raise `ChaptersReopened` for the distinct failed chapters that are completed (if any), then always `ExhaustionNoted(Session, Notice, NotedAt)`, in one append.

  Noting even when nothing reopens keeps a later redelivery from reopening chapters the learner completed in between. An outside chapter is refused rather than skipped: Assessment derives the chapters from the version's own test, so it is a fault, and skipping it could leave a completed session with nothing reopened while Assessment waits for a completion that never comes.
- **D2: a pass finishes the session from any state, at `PassedAt`.** `TestPassedV1Consumer` calls `Finish(Timestamp.From(message.PassedAt))`. `Finished` is terminal, so a second `TestPassedV1`, whatever its identifier, raises nothing; no note is needed. A pass reaching an `InProgress` session still finishes it: the graded examination is what counts (ADR 0018).
- **D3: a pass outranks a lock, in either order of arrival.** Pass, then lock: D1's first check; nothing is appended and the consumer does not save. Lock, then pass: the reopen is stored, then the pass finishes the session, and the reopened chapters stay incomplete in the finished session's view, which is the true record.
- **D4: memberships and the version's retirement are not consulted.** The outcome concerns an existing session, not a learner's request (ADR 0017 D3), and a running session stays on its version.
- **D5: faults throw, so they are retried and then dead-lettered, never dropped.** A malformed message (`Ensure.That`, naming the field), an unknown session, a session whose learner, course or version differs from the message (naming the field and both values), and a domain failure (with its code and the chapter) each throw. A lost append throws from `SaveAsync`; the consumers do not use `TrySaveAsync`, because the retry reloads the stream and finds the note or the finished state when the winner was a duplicate. A consumer with nothing pending does not save. The consumers publish nothing and take no unit of work: Compliance consumes `AttemptsExhaustedV1` itself, and Assessment unlocks on the next `ContentCompletedV1`.

## Rejected

- **State alone**, as before: a redelivery after the chapters were worked through reopens them again (Context).
- **A `learning.handled_exhaustions` table like ADR 0012's marker:** the stream append and the marker are two transactions, so a replayed dead letter could act twice in the window between them, and it needs a migration. The note in the stream commits atomically with the reopen.
- **Adding the notice to `ChaptersReopened`:** it is an event type stored under its name; a new event type is added instead.

## Consequences

- `ExhaustionNoted` and `ExhaustionNotice` are stored forever and never renamed.
- A session's stream grows by one note per exhaustion, at most one per cycle.
- An `AttemptsExhaustedV1` that Assessment re-published under a *new* identifier would count as a new lock. Assessment's outbox does not do that; should it ever republish (as Editorial does, ADR 0019), it must keep the identifier, or this decision is revisited.
- Outcomes for unknown sessions dead-letter. An erasure of sessions (#115) must account for them.
- The view (`GET /learning/sessions/{id}`) shows no notices; the projection's step for `ExhaustionNoted` changes nothing.
