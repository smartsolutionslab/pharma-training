# 0021: Video progress may not outpace playback

**Status:** Accepted (#43)

## Context

- `docs/domain/README.md` (LearningSession) says of video: "Vorspulen nur bis zur bisher gesehenen Stelle; erledigt ab 95 %; Server prüft Plausibilität der Zeit". Text and PDF have "keine Mindestverweildauer".
- ADR 0018 (the examination is the gate, #207) keeps text and PDF progress as reported and keeps the planned plausibility check for video (#43), "judged against the duration Learning copied from `CourseVersionPublishedV1`, with a tolerance".
- The duration already travels end to end. Editorial requires it on a video chapter (`VideoDuration`, whole seconds from 1 to 14,400). `CourseChapterV1.VideoDurationSeconds` carries it, and Learning's `CourseVersionPublishedV1Consumer` copies it into `learning.course_version_chapters.video_duration_seconds`. The copy is written once; the first delivery wins.
- Before this decision, `POST /learning/sessions/{id}/progress` (#40, #206) accepted 0 to 95 % on a video at once: a learner could complete a video without watching it.

## Decision

1. **The duration comes from Learning's copy, read by the handler at report time.** No stored event gains a field: stored events are never edited, and a new start event for this would be out of proportion. The copy never changes once written, so reading it at report time gives the same duration on every report and replay. `ICourseVersionCopies.FindVideoDurationAsync(CourseVersionId, ChapterId)` answers `Option<VideoDuration>`: none for a chapter that is not a video, a chapter not in the copy, or a copy Learning does not hold. `LearningSession.RecordProgress(chapter, reported, duration, now)` takes it as a parameter, as it takes the time. Learning's Domain has its own `VideoDuration` value object (1 to 14,400 s), mirroring Editorial's, since modules share no Domain types.
2. **The clock runs from the session's last recorded activity, across all chapters.** The aggregate keeps one instant, set only in `Apply`: `SessionStarted.StartedAt`, every `ChapterProgressRecorded.RecordedAt` (any kind), and `ChaptersReopened.ReopenedAt`. The elapsed time is `now - last`, with `now` from the handler's `TimeProvider`; below zero (two replicas' clocks apart) it counts as zero.
   - One clock per session, not per chapter, so consecutive intervals never overlap and the same wall time cannot be credited to several videos.
   - Text and PDF reports move the clock too: time spent reading was not spent watching.
   - Only stored progress moves it. A refused, lower or repeated report stores nothing, so it does not move the clock, and a refusal heals itself: the next report is measured from the same, earlier instant.
   - A reopen (after `AttemptsExhausted`, #42) restarts it: the reopened video is watched again from 0, at pace from the reopening.
3. **The rule.** For a video chapter with stored progress `p`, a report `r > p`, duration `d` seconds and elapsed time `e` milliseconds, the report is plausible exactly when `(r - p) × d × 8 ≤ e`, in `long` arithmetic (`VideoPace`, a static Domain rule beside `CompletionRule`). The gained share takes `(r - p) / 100 × d × 1000` ms at normal speed; at 1.25 times speed that is `(r - p) × d × 8` ms.
   - The factor 1.25 absorbs the jitter of batched reports (a 30 s batch arriving up to 7.5 s early), whole-second durations and whole-percent reports, and a declared duration up to 20 % longer than the file.
   - **No additive slack.** A slack of `+N` points or `+N` seconds per report is granted again on every report: fifty reports a second apart add fifty slacks, and `+5` points reaches 95 % in 19 reports. A factor on elapsed time is granted once per unit of real time, however the reports are cut.
   - Erring strict is cheap: a refused honest report stores nothing and leaves the clock where it was, so the client's next report, over the longer interval, passes.
   - Worked numbers: a 420 s video needs at least 319.2 s from 0 to 95 % and 16.8 s from 95 to 100 %; a 1 s video needs 760 ms from 0 to 95 %; "0 to 95 % within seconds" is refused for any video longer than about 6 s.
4. **The refusal is `ProgressTooFast`, `learning.progress-too-fast`, 422.** It joins the `RecordProgressError` union and is mapped in `LearningSessionFailures.From` without a discard arm. The request is well formed (400 `invalid-argument` stays for a percent outside 0 to 100), but what it claims contradicts what the server knows, like `learning.chapter-not-in-course`, already 422 here.
5. **Order of the checks:** finished session 409; chapter not in the course 422 `chapter-not-in-course`; `r ≤ p` success with no event; pace, video only, 422 `progress-too-fast` with no event; then `ChapterProgressRecorded`, `ChapterCompleted`, `ContentCompleted` as before. A video chapter with a real gain and no duration throws `InvalidOperationException` (500): Learning's copy broke its own invariant, which the consumer now refuses (a video chapter without a duration of 1 to 14,400 s dead-letters by the field's name).
6. **Text and PDF are not paced** (ADR 0018); they only move the clock.
7. **A lower or repeated report is never refused.** It stays 200 with no event, as #206 made it, so rewatching, a second device behind the first and a replayed batch do not fail.
8. **100 % follows the same pace**, with no special case. A video completes at 95 %, so a refused 100 % never holds back completion.
9. **The first report needs no special case**: progress starts at 0 and the clock at `SessionStarted`.
10. **API and OpenAPI are unchanged**: a new problem code, no new request, response or route. The player (#44) treats 422 `learning.progress-too-fast` as "keep the position and report again later", silently, not as an error toast, and offers no playback rate above 1.25×; a faster rate needs this factor changed by a developer's decision recorded here.

## Rejected

- **A clock per chapter.** It lets the same wall time count for several videos: start, wait one video's length, report 95 % on five videos.
- **An additive tolerance** (points or seconds per report): granted again on every report, it defeats the check by cutting reports small.
- **Durations in `SessionStarted`.** Stored events are never edited; a new start event type only for this is out of proportion while the copy never changes.
- **409**: in Learning it means a state conflict the client settles by reloading (a finished session, a lost append); reloading settles nothing here. **429**: that is request rate, and refusals are not counted. **403**: the refusal is not about who reports.

## Consequences

- A refusal heals itself: it stores nothing, and the next report is measured over the longer interval.
- Parallel tabs or devices cannot double-count: one clock per session, and of racing reports that passed on the same snapshot only one append lands (ADR 0016), so the bound holds over what is stored.
- A learner who starts a session and waits a video's length can still report it watched: the check proves time, not attention. The examination is the gate (ADR 0018).
- The player (#44) must keep its playback rate at 1.25× or below and handle the 422 silently.
- Integration tests that report video progress use a 1 s video and wait 1 s before a gain.
