using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.Samples;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.Samples.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.Samples;

public class SampleTests
{
    [Fact]
    public void A_new_sample_has_its_id_and_name_and_raises_that_it_was_created()
    {
        var id = SampleId.New();

        var sample = Sample.Create(id, SampleName.From("Ada"));

        sample.Id.ShouldBe(id);
        sample.Name.ShouldBe(SampleName.From("Ada"));
        sample.DomainEvents.ShouldHaveSingleItem().ShouldBe(new SampleCreated(id, SampleName.From("Ada")));
    }
}
