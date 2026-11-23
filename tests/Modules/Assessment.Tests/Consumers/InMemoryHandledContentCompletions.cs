using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Consumers;

/// <summary>The content completions Assessment handled, in memory, and its unit of work: what is added counts only once committed.</summary>
internal sealed class InMemoryHandledContentCompletions : IHandledContentCompletions, IAssessmentUnitOfWork
{
    private readonly List<HandledContentCompletion> added = [];
    private bool throwOnNextCommit;

    public List<HandledContentCompletion> Handled { get; } = [];

    public int Commits { get; private set; }

    /// <summary>The next commit fails, as a crash or a lost connection would, and what was added is lost with its scope.</summary>
    public void ThrowOnNextCommit() => throwOnNextCommit = true;

    public Task<bool> ContainsAsync(Guid eventIdentifier, CancellationToken cancellationToken) =>
        Task.FromResult(Handled.Any(handled => handled.EventIdentifier == eventIdentifier));

    public void Add(HandledContentCompletion completion) => added.Add(completion);

    public Task PublishAsync(IntegrationEvent integrationEvent) => Task.CompletedTask;

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        if (throwOnNextCommit)
        {
            throwOnNextCommit = false;
            added.Clear();
            throw new InvalidOperationException("The commit failed.");
        }

        Handled.AddRange(added);
        added.Clear();
        Commits++;
        return Task.CompletedTask;
    }
}
