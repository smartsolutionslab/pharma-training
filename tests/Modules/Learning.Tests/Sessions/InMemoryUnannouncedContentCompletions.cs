using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Sessions;

/// <summary>
/// The stored <c>ContentCompleted</c> events without a marker, as the event store would find them (#277), oldest first,
/// at most as many as asked for. Every question is recorded, so a test sees the window and the limit a pass asked for.
/// </summary>
internal sealed class InMemoryUnannouncedContentCompletions : IUnannouncedContentCompletions
{
    public List<StoredContentCompletion> Stored { get; } = [];

    public List<Question> Asked { get; } = [];

    public Task<IReadOnlyList<StoredContentCompletion>> FindAsync(DateTimeOffset storedFrom, DateTimeOffset storedUntil, int limit, CancellationToken cancellationToken)
    {
        Asked.Add(new Question(storedFrom, storedUntil, limit));
        IReadOnlyList<StoredContentCompletion> found = [.. Stored.Take(limit)];
        return Task.FromResult(found);
    }

    public sealed record Question(DateTimeOffset StoredFrom, DateTimeOffset StoredUntil, int Limit);
}
