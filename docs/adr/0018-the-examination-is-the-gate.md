# 0018: The examination is the gate, not reported progress

**Status:** Accepted (#207, owner decision of 2026-11-23: option 1, plus #43).

## Context

- Learning records the progress the learner's client reports (#40, PR #206). A chapter completes at 95 % for a video and at 100 % for text or a PDF. Once every required chapter is complete, the session raises `ContentCompleted`, and Learning publishes `ContentCompletedV1` (`docs/domain/README.md`, LearningSession).
- Nothing on the server checks that a reported percent is plausible. A learner can report 100 % for every chapter of their own session and complete the content without reading any of it. The review of PR #206 (review 6019210449, optional finding 2) named this as a trust gap for a compliance training record.
- What counts for the record is the chain `ContentCompleted` → `TestPassed` → `CertificateIssued` → `AssignmentCompleted` (`docs/domain/README.md`, the flow under the integration events):
  - `ContentCompletedV1` has one consumer by design, Assessment, where it unlocks the examination (#45, PR #200).
  - Certification issues a certificate from `TestPassedV1` alone.
  - Compliance is specified to complete an assignment from `CertificateIssued`; its consumer is not built yet.
- #207 asked which of three options to take:
  1. the examination is the gate;
  2. server-side plausibility checks on every report;
  3. both.

## Decision

- **The passed examination is what counts for the record. Completed content proves nothing on its own.** Content completion only unlocks the examination. A certificate, and with it a completed assignment, follows only from `TestPassed`, which Assessment grades server-side against the version's test definition.
- **Text and PDF progress stays as the client reports it.** Learning applies only the session's own invariants:
  - progress per chapter only grows, and a report that does not grow it records nothing;
  - a chapter must belong to the course;
  - a finished session takes no more reports.

  No minimum reading time and no page-by-page proof is asked for.
- **Video keeps its planned plausibility check (#43).** Video progress may not grow faster than the time since the last report allows, judged against the duration Learning copied from `CourseVersionPublishedV1`, with a tolerance. That check is #43's to build; this decision does not change it.
- **Nothing downstream may treat `ContentCompleted`, `ContentCompletedV1` or a session's content-completed state as evidence of training.** That covers a report, an export, a compliance status and a certificate. A new consumer of `ContentCompletedV1` other than Assessment's unlock needs a decision of its own.

## Consequences

- A learner can reach the examination without reading the text or PDF chapters. That is accepted: they still have to pass it, with at most three attempts, and after exhausting them only the chapters of the wrongly answered questions reopen (README, Examination). The examination's questions, each tied to a chapter, carry the assurance. That makes question quality and the four-eyes approval of questions (ADR 0007) part of the compliance argument, not only of content quality.
- Learning needs no per-chapter text length, page count or reading-time model, and the client's 30-second batching of reports stays as it is.
- A screen may still show a learner's progress and "content completed" as a convenience, to the learner or to whoever follows their training. It must not present them as proof of training.
- If an auditor or customer later asks for proof of reading, this decision is revisited. Option 2's text and PDF checks would then need each chapter's length in Learning's copy, which `CourseChapterV1` does not carry today.
