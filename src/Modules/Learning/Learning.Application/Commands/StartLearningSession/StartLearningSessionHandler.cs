using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Commands.StartLearningSession;

/// <summary>
/// At most one unfinished session per learner and course version: a run not finished yet is resumed, and otherwise
/// the next run starts with the chapters of Learning's copy of the version. The new run's id is derived from learner,
/// version and run, so of two concurrent first starts the second stream is refused by the store (409 already-exists).
/// A created session is announced as LearningSessionStartedV1 once its stream is stored; a resume announces nothing,
/// and the losing concurrent start is refused before it publishes.
/// </summary>
public sealed class StartLearningSessionHandler(
    ICourseVersionCopies versions,
    ILearningSessions sessions,
    ILearningAggregateStore store,
    ILearningUnitOfWork unitOfWork,
    TimeProvider time)
    : ICommandHandler<StartLearningSession, Result<LearningSessionStart, ApiError>>
{
    public async Task<Result<LearningSessionStart, ApiError>> HandleAsync(StartLearningSession command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var copy = await versions.FindAsync(command.Version, cancellationToken);
        if (!copy.HasValue)
        {
            return Failure(LearningSessionFailures.CourseVersionNotFound(command.Version.Value));
        }

        var runs = await sessions.ListAsync(command.Learner, command.Version, cancellationToken);
        var unfinished = runs
            .Where(run => run.Status != SessionStatus.Finished)
            .OrderByDescending(run => run.StartedAt)
            .FirstOrDefault();
        if (unfinished is not null)
        {
            return Success(new LearningSessionStart(unfinished.Id, Created: false));
        }

        var session = Start(command, copy.Value, SessionRun.From(runs.Count + 1));
        var started = ((IEventSourced)session).PendingEvents.OfType<SessionStarted>().Single();
        await store.SaveAsync(session, cancellationToken);
        await unitOfWork.PublishAsync(started.ToIntegrationEvent());
        await unitOfWork.CommitAsync(cancellationToken);
        return Success(new LearningSessionStart(session.Id.Value, Created: true));
    }

    private LearningSession Start(StartLearningSession command, CourseVersionCopy version, SessionRun run)
    {
        var chapters = version.Chapters.OrderBy(chapter => chapter.Position).ToList();
        return LearningSession.Start(
            LearningSessionId.For(command.Learner, command.Version, run),
            command.Learner,
            CourseId.From(version.CourseId),
            command.Version,
            ChaptersOf(chapters, ChapterKind.Text),
            ChaptersOf(chapters, ChapterKind.Pdf),
            ChaptersOf(chapters, ChapterKind.Video),
            Timestamp.From(time.GetUtcNow()));
    }

    private static List<ChapterId> ChaptersOf(IEnumerable<CourseVersionChapterCopy> chapters, ChapterKind kind) =>
        [.. chapters.Where(chapter => chapter.Kind == kind).Select(chapter => ChapterId.From(chapter.ChapterId))];
}
