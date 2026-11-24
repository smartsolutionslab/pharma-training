using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Sessions;

/// <summary>How a progress report's refusals reach the learner: the domain's union mapped to its status, and the lost race the store reports.</summary>
public class LearningSessionFailuresTests
{
    private static readonly LearningSessionId Session = LearningSessionId.New();

    [Fact]
    public void A_finished_session_answers_409_with_its_code()
    {
        var refused = LearningSessionErrors.FinishedAlready(Session);

        var mapped = LearningSessionFailures.From(refused);

        mapped.Code.ShouldBe("learning.session-finished");
        mapped.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void A_chapter_outside_the_session_answers_422_with_its_code()
    {
        var refused = LearningSessionErrors.ChapterNotInCourse(Session, ChapterId.From(Guid.CreateVersion7()));

        var mapped = LearningSessionFailures.From(refused);

        mapped.Code.ShouldBe("learning.chapter-not-in-course");
        mapped.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public void A_report_faster_than_the_video_is_422_progress_too_fast()
    {
        var refused = LearningSessionErrors.ProgressTooFast(Session, ChapterId.From(Guid.CreateVersion7()));

        var mapped = LearningSessionFailures.From(refused);

        mapped.Code.ShouldBe("learning.progress-too-fast");
        mapped.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public void A_lost_race_answers_409_session_changed()
    {
        var mapped = LearningSessionFailures.Changed(Session.Value);

        mapped.Code.ShouldBe("learning.session-changed");
        mapped.Status.ShouldBe(HttpStatusCode.Conflict);
        mapped.Message.ShouldBe("The learning session changed in the meantime; report again.");
        mapped.Message.ShouldNotContain(Session.Value.ToString());
    }
}
