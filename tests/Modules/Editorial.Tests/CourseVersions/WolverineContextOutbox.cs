using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;
using Wolverine.Runtime;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// An outbox that is a Wolverine <see cref="MessageContext"/>, as every outbox the host resolves is, over a context
/// whose save a test decides; a domain-event scraper is given it. Wolverine's combined save-and-flush throws: the unit
/// of work saves, commits and flushes apart, so a test that reaches it fails.
/// </summary>
public sealed class WolverineContextOutbox(IWolverineRuntime runtime, EditorialDbContext dbContext)
    : MessageContext(runtime), IDbContextOutbox<EditorialDbContext>
{
    public EditorialDbContext DbContext { get; } = dbContext;

    public Task SaveChangesAndFlushMessagesAsync(CancellationToken token = default) =>
        throw new InvalidOperationException("TryCommitAsync saves, commits and flushes apart; it never calls Wolverine's combined method.");

    public Task SaveChangesAndFlushMessagesAsync(MultiFlushMode multiFlushMode, CancellationToken token = default) =>
        throw new InvalidOperationException("TryCommitAsync saves, commits and flushes apart; it never calls Wolverine's combined method.");
}
