using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Consumers;

/// <summary>The content completions Assessment handled, in memory, and its unit of work: what is added counts only once committed.</summary>
internal sealed class InMemoryHandledContentCompletions : IHandledContentCompletions, IAssessmentUnitOfWork
{
    private readonly List<HandledContentCompletion> added = [];

    public List<HandledContentCompletion> Handled { get; } = [];

    public int Commits { get; private set; }

    public Task<bool> ContainsAsync(Guid eventIdentifier, CancellationToken cancellationToken) =>
        Task.FromResult(Handled.Any(handled => handled.EventIdentifier == eventIdentifier));

    public void Add(HandledContentCompletion completion) => added.Add(completion);

    public Task PublishAsync(IntegrationEvent integrationEvent) => Task.CompletedTask;

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Handled.AddRange(added);
        added.Clear();
        Commits++;
        return Task.CompletedTask;
    }
}
