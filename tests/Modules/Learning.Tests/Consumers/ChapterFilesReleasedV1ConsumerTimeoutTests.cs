using JasperFx;
using JasperFx.CodeGeneration;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;
using Wolverine;
using Wolverine.Runtime.Handlers;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Consumers;

/// <summary>
/// Wolverine cancels a handler after its execution timeout, 60 seconds unless the chain has its own. A released video is
/// copied in one request that may take up to the copy's network timeout, so the consumer carries a timeout of its own
/// (docs/adr/0002): copies start only while the 15-minute source URL is valid, and the last one started may then run for
/// a whole network timeout, so the message needs at least the URL's lifetime plus one network timeout.
/// </summary>
public class ChapterFilesReleasedV1ConsumerTimeoutTests
{
    [Fact]
    public void Its_chain_is_given_thirty_minutes_instead_of_wolverines_default()
    {
        var chain = HandlerChain.For<ChapterFilesReleasedV1Consumer>(nameof(ChapterFilesReleasedV1Consumer.HandleAsync), new HandlerGraph());

        new ConsumerTimeoutPolicy().Apply([chain], new GenerationRules(), Substitute.For<IServiceContainer>());

        var timeout = TimeSpan.FromSeconds(chain.ExecutionTimeoutInSeconds ?? new WolverineOptions().DefaultExecutionTimeout.TotalSeconds);
        timeout.ShouldBe(TimeSpan.FromMinutes(30));
        timeout.ShouldBeGreaterThanOrEqualTo(BlobUrlLifetime.Maximum + AzureBlobCopies.NetworkTimeout);
    }
}
