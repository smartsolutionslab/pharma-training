using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Samples;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Samples.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Samples;

public class SampleTests
{
    [Fact]
    public void A_new_sample_raises_that_it_was_created_and_has_its_id_and_name()
    {
        var id = SampleId.New();

        var sample = Sample.Create(id, SampleName.From("Ada"));

        sample.Id.ShouldBe(id);
        sample.Name.ShouldBe(SampleName.From("Ada"));
        ((IEventSourced)sample).PendingEvents.ShouldHaveSingleItem().ShouldBe(new SampleCreated(id, SampleName.From("Ada")));
    }

    [Fact]
    public void A_sample_loaded_from_its_events_is_the_sample_that_raised_them()
    {
        var created = Sample.Create(SampleId.New(), SampleName.From("Ada"));
        var loaded = (IEventSourced)Activator.CreateInstance(typeof(Sample), nonPublic: true)!;

        loaded.Replay(((IEventSourced)created).PendingEvents);

        ((Sample)loaded).Id.ShouldBe(created.Id);
        ((Sample)loaded).Name.ShouldBe(created.Name);
        loaded.Version.ShouldBe(1);
    }
}
