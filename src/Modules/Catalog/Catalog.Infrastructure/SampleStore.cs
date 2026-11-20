using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Domain.Samples;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Infrastructure;

/// <summary>The samples table. The module's unit of work saves what is added.</summary>
public sealed class SampleStore(CatalogDbContext context) : ISampleStore
{
    public void Add(Sample sample) => context.Add(sample);

    public async Task<Option<Sample>> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Set<Sample>().AsNoTracking().SingleOrDefaultAsync(sample => sample.Id == id, cancellationToken) is { } sample
            ? Option<Sample>.Some(sample)
            : Option<Sample>.None;

    public async Task<PagedResult<Sample>> ListAsync(Page page, PageSize pageSize, CancellationToken cancellationToken)
    {
        Ensure.That(page).IsNotNull();
        Ensure.That(pageSize).IsNotNull();

        var samples = context.Set<Sample>().AsNoTracking();
        var total = await samples.CountAsync(cancellationToken);
        var items = await samples
            .OrderBy(sample => sample.Name)
            .ThenBy(sample => sample.Id)
            .Skip(page.Skip(pageSize))
            .Take(pageSize.Value)
            .ToListAsync(cancellationToken);
        return new PagedResult<Sample>(items, total, page.Value, pageSize.Value);
    }
}
