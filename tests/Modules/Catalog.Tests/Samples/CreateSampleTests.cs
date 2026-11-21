using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.Commands.CreateSample;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Tests.Samples;

public class CreateSampleTests
{
    [Fact]
    public async Task A_created_sample_is_stored_with_its_name_once_the_unit_of_work_commits_and_its_id_answered()
    {
        var store = new InMemorySampleStore();

        var created = await new CreateSampleHandler(store, store).HandleAsync(new CreateSample("Ada"), TestContext.Current.CancellationToken);

        var sample = store.Stored.ShouldHaveSingleItem();
        created.Value.ShouldBe(sample.Id);
        sample.Name.ShouldBe("Ada");
        created.Value.Version.ShouldBe(7);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789x")]
    public async Task A_blank_name_or_one_longer_than_the_maximum_is_refused_and_nothing_stored(string name)
    {
        var store = new InMemorySampleStore();

        var created = await new CreateSampleHandler(store, store).HandleAsync(new CreateSample(name), TestContext.Current.CancellationToken);

        created.Error.Code.ShouldBe("catalog.invalid-sample-name");
        store.Stored.ShouldBeEmpty();
    }
}
