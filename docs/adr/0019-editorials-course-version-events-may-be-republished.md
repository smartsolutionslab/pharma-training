# 0019: Editorial's course-version events may be republished; consumers deduplicate on the version id

**Status:** Accepted (#189).

## Context

- Editorial announces a course version with `CourseVersionPublishedV1` and `TestDefinitionPublishedV1` when it is published, and with `CourseVersionRetiredV1` when it is retired. Each event goes to a fanout exchange named after its type (ADR 0011), and the broker drops an event when no queue is bound to that exchange.
- A service's queue is bound only from its first start. A service that consumes these events and is deployed after they went out (Certification, Assessment) never receives them, and has no copy of the versions published before it.
- The fabric ADR `module-communication-integration-events` tells consumers to deduplicate on `Metadata.EventIdentifier`. An event sent again gets a new identifier, so a consumer that followed that rule alone would apply it twice.
- A published version never changes: every content method refuses anything but a draft. Mapping the stored version again yields the payload it was announced with.

## Decision

- **The command.** `POST /editorial/course-versions/republish` (`RepublishCourseVersions`) sends again, through Editorial's own unit of work and broker user, the events of every course version Editorial announced.
- **What is selected.** A version with `PublishedAt` set gets `CourseVersionPublishedV1` and then `TestDefinitionPublishedV1`; one with `RetiredAt` set gets `CourseVersionRetiredV1`. `Publish` and `Retire` set those fields in the save that stored the events, so they mark exactly what was announced. The selection is never by status: a row marked published any other way was never announced, and its consumers might refuse it.
- **The order.** By course id, then by version number; within a version, published, test definition, retired, as they first went out. The order is best effort: one queue is delivered in parallel (ADR 0015), and no consumer may rely on it.
- **One commit per version.** Each version is loaded, its two or three events published, and that version committed, so a transaction holds one version's content and a version's events stay together. Nothing tracked changes, so the commit cannot lose a race (ADR 0002); a commit that fails answers 500, and what was committed before it stays sent.
- **`200` with counts.** The answer is `EditorialRepublishedEventsDto(CourseVersions, TestDefinitions, Retirements)`: the events are in Editorial's durable outbox when it answers, and the counts tell an operator whether anything went out. An empty catalogue answers `200` with three zeros.
- **`editorial.edit`.** The policy of publishing and retiring, whose announcements it repeats. It creates no fact and reveals nothing an editor cannot read already; its worst misuse is load that every consumer drops after one key lookup. `editorial.manage` is the module's sample policy and grants nothing editorial.
- **The rule for consumers.** A consumer of `CourseVersionPublishedV1`, `TestDefinitionPublishedV1` or `CourseVersionRetiredV1` deduplicates on `CourseVersionId`, against an insert-only copy whose key is that id, and reads nothing from `Metadata`. Catalog, Compliance, Learning and Certification already do, and so does Assessment's `TestDefinitionPublishedV1Consumer` (added with #200): it keeps the first copy of a version's test and drops any later one by the version id.

## Running it

Added after the review of #243.

- **One run at a time.** A run takes a PostgreSQL advisory lock in Editorial's database (`EditorialRepublishLock`, `pg_try_advisory_lock`) before its first load and holds it to its end, so it holds across every instance of the service. A run asked for while another holds the lock answers `409` with `editorial.republish-running` at once, having sent nothing. The lock is session-level on the connection the run's DbContext keeps open for the scope, since a transaction-level lock could not span the run's commits, one per version; it is released when the run ends, and by PostgreSQL when the session ends any other way.
- **A timeout means "run again".** A caller whose request timed out cannot tell whether the run finished, is still going or was cut off. They run it again: while the first run still holds the lock, the second answers `409` and they wait and retry; otherwise it sends everything once more, and every consumer drops what it has by the version id.
- **One log line per run**, at Information: the editor's person id and the counts sent, or that the run was refused.
- **A command that answers counts.** CQRS-logical has a command answer at most an identifier. The republish answers `EditorialRepublishedEventsDto` all the same, as a deliberate exception: only the run knows what it put in the outbox, and no query afterwards can tell an operator whether this run sent anything. The counts are the run's receipt, not a read of the module's state.
- **Its own commit.** Each version is committed with `CommitOneOfSeveralAsync`, which flushes the outbox with Wolverine's `MultiFlushMode.AllowMultiples` for that call alone, so each commit's events go out at once. Every other commit of every module keeps Wolverine's default, `OnlyOnce`, and the republish is the only caller; it may be, because nothing it saves can lose a race.
- **The policy stays `editorial.edit`.** The review asked for a stricter one, held by whoever administers the catalogue. The realm has no such role: `editor` is the Redaktion, and `manager` is the modules' sample role that grants nothing editorial. A stricter policy waits for that role to be decided.
- **Load once republish also releases chapter files (#264).** A run then releases the files of every published version, superseded and retired ones included, and Learning checks each file again and copies the ones it lacks, each copy under its consumer's 30-minute timeout (#228). With many versions or large videos, one run is a burst of server-side copies: run it off-peak. Against an empty Learning, such a run can outlast the 15-minute read URLs it signs, so some copies fail and are dead-lettered; running it again converges, since each run signs fresh URLs and Learning copies only what it is missing.

## Consequences

- The republished payload follows the current mappings: if a mapping or `EditorialIntegrationEventMappings.MaxAttempts` changes after a version was published, its republished events carry the new shape. Every consumer keeps the copy that arrived first, so nothing already stored changes.
- Every subscriber receives a run, not only the one that was missing the events, and drops what it already has by version id.
- A run cut off part-way is simply run again.
- A consumer of these events added later, in Certification or any other module, is bound by the rule above.
- `0016` was held by #206, and `0017` or `0018` might be taken by #194, when this was written. If `0019` is taken by the time #189 merges, this file and every reference to it (`RepublishCourseVersions`, its handler and endpoint, the three contracts' docs and the README) are renumbered.
