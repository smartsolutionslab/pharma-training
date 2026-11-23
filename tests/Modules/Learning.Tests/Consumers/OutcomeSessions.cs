using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Sessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Consumers;

/// <summary>
/// One learner's sessions of one course version in memory, as Assessment's outcomes find them, and those outcomes as they
/// arrive: published by Assessment's unit of work, so each carries its metadata. A redelivery carries the same metadata;
/// a republish an identifier of its own.
/// </summary>
internal sealed class OutcomeSessions
{
    /// <summary>When the learner passed: an hour before the clock, so a session finished at the clock's time would show.</summary>
    public static readonly DateTimeOffset PassedAt = new(2027, 3, 1, 8, 0, 0, TimeSpan.Zero);

    public InMemoryLearningSessions Store { get; } = new();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));

    public PersonId Learner { get; } = PersonId.From(Guid.CreateVersion7());

    public CourseId Course { get; } = CourseId.From(Guid.CreateVersion7());

    public CourseVersionId Version { get; } = CourseVersionId.From(Guid.CreateVersion7());

    public ChapterId Intro { get; } = ChapterId.From(Guid.CreateVersion7());

    public ChapterId Handbook { get; } = ChapterId.From(Guid.CreateVersion7());

    public ChapterId Film { get; } = ChapterId.From(Guid.CreateVersion7());

    /// <summary>How long the film runs.</summary>
    public VideoDuration FilmDuration { get; } = VideoDuration.From(60);

    public static AttemptsExhaustedV1 Redelivered(AttemptsExhaustedV1 exhausted) => exhausted with { };

    public static TestPassedV1 Redelivered(TestPassedV1 passed) => passed with { };

    public static TestPassedV1 Republished(TestPassedV1 passed) => passed with { Metadata = new EventMetadata(Guid.CreateVersion7(), PassedAt) };

    /// <summary>The learner's first run of the version, one chapter of each kind, stored as starting it stores it.</summary>
    public async Task<LearningSessionId> StartedAsync()
    {
        var id = LearningSessionId.For(Learner, Version, SessionRun.First);
        var session = LearningSession.Start(id, Learner, Course, Version, [Intro], [Handbook], [Film], Now());
        await Store.SaveAsync(session, TestContext.Current.CancellationToken);
        return id;
    }

    /// <summary>A session whose every chapter the learner worked through, so Assessment could open its test.</summary>
    public async Task<LearningSessionId> CompletedAsync()
    {
        var id = await StartedAsync();
        await ChangeAsync(id, session =>
        {
            session.RecordProgress(Intro, ProgressPercent.Complete, Option<VideoDuration>.None, Now()).IsSuccess.ShouldBeTrue();
            session.RecordProgress(Handbook, ProgressPercent.Complete, Option<VideoDuration>.None, Now()).IsSuccess.ShouldBeTrue();
            // The film was watched to the end since the session started (ADR 0024).
            session.RecordProgress(Film, ProgressPercent.Complete, Option<VideoDuration>.Some(FilmDuration), Timestamp.From(Time.GetUtcNow().AddSeconds(FilmDuration.Value))).IsSuccess.ShouldBeTrue();
        });
        (await LoadAsync(id)).Status.ShouldBe(SessionStatus.ContentCompleted);
        return id;
    }

    /// <summary>Stores what the learner or another use case did to the session meanwhile.</summary>
    public async Task ChangeAsync(LearningSessionId id, Action<LearningSession> change)
    {
        var session = await LoadAsync(id);
        change(session);
        await Store.SaveAsync(session, TestContext.Current.CancellationToken);
    }

    public async Task<LearningSession> LoadAsync(LearningSessionId id) =>
        (await Store.LoadAsync<LearningSession>(id, TestContext.Current.CancellationToken)).Value;

    public IReadOnlyList<IDomainEvent> Stream(LearningSessionId id) => Store.Streams[id.Value];

    public AttemptsExhaustedV1 Exhausted(LearningSessionId id, params ChapterId[] failed) =>
        new(id.Value, Learner.Value, Course.Value, Version.Value, [.. failed.Select(chapter => chapter.Value)])
        {
            Metadata = new EventMetadata(Guid.CreateVersion7(), Time.GetUtcNow()),
        };

    public TestPassedV1 Passed(LearningSessionId id) =>
        new(Guid.CreateVersion7(), id.Value, Learner.Value, Course.Value, Version.Value, 90, PassedAt)
        {
            Metadata = new EventMetadata(Guid.CreateVersion7(), PassedAt),
        };

    public Timestamp Now() => Timestamp.From(Time.GetUtcNow());
}
