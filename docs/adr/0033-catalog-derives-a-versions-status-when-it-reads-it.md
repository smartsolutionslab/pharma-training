# 0033: Catalog derives a course version's status when it reads it

**Status:** Accepted (#273; the rule itself since #182)

## Context

- Catalog keeps a copy of every course version Editorial publishes (`CourseVersionPublishedV1`) and every retirement (`CourseVersionRetiredV1`), and answers `GET /catalog/courses` and `GET /catalog/course-versions/{id}` from them. A version is `Current`, `Superseded` or `Retired`.
- Nothing announces a supersede: Editorial supersedes the predecessors in the publish handler's single save (ADR 0007), and `CourseVersionRetiredV1` carries no version number.
- Events arrive at least once and in any order across Editorial's publishes and retirements.
- The rule was recorded only as a few sentences in ADR 0007, Editorial's ADR (review of PR #261).

## Decision

- **Catalog stores what it received and derives the status when it reads.** Published versions and retirements are insert-only rows; a version's status is computed from them at read time:
  - `Retired` when a retirement of that version has arrived, whatever arrived before or after;
  - otherwise `Superseded` when a higher-numbered version of its course has arrived, retired or not;
  - otherwise `Current`.
- **Why:** no row is ever updated, so two consumers cannot race on one row, and a duplicate or an out-of-order event gives the same answer as the events in order. There is nothing to repair after a late event.
- **"The highest version number is current" holds only while that version is not retired.** `GET /catalog/courses` lists a course with its current version; a course whose highest version is retired is not listed, although an older version exists (`Retiring_the_current_version_hides_the_course_although_an_older_version_exists`).

## Consequences

- **The accepted race.** A retirement of the newer version can arrive before that version's publish. Until the publish arrives, Catalog does not know a newer version exists: the older version reads `Current` and the course is listed with it. Once the publish arrives, the end state is the in-order one: the older version reads `Superseded`, the newer `Retired`, and the course leaves `GET /catalog/courses`. The window is eventual consistency and accepted. `CatalogCourseVersionsTests.A_newer_versions_retirement_arriving_before_its_publish_ends_as_if_the_events_arrived_in_order` pins it.
- **The assumption the acceptance rests on: Catalog's status is for display only.** Learning, Compliance and Certification keep their own copies from Editorial's events and never read Catalog's `Current`; Catalog publishes no integration event. A later change that assigns, lets people learn or certifies from Catalog's status has to revisit this ADR, because the race above would then reach a decision rather than a screen.
