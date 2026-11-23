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
- **The rule for consumers.** A consumer of `CourseVersionPublishedV1`, `TestDefinitionPublishedV1` or `CourseVersionRetiredV1` deduplicates on `CourseVersionId`, against an insert-only copy whose key is that id, and reads nothing from `Metadata`. Catalog, Compliance and Learning already do; Assessment's consumer (#45) does too.

## Consequences

- The republished payload follows the current mappings: if a mapping or `EditorialIntegrationEventMappings.MaxAttempts` changes after a version was published, its republished events carry the new shape. Every consumer keeps the copy that arrived first, so nothing already stored changes.
- Every subscriber receives a run, not only the one that was missing the events, and drops what it already has by version id.
- A run cut off part-way is simply run again.
- Certification's future consumers of these events are bound by the rule above.
- `0016` was held by #206, and `0017` or `0018` might be taken by #194, when this was written. If `0019` is taken by the time #189 merges, this file and every reference to it (`RepublishCourseVersions`, its handler and endpoint, the three contracts' docs and the README) are renumbered.
