using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.CreateSample;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Samples;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Samples;

public class CreateSampleTests
{
    [Fact]
    public async Task A_created_samples_event_is_stored_in_its_stream_its_read_model_written_and_its_id_answered()
    {
        var samples = new InMemorySamples();

        var created = await new CreateSampleHandler(samples, samples, samples).HandleAsync(new CreateSample(SampleName.From("Ada")), TestContext.Current.CancellationToken);

        samples.Streams[created.Value].ShouldHaveSingleItem();
        var summary = samples.Summaries.ShouldHaveSingleItem();
        summary.Id.ShouldBe(created.Value);
        summary.Name.ShouldBe("Ada");
        created.Value.Version.ShouldBe(7);
    }
}
