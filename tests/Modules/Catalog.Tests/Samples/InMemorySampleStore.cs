using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Domain.Samples;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Tests.Samples;

/// <summary>The samples table in memory, and the module's unit of work over it: what is added is stored once committed.</summary>
internal sealed class InMemorySampleStore : ISampleStore, ICatalogUnitOfWork
{
    private readonly List<Sample> added = [];

    public List<Sample> Stored { get; } = [];

    public void Add(Sample sample) => added.Add(sample);

    public Task<Option<Sample>> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.SingleOrDefault(sample => sample.Id == id) is { } sample ? Option<Sample>.Some(sample) : Option<Sample>.None);

    public Task<PagedResult<Sample>> ListAsync(Page page, PageSize pageSize, CancellationToken cancellationToken)
    {
        List<Sample> items = [.. Stored
            .OrderBy(sample => sample.Name, StringComparer.Ordinal)
            .ThenBy(sample => sample.Id)
            .Skip(page.Skip(pageSize))
            .Take(pageSize.Value)];
        return Task.FromResult(new PagedResult<Sample>(items, Stored.Count, page.Value, pageSize.Value));
    }

    public Task PublishAsync(IntegrationEvent integrationEvent) => Task.CompletedTask;

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Stored.AddRange(added);
        added.Clear();
        return Task.CompletedTask;
    }
}
