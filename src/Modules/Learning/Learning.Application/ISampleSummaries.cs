using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.Samples;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

/// <summary>
/// The samples' table, the module's read model of its samples; implemented in its Infrastructure project. A sample
/// itself is its event stream (<see cref="ILearningAggregateStore"/>).
/// </summary>
public interface ISampleSummaries
{
    /// <summary>Writes the sample's row as its stored events leave it. Call it right after saving the sample's events; committing the module's unit of work saves the row.</summary>
    Task RecordAsync(Sample sample, CancellationToken cancellationToken);

    Task<Option<SampleSummary>> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>One page of the samples, by name.</summary>
    Task<PagedResult<SampleSummary>> ListAsync(Page page, PageSize pageSize, CancellationToken cancellationToken);
}
