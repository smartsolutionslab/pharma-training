using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage.Tests;

/// <summary>
/// Published, AzureStoredBlobs reads the module's container keyless, under the service's own managed identity: the
/// <c>blobs</c> connection string has the account's endpoint and the container but no key (docs/adr/0002).
/// </summary>
public class AzureStoredBlobsTests
{
    [Fact]
    public async Task Without_an_account_key_or_a_managed_identity_it_fails_when_a_blob_is_first_looked_up_naming_both()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:blobs"] = "Endpoint=https://probeaccount.blob.core.windows.net/;ContainerName=editorial" })
            .Build();
        var blobs = new AzureStoredBlobs(configuration);

        var failure = await Should.ThrowAsync<InvalidOperationException>(() =>
            blobs.FindAsync(BlobName.From("handout.pdf"), TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("AccountKey");
        failure.Message.ShouldContain("AZURE_CLIENT_ID");
    }
}
