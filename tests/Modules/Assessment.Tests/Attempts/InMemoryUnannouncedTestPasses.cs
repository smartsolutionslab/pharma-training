using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts;

/// <summary>
/// The stored <c>ExaminationPassed</c> events without a marker, as the event store would find them (#277), oldest first,
/// at most as many as asked for. Every question is recorded, so a test sees the window and the limit a pass asked for.
/// </summary>
internal sealed class InMemoryUnannouncedTestPasses : IUnannouncedTestPasses
{
    public List<StoredTestPass> Stored { get; } = [];

    public List<Question> Asked { get; } = [];

    public Task<IReadOnlyList<StoredTestPass>> FindAsync(DateTimeOffset storedFrom, DateTimeOffset storedUntil, int limit, CancellationToken cancellationToken)
    {
        Asked.Add(new Question(storedFrom, storedUntil, limit));
        IReadOnlyList<StoredTestPass> found = [.. Stored.Take(limit)];
        return Task.FromResult(found);
    }

    public sealed record Question(DateTimeOffset StoredFrom, DateTimeOffset StoredUntil, int Limit);
}
