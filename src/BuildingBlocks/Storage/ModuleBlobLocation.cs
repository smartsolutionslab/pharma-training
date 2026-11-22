namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// The module's container as the connection string <c>blobs</c> names it: its name, and either the account's connection
/// string with its key (Azurite in run mode) or, without a key, the blob service's endpoint (published, docs/adr/0002).
/// </summary>
/// <param name="ContainerName">The module's own container.</param>
/// <param name="AccountConnectionString">The account's connection string with its key, or none where it has no key.</param>
/// <param name="ServiceUri">The blob service's endpoint.</param>
internal sealed record ModuleBlobLocation(string ContainerName, string? AccountConnectionString, Uri ServiceUri)
{
    /// <summary>Whether URLs are signed with the account's key; otherwise with a user delegation key.</summary>
    public bool HasAccountKey => AccountConnectionString is not null;
}
