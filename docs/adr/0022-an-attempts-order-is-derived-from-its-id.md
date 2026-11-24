# 0022: An attempt's order is derived from its id

**Status:** Accepted (#46)

## Context

- The README asks for questions and answer options shuffled per attempt ("Fragen und Antwortoptionen pro Versuch gemischt", `docs/domain/README.md:73`), to make copying from a neighbour harder.
- The reference fixes `Examination.StartAttempt(AttemptId, IReadOnlyList<QuestionVersionId> shuffledQuestions, Timestamp)`, and its doc on `TestAttempt` says "Questions and options are shuffled per attempt; the order is stored". The reference tests pin that signature, and `AttemptStarted(Examination, Attempt, Number, Questions, StartedAt)` stores the question order. Neither can carry an order of options.
- `AttemptStarted` is stored forever under its type name: it is never edited, and a new field would be a new event type.
- A learner who reloads the page during an attempt must see the same questions and options in the same order, with their answers (#48). A per-request shuffle (`Random.Shared`) would reorder the options on every read.

## Decision

- **One pure domain service, `AttemptShuffle`** (`Assessment.Domain/Examinations/AttemptShuffle.cs`, BCL only):
  - `IReadOnlyList<QuestionVersionId> Questions(AttemptId attempt, IEnumerable<QuestionVersionId> questions)`;
  - `IReadOnlyList<AnswerOptionId> Options(AttemptId attempt, IEnumerable<AnswerOptionId> options)`.
- **The ordering.** Each item's key is `SHA-256(attempt's Guid ‖ item's Guid)`, where both Guids are written big-endian, 16 bytes each, as `ExaminationId.For` writes them (ADR 0012). The items are ordered by their keys' bytes ascending, unsigned and lexicographic. A tie, which does not happen in practice, falls back to the item's own big-endian bytes. The order the items are given in does not matter, and a duplicate item is refused with a `GuardException`.
- **Questions.** `StartAttemptHandler` computes the question order once, from the new attempt's id and the definition's questions, and `AttemptStarted.Questions` stores it ("Gestellte Fragenversionen in fester Reihenfolge").
- **Options.** The option order is computed on every read from the stored attempt id and the copy's option ids. Both are immutable: the attempt id is in the stream, and the copy is written once per version (ADR 0012). The order is therefore a pure function of stored data: the same on every device and every reload, and different per attempt.
- **The pinned vector**, computed with Python's `hashlib` independently of the C# code: with attempt `0192f5d4-7c3a-7b2e-9a41-5e6f7d8c9b0a` and items `00000000-0000-7000-8000-00000000000N` for N = 1…5,
  - the order by N is `2, 5, 4, 3, 1`; the leading key bytes are N=1 `eba28b33…`, N=2 `08329521…`, N=3 `749d9ebe…`, N=4 `4f1cbd39…`, N=5 `210956fb…`;
  - with attempt `…9b0b` instead, the order is `1, 4, 3, 5, 2`.
  `AttemptShuffleTests` pins both.
- **Membership (owner decision, 2026-11-23).** Assessment does not check pharmacy membership. An examination opened while the person was a member is theirs to finish: Assessment keeps no copy of Organisation's memberships and refuses no former member on starting an attempt, answering or submitting. The way into an examination is Learning's `ContentCompletedV1`, which Learning sends only for a session it admitted. This is the contrast to ADR 0017 (on `agent/194`), where Learning refuses a person who is no longer an active member: Learning decides who may *begin* a course; once the content is completed, the examination belongs to its learner. Certification still issues a certificate only from its own membership copy.

### Rejected alternatives

- **`Random.Shared`:** not reproducible in tests, and the options' order would not survive a reload.
- **`System.Random(seed)` seeded with the attempt id:** its sequence is not guaranteed stable across .NET versions. A hash whose algorithm we own is stable for all time.
- **A stored seed in a new event:** it changes nothing in substance, since the attempt id already is a stored seed (a Guid v7 with 74 random bits), and it costs an event type.
- **Storing each question's option order:** impossible without changing `AttemptStarted` or the reference's signature.
- **A membership check in Assessment** (an insert-only copy of Organisation's memberships, `403 assessment.membership-missing`, as ADR 0017 does for Learning): three tables, three consumers and new broker read grants, for a gap the owner decided to leave open.

## Consequences

- **The algorithm and its byte layout must never change.** A change would reorder the options of every stored attempt, and a reload would show them in another order. The pinned vectors guard this.
- The option order is derived, not stored literally; reading an attempt needs the version's copy, which is written once and never removed.
- Whoever knows an attempt id can compute its order. That is harmless: the order is no secret, it only makes copying from a neighbour harder. Attempt ids reach only their own learner.
- A person whose membership ends between completing the content and submitting may still finish the examination, and their `TestPassedV1` reaches Certification, which decides on its own membership copy.
