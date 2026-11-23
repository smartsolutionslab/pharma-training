using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;
using Marten.Events.Aggregation;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.EventStore;

/// <summary>
/// Each session's view, built from its stream and run inline, so it commits in the same transaction as the events:
/// the start finds a learner's runs on a version by it, and a session's reads answer from it.
/// </summary>
public sealed class LearningSessionViewProjection : SingleStreamProjection<LearningSessionView, Guid>, ILearningSessionViewProjection
{
    public LearningSessionView Create(SessionStarted started)
    {
        Ensure.That(started).IsNotNull();

        return new LearningSessionView
        {
            Id = started.Session.Value,
            LearnerId = started.Learner.Value,
            CourseId = started.Course.Value,
            CourseVersionId = started.CourseVersion.Value,
            Status = SessionStatus.InProgress,
            StartedAt = started.StartedAt.Value,
            Chapters =
            [
                .. Chapters(started.TextChapters, ChapterKind.Text),
                .. Chapters(started.PdfChapters, ChapterKind.Pdf),
                .. Chapters(started.VideoChapters, ChapterKind.Video),
            ],
        };
    }

    public void Apply(ChapterProgressRecorded recorded, LearningSessionView view)
    {
        Ensure.That(recorded).IsNotNull();
        Ensure.That(view).IsNotNull();

        ChapterOf(view, recorded.Chapter).ProgressPercent = recorded.Progress.Value;
    }

    public void Apply(ChapterCompleted completed, LearningSessionView view)
    {
        Ensure.That(completed).IsNotNull();
        Ensure.That(view).IsNotNull();

        ChapterOf(view, completed.Chapter).Completed = true;
    }

    public void Apply(ContentCompleted completed, LearningSessionView view)
    {
        Ensure.That(completed).IsNotNull();
        Ensure.That(view).IsNotNull();

        view.Status = SessionStatus.ContentCompleted;
    }

    public void Apply(ChaptersReopened reopened, LearningSessionView view)
    {
        Ensure.That(reopened).IsNotNull();
        Ensure.That(view).IsNotNull();

        foreach (var chapter in reopened.Chapters)
        {
            var reopenedChapter = ChapterOf(view, chapter);
            reopenedChapter.Completed = false;
            reopenedChapter.ProgressPercent = ProgressPercent.None.Value;
        }

        view.Status = SessionStatus.InProgress;
    }

    public void Apply(SessionFinished finished, LearningSessionView view)
    {
        Ensure.That(finished).IsNotNull();
        Ensure.That(view).IsNotNull();

        view.Status = SessionStatus.Finished;
    }

    // Stub for #42's red run.
    public void Apply(ExhaustionNoted noted, LearningSessionView view) => throw new NotImplementedException();

    private static IEnumerable<LearningSessionChapterView> Chapters(IEnumerable<ChapterId> chapters, ChapterKind kind) =>
        chapters.Select(chapter => new LearningSessionChapterView { ChapterId = chapter.Value, Kind = kind, ProgressPercent = ProgressPercent.None.Value });

    private static LearningSessionChapterView ChapterOf(LearningSessionView view, ChapterId chapter) =>
        view.Chapters.Single(candidate => candidate.ChapterId == chapter.Value);
}
