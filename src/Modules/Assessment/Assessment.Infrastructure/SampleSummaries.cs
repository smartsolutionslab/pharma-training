using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Samples;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Infrastructure;

/// <summary>The samples' table, written after each sample's events; the module's unit of work saves what is recorded.</summary>
public sealed class SampleSummaries(AssessmentDbContext context) : ISampleSummaries
{
    public async Task RecordAsync(Sample sample, CancellationToken cancellationToken)
    {
        Ensure.That(sample).IsNotNull();

        var row = await context.Set<SampleSummary>().SingleOrDefaultAsync(row => row.Id == sample.Id.Value, cancellationToken);
        if (row is null)
        {
            row = new SampleSummary { Id = sample.Id.Value };
            context.Add(row);
        }

        row.Name = sample.Name.Value;
    }

    public async Task<Option<SampleSummary>> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Set<SampleSummary>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken) is { } row
            ? Option<SampleSummary>.Some(row)
            : Option<SampleSummary>.None;

    public async Task<PagedResult<SampleSummary>> ListAsync(Page page, PageSize pageSize, CancellationToken cancellationToken)
    {
        Ensure.That(page).IsNotNull();
        Ensure.That(pageSize).IsNotNull();

        var rows = context.Set<SampleSummary>().AsNoTracking();
        var total = await rows.CountAsync(cancellationToken);
        var items = await rows
            .OrderBy(row => row.Name)
            .ThenBy(row => row.Id)
            .Skip(page.Skip(pageSize))
            .Take(pageSize.Value)
            .ToListAsync(cancellationToken);
        return new PagedResult<SampleSummary>(items, total, page.Value, pageSize.Value);
    }
}
