using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>Deletes a blob in the module's own container (#115). Not implemented yet: slice 4's red tests.</summary>
public sealed class AzureBlobDeletes : IBlobDeletes
{
    private readonly IConfiguration configuration;

    public AzureBlobDeletes(IConfiguration configuration)
    {
        Ensure.That(configuration).IsNotNull();
        this.configuration = configuration;
    }

    public Task DeleteAsync(BlobName name, CancellationToken cancellationToken) =>
        throw new NotImplementedException($"{configuration.GetType().Name}");
}
