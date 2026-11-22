using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// Signs <see cref="IBlobUrls"/> as SAS URLs on one blob of the module's own container: an upload URL may only create
/// its blob, a read URL may only read it, and both stop working when their lifetime ends (docs/adr/0002). The container is
/// the connection string <c>blobs</c>, which the AppHost passes from the module's <c>&lt;module&gt;-blobs</c> resource. In
/// run mode it has Azurite's account key, and URLs are service SAS signed with it; published, shared-key access is off and
/// it has none, so URLs are user delegation SAS, signed with a key the storage service issues to the service's own
/// identity (<see cref="IUserDelegationKeySource"/>), kept until a URL would outlive it. The connection string is read
/// when the first URL is signed, so a host without it starts and fails only where a URL is needed.
/// </summary>
public sealed class AzureBlobUrls : IBlobUrls
{
    // How long a user delegation key is asked for: longer than any URL lives, short enough that a key is rarely asked for.
    private static readonly TimeSpan KeyLifetime = TimeSpan.FromHours(1);

    private readonly TimeProvider time;

    private readonly IUserDelegationKeySource keys;

    private readonly Lazy<ModuleBlobLocation> location;

    private readonly Lazy<BlobContainerClient> signingContainer;

    private UserDelegationKey? key;

    /// <summary>Signs keyless under the managed identity <c>AZURE_CLIENT_ID</c> names where there is no account key.</summary>
    public AzureBlobUrls(IConfiguration configuration, TimeProvider time)
        : this(configuration, time, new ManagedIdentityUserDelegationKeys(configuration))
    {
    }

    public AzureBlobUrls(IConfiguration configuration, TimeProvider time, IUserDelegationKeySource keys)
    {
        Ensure.That(configuration).IsNotNull();
        Ensure.That(time).IsNotNull();
        Ensure.That(keys).IsNotNull();
        this.time = time;
        this.keys = keys;
        location = new Lazy<ModuleBlobLocation>(() => ModuleBlobContainer.Locate(configuration));
        signingContainer = new Lazy<BlobContainerClient>(() => ModuleBlobContainer.ForSigning(location.Value));
    }

    public Task<BlobUrl> CreateUploadUrlAsync(BlobName name, BlobUrlLifetime lifetime, CancellationToken cancellationToken) =>
        SignAsync(name, lifetime, BlobSasPermissions.Create, cancellationToken);

    public Task<BlobUrl> CreateReadUrlAsync(BlobName name, BlobUrlLifetime lifetime, CancellationToken cancellationToken) =>
        SignAsync(name, lifetime, BlobSasPermissions.Read, cancellationToken);

    private async Task<BlobUrl> SignAsync(BlobName name, BlobUrlLifetime lifetime, BlobSasPermissions permissions, CancellationToken cancellationToken)
    {
        Ensure.That(name).IsNotNull();
        Ensure.That(lifetime).IsNotNull();
        cancellationToken.ThrowIfCancellationRequested();

        // Whole seconds, rounded down: the signature carries no fractions, and rounding up would outlive the lifetime.
        var now = time.GetUtcNow();
        var expiresAt = now.Add(lifetime.Value);
        expiresAt = new DateTimeOffset(expiresAt.UtcTicks - (expiresAt.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);

        var where = location.Value;
        var sas = new BlobSasBuilder(permissions, expiresAt)
        {
            BlobContainerName = where.ContainerName,
            BlobName = name.Value,
            Resource = "b",
        };

        // A signature for an https endpoint works only over https (spr=https), so it never travels in the clear, on both
        // signing paths. The emulator, Azurite, serves http, which that would refuse; its URLs name no protocol.
        if (where.ServiceUri.Scheme == Uri.UriSchemeHttps)
        {
            sas.Protocol = SasProtocol.Https;
        }

        if (where.HasAccountKey)
        {
            return new BlobUrl(signingContainer.Value.GetBlobClient(name.Value).GenerateSasUri(sas), expiresAt);
        }

        var delegationKey = await DelegationKeyAsync(where.ServiceUri, now, expiresAt, cancellationToken);
        var url = new BlobUriBuilder(where.ServiceUri)
        {
            BlobContainerName = where.ContainerName,
            BlobName = name.Value,
            Sas = sas.ToSasQueryParameters(delegationKey, new BlobUriBuilder(where.ServiceUri).AccountName),
        };
        return new BlobUrl(url.ToUri(), expiresAt);
    }

    /// <summary>
    /// The user delegation key a URL that expires at <paramref name="expiresAt"/> is signed with: the one kept from before
    /// while it outlives the URL, a new one otherwise. Two URLs signed at once may each ask for one; both keys are valid.
    /// </summary>
    private async Task<UserDelegationKey> DelegationKeyAsync(Uri serviceUri, DateTimeOffset now, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var kept = Volatile.Read(ref key);
        if (kept is not null && kept.SignedExpiresOn >= expiresAt)
        {
            return kept;
        }

        var keyExpiresOn = now.Add(KeyLifetime);
        var issued = await keys.GetKeyAsync(serviceUri, keyExpiresOn > expiresAt ? keyExpiresOn : expiresAt, cancellationToken);
        Volatile.Write(ref key, issued);
        return issued;
    }
}
