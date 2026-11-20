using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.Samples.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.Samples;

/// <summary>
/// The module's sample aggregate, emitted by fabric add-module: a record with a name, the sum of its events, that the
/// sample's endpoints create, list and read back. Rename it after the module's first aggregate (<c>Invoice</c>) or
/// delete it with everything named Sample (AGENTS.md).
/// </summary>
public sealed class Sample : EventSourcedAggregate<SampleId>
{
    // Only loading creates an empty sample, which its first event fills.
    private Sample()
    {
    }

    public SampleName Name { get; private set; } = null!;

    /// <summary>A new sample: what happened is raised as <see cref="SampleCreated"/> and applied by <see cref="Apply"/>.</summary>
    public static Sample Create(SampleId id, SampleName name)
    {
        Ensure.That(id).IsNotNull();
        Ensure.That(name).IsNotNull();

        var sample = new Sample();
        sample.Raise(new SampleCreated(id, name));
        return sample;
    }

    private void Apply(SampleCreated created)
    {
        Id = created.Id;
        Name = created.Name;
    }
}
