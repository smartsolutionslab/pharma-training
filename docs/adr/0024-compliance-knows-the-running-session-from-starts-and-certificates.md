# 0024: Compliance knows the running session from starts and certificates

**Status:** Accepted (#35). It applies the decision "Matrix und Startversion": the learner's dashboard opens a running session on its own version, and otherwise the course's current version.

## Context

- The dashboard (`GET /compliance/me/assignments?pharmacyId=`, C4) needs, per active assignment, exactly one course version to open: `POST /learning/sessions` starts or resumes by version (ADR 0010). A session that is running must be continued on its own version, even when a newer one was published or its own was retired since; without one, the course's current version is begun.
- Learning owns sessions. Compliance must not ask it (domain README: "Learning wird nicht abgefragt"). It already consumes the two events that tell it what it needs: `LearningSessionStartedV1(SessionId, PersonId, CourseId, CourseVersionId)`, which happened at `Metadata.OccurredAt`, and `CertificateIssuedV1(CertificateId, PersonId, CourseId, CourseVersionId, IssuedAt, ValidUntil)`; a certificate follows the passed test, which finishes the session in Learning.
- Until #35 neither consumer kept anything of the event: the start only moved active assignments to `InProgress` (ADR 0019), the certificate only completed them.
- Events arrive in any order, more than once, and a start may arrive before the assignment it belongs to exists.

## Decision

- **D1. Two insert-only copies.** `compliance.learning_session_starts` (`id` = the session id, `learner_id`, `course_id`, `course_version_id`, `started_at` = `Metadata.OccurredAt`) and `compliance.issued_certificates` (`id` = the certificate id, `learner_id`, `course_id`, `course_version_id`, `issued_at`), each indexed on `(learner_id, course_id)`, without foreign keys. `LearningSessionStartedV1Consumer` and `CertificateIssuedV1Consumer` keep the copy whether or not the person has an active assignment of the course, deduplicated on its id, and save it in the same commit as their changes to assignments. Two deliveries at once race only on the primary key: the loser throws, Wolverine retries, and the retry finds the row (ADR 0002, as #28's copies).
- **D2. The running session.** For one learner and course, let `L` be the latest `IssuedAt` among the learner's certificates of the course, of any version. The running session is the start with the latest `StartedAt` among the starts with `StartedAt > L`, or among all starts without a certificate; on equal `StartedAt` the greater session id wins. No such start means no running session (`LearnerProgress.RunningSession()`). Assignments are closed per course, so a certificate of any version ends the learner's cycle; a start at the certificate's instant counts as finished. Only the instants decide, never the order of arrival.
- **D3. The version to open.** `CourseStart.For(running, course)`: a running session's version, to resume, whatever became of that version; else the course's current version, to begin; else none, when the latest version is retired. A session started before the assignment was made counts: sessions belong to the person, not to a pharmacy, so one start serves the learner's assignments of the course in every pharmacy.

## Rejected

- **Asking Learning for the session.** A call between services at read time, and the dashboard fails with Learning.
- **Keeping the session on the assignment.** A start before the assignment would be lost, the same start would be written to every pharmacy's assignment, and a certificate would have to clear it on rows it does not complete.
- **Deduplicating on `Metadata.EventIdentifier`.** The session and certificate ids are the natural keys and also absorb a republished event under a new identifier.

## Consequences

- **Known limit.** A certificate is issued a moment after the session's test is passed. A new run of the same course started inside that gap reads as finished; the page then offers the current version, and Learning resumes that run itself when the version is the same.
- **Not covered.** Learning has no event for an abandoned session, so an unfinished session is simply superseded by a newer start.
- The tables hold no names; the erasure of #115 needs nothing new for them.
- The rows only grow, a few per learner and course; reads are by `(learner_id, course_id)`.
- `0023` is held by an open branch. If `0024` is taken by the time #35 merges, this file and every reference to it (the domain README, `LearnerProgress`, `CourseStart`, the copies' docs, the consumers' docs) are renumbered.
