using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.LearningSessions;

/// <summary>
/// Video progress may not outpace playback (#43, ADR 0024): a gain of <c>r - p</c> points on a video of <c>d</c> seconds
/// is plausible once <c>(r - p) × d × 8</c> milliseconds have passed since the session's last recorded progress, in any
/// chapter, which is watching it at 1.25 times speed. Text and PDF are not paced; a refused report stores nothing and
/// leaves the clock where it was.
/// </summary>
public class VideoPaceTests
{
    private static readonly Timestamp Start = Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private static readonly VideoDuration FilmDuration = VideoDuration.From(420);
    private static readonly ChapterId Intro = ChapterId.From(Guid.CreateVersion7());
    private static readonly ChapterId Handbook = ChapterId.From(Guid.CreateVersion7());
    private static readonly ChapterId Film = ChapterId.From(Guid.CreateVersion7());
    private static readonly ChapterId Demo = ChapterId.From(Guid.CreateVersion7());

    [Theory]
    [InlineData(5)]
    [InlineData(60)]
    public void A_jump_from_0_to_95_percent_seconds_after_the_start_is_refused(int seconds)
    {
        var session = Started();
        var stored = Pending(session).Count;

        var reported = Report(session, Film, 95, At(seconds));

        ShouldBeTooFast(reported);
        Pending(session).Count.ShouldBe(stored);
        Pending(session).OfType<ChapterProgressRecorded>().ShouldBeEmpty();
        session.CompletedChapters.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(95, 319_200, true)]
    [InlineData(95, 319_199, false)]
    [InlineData(1, 3_360, true)]
    [InlineData(1, 3_359, false)]
    [InlineData(100, 336_000, true)]
    public void A_video_gain_is_accepted_exactly_when_watching_it_at_1_25_times_speed_fits_the_elapsed_time(int gain, long elapsedMs, bool accepted)
    {
        var session = Started();

        var reported = Report(session, Film, gain, AtMs(elapsedMs));

        if (accepted)
        {
            reported.IsSuccess.ShouldBeTrue($"{gain} points after {elapsedMs} ms were refused");
            Pending(session).OfType<ChapterProgressRecorded>().ShouldHaveSingleItem().Progress.ShouldBe(ProgressPercent.From(gain));
        }
        else
        {
            ShouldBeTooFast(reported);
            Pending(session).OfType<ChapterProgressRecorded>().ShouldBeEmpty();
        }
    }

    [Fact]
    public void Progress_at_playback_pace_completes_the_video()
    {
        var session = Started();

        // 7 points of a 420 s film need 23.52 s; one batch every 30 s.
        for (var step = 1; step <= 13; step++)
        {
            Report(session, Film, 7 * step, At(30 * step)).IsSuccess.ShouldBeTrue($"{7 * step} % at {30 * step} s");
        }

        // 4 points need 13.44 s.
        Report(session, Film, 95, At(405)).IsSuccess.ShouldBeTrue("95 % at 405 s");

        Pending(session).OfType<ChapterProgressRecorded>().Count().ShouldBe(14);
        Pending(session).OfType<ChapterCompleted>().ShouldHaveSingleItem().Chapter.ShouldBe(Film);
    }

