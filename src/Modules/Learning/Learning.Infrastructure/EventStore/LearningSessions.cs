using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using Marten;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.EventStore;

/// <summary>The sessions' views, which <see cref="LearningSessionViewProjection"/> keeps inline with their streams.</summary>
public sealed class LearningSessions(IDocumentStore store) : ILearningSessions
{
    public async Task<IReadOnlyList<LearningSessionView>> ListAsync(PersonId learner, CourseVersionId version, CancellationToken cancellationToken)
    {
        Ensure.That(learner).IsNotNull();
        Ensure.That(version).IsNotNull();

        await using var session = store.QuerySession();
        return await session.Query<LearningSessionView>()
            .Where(view => view.LearnerId == learner.Value && view.CourseVersionId == version.Value)
            .ToListAsync(cancellationToken);
    }

    public async Task<Option<LearningSessionView>> FindAsync(LearningSessionId id, CancellationToken cancellationToken)
    {
        Ensure.That(id).IsNotNull();

        await using var session = store.QuerySession();
        return await session.LoadAsync<LearningSessionView>(id.Value, cancellationToken) is { } view
            ? Option<LearningSessionView>.Some(view)
            : Option<LearningSessionView>.None;
    }
}
