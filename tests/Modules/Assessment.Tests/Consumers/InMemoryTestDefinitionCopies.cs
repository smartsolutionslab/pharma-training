using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Consumers;

/// <summary>Assessment's local copy of the published test definitions in memory, and its unit of work: what is added is stored only once committed.</summary>
internal sealed class InMemoryTestDefinitionCopies : ITestDefinitionCopies, IAssessmentUnitOfWork
{
    private readonly List<TestDefinitionCopy> added = [];

    public List<TestDefinitionCopy> Definitions { get; } = [];

    public int Commits { get; private set; }

    public Task<bool> ExistsAsync(CourseVersionId version, CancellationToken cancellationToken) =>
        Task.FromResult(Definitions.Any(stored => stored.CourseVersionId == version.Value));

    /// <summary>The stored copy of the version, as the store reads it back: questions and options in position order.</summary>
    public Task<Option<TestDefinitionCopy>> FindAsync(CourseVersionId version, CancellationToken cancellationToken)
    {
        var stored = Definitions.SingleOrDefault(candidate => candidate.CourseVersionId == version.Value);
        if (stored is null)
        {
            return Task.FromResult(Option<TestDefinitionCopy>.None);
        }

        foreach (var question in stored.Questions)
        {
            question.Options = [.. question.Options.OrderBy(option => option.Position)];
        }

        stored.Questions = [.. stored.Questions.OrderBy(question => question.Position)];
        return Task.FromResult(Option<TestDefinitionCopy>.Some(stored));
    }

    public void Add(TestDefinitionCopy definition) => added.Add(definition);

    public Task PublishAsync(IntegrationEvent integrationEvent) => Task.CompletedTask;

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Definitions.AddRange(added);
        added.Clear();
        Commits++;
        return Task.CompletedTask;
    }
}
