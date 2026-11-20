namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;

/// <summary>
/// A sample as its list and its page read it: a row of the samples' table, written right after the sample's events are stored.
/// </summary>
public sealed class SampleSummary
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
