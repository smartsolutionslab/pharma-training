using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts;

/// <summary>
/// Assessment's unit of work as its outbox behaves: what is published is sent only once committed, and a cancelled
/// token commits nothing. Publishes and commits are recorded in <see cref="Steps"/>, which the aggregate store shares,
/// so a test sees whether the examination was saved before its announcement.
/// </summary>
internal sealed class RecordingAssessmentUnitOfWork(List<string> steps) : IAssessmentUnitOfWork
{
    public const string Queued = "publish";
    public const string Committed = "commit";

    private readonly List<IntegrationEvent> queued = [];

    public List<IntegrationEvent> Published { get; } = [];

    public int Commits { get; private set; }

    public Task PublishAsync(IntegrationEvent integrationEvent)
    {
        queued.Add(integrationEvent);
        steps.Add(Queued);
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Published.AddRange(queued);
        queued.Clear();
        Commits++;
        steps.Add(Committed);
        return Task.CompletedTask;
    }
}