    [Fact]
    public void The_clock_runs_from_the_sessions_last_recorded_progress_in_any_chapter()
    {
        var session = Started();
        Report(session, Intro, 100, At(300)).IsSuccess.ShouldBeTrue();

        ShouldBeTooFast(Report(session, Film, 95, At(310)));
        Report(session, Film, 95, AtMs(300_000 + 319_200)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Two_videos_cannot_be_credited_for_the_same_time()
    {
        var session = Started(videoChapters: [Film, Demo]);
        Report(session, Film, 95, At(320)).IsSuccess.ShouldBeTrue();

        ShouldBeTooFast(Report(session, Demo, 95, At(321)));
        session.CompletedChapters.ShouldBe([Film]);
    }

    [Fact]
    public void A_refused_report_does_not_move_the_clock()
    {
        var session = Started();
        ShouldBeTooFast(Report(session, Film, 95, At(10)));

        // Measured from +10 s, 310 s would be too little; measured from the start, 320 s is enough.
        Report(session, Film, 95, At(320)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_lower_or_repeated_video_report_is_never_refused_and_does_not_move_the_clock()
    {
        var session = Started();
        Report(session, Film, 50, At(210)).IsSuccess.ShouldBeTrue();
        var stored = Pending(session).Count;

        Report(session, Film, 40, At(211)).IsSuccess.ShouldBeTrue();
        Report(session, Film, 50, At(211)).IsSuccess.ShouldBeTrue();
        Pending(session).Count.ShouldBe(stored);

        // 45 points need 151.2 s, measured from +210 s.
        Report(session, Film, 95, At(210 + 189)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Text_and_pdf_chapters_are_not_paced()
    {
        var session = Started();

        Report(session, Intro, 100, Start).IsSuccess.ShouldBeTrue();
        Report(session, Handbook, 100, Start).IsSuccess.ShouldBeTrue();

        session.CompletedChapters.ShouldBe([Intro, Handbook], ignoreOrder: true);
    }

    [Fact]
    public void Reaching_100_percent_follows_the_same_pace()
    {
        var session = Started();
        Report(session, Film, 95, At(320)).IsSuccess.ShouldBeTrue();

        // 5 points need 16.8 s.
        ShouldBeTooFast(Report(session, Film, 100, At(330)));
        Report(session, Film, 100, At(337)).IsSuccess.ShouldBeTrue();

        Pending(session).OfType<ChapterCompleted>().ShouldHaveSingleItem().Chapter.ShouldBe(Film);
    }

    [Fact]
    public void After_chapters_reopen_the_clock_runs_from_the_reopening()
    {
        var session = Started();
        Report(session, Film, 95, At(320)).IsSuccess.ShouldBeTrue();
        var reopenedAt = Timestamp.From(Start.Value.AddHours(1));
        session.ReopenChapters(ExhaustionNotice.From(Guid.CreateVersion7()), [Film], reopenedAt).IsSuccess.ShouldBeTrue();

        ShouldBeTooFast(Report(session, Film, 95, Timestamp.From(reopenedAt.Value.AddSeconds(10))));
        Report(session, Film, 95, Timestamp.From(reopenedAt.Value.AddSeconds(320))).IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// A reopening stamped before the last progress, by an instance whose clock runs behind, leaves the clock at that
    /// progress: the pace never measures from an earlier instant than one already recorded.
    /// </summary>
    [Fact]
    public void A_reopening_stamped_before_the_last_progress_does_not_move_the_clock_back()
    {
        var session = Started();
        Report(session, Intro, 100, At(3600)).IsSuccess.ShouldBeTrue();
        session.ReopenChapters(ExhaustionNotice.From(Guid.CreateVersion7()), [Intro], At(600)).IsSuccess.ShouldBeTrue();

        ShouldBeTooFast(Report(session, Film, 95, At(3610)));
        Report(session, Film, 95, At(3600 + 320)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Replaying_the_stored_events_keeps_the_clock()
    {
        var original = Started();
        Report(original, Intro, 100, At(60)).IsSuccess.ShouldBeTrue();
        Report(original, Film, 50, At(300)).IsSuccess.ShouldBeTrue();

        var replayed = Replay(Pending(original));

        // 45 points need 151.2 s from +300 s.
        foreach (var session in new[] { original, replayed })
        {
            ShouldBeTooFast(Report(session, Film, 95, At(400)));
            Report(session, Film, 95, At(452)).IsSuccess.ShouldBeTrue();
        }
    }

    [Fact]
    public void A_finished_session_or_a_foreign_chapter_is_refused_before_the_pace()
    {
        var finished = Started();
        finished.Finish(Start);
        var open = Started();

        Report(finished, Film, 95, At(1)).Error.Value.ShouldBeOfType<SessionFinishedAlready>();
        open.RecordProgress(ChapterId.From(Guid.CreateVersion7()), ProgressPercent.From(95), Option<VideoDuration>.Some(FilmDuration), At(1))
            .Error.Value.ShouldBeOfType<ChapterNotInCourse>();
    }

    [Fact]
    public void A_clock_behind_the_last_recorded_progress_counts_as_no_time()
    {
        var session = Started();

        var reported = Report(session, Film, 1, At(-1));

        ShouldBeTooFast(reported);
    }

    [Fact]
    public void A_video_gain_without_a_duration_is_a_bug()
    {
        var session = Started();

        Should.Throw<InvalidOperationException>(() => session.RecordProgress(Film, ProgressPercent.From(10), Option<VideoDuration>.None, At(420)));
        session.RecordProgress(Intro, ProgressPercent.From(10), Option<VideoDuration>.None, Start).IsSuccess.ShouldBeTrue();
    }

    private static void ShouldBeTooFast(Result<Unit, RecordProgressError> reported)
    {
        reported.IsFailure.ShouldBeTrue("the report was accepted although the video cannot have been watched that fast");
        reported.Error.Value.ShouldBeOfType<ProgressTooFast>().Code.ShouldBe("learning.progress-too-fast");
    }

    /// <summary>Reports as the handler does: with the video's duration from Learning's copy, none for text and PDF.</summary>
    private static Result<Unit, RecordProgressError> Report(LearningSession session, ChapterId chapter, int percent, Timestamp now) =>
        session.RecordProgress(
            chapter,
            ProgressPercent.From(percent),
            chapter == Film || chapter == Demo ? Option<VideoDuration>.Some(FilmDuration) : Option<VideoDuration>.None,
            now);

    private static Timestamp At(int seconds) => Timestamp.From(Start.Value.AddSeconds(seconds));

    private static Timestamp AtMs(long milliseconds) => Timestamp.From(Start.Value.AddMilliseconds(milliseconds));

    private static LearningSession Started(IReadOnlyList<ChapterId>? videoChapters = null) =>
        LearningSession.Start(
            LearningSessionId.New(),
            PersonId.From(Guid.CreateVersion7()),
            CourseId.From(Guid.CreateVersion7()),
            CourseVersionId.From(Guid.CreateVersion7()),
            textChapters: [Intro],
            pdfChapters: [Handbook],
            videoChapters: videoChapters ?? [Film],
            Start);

    private static IReadOnlyList<IDomainEvent> Pending(LearningSession session) => ((IEventSourced)session).PendingEvents;

    /// <summary>Loads a session the way the aggregate store does: the private constructor, then the history.</summary>
    private static LearningSession Replay(IReadOnlyList<IDomainEvent> history)
    {
        var session = (LearningSession)Activator.CreateInstance(typeof(LearningSession), nonPublic: true)!;
        ((IEventSourced)session).Replay(history);
        return session;
    }
}
