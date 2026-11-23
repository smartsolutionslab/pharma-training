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
/// identity (<see cref="IUserDelegationKeySource"/>), kept until a URL would outlive it, and valid over https alone. URLs
/// signed at once share one request for a key, and a key never replaces a kept one that outlives it. The connection string
/// is read when the first URL is signed, so a host without it starts and fails only where a URL is needed; without a key,
/// its endpoint must be an account's blob service, <c>https://&lt;account&gt;.blob.core.windows.net/</c>, whose account
/// the URLs are signed for.
/// </summary>
public sealed class AzureBlobUrls : IBlobUrls
{
    // The host of an account's blob service in Azure's public cloud; its first label is the account the URLs are signed for.
    private const string BlobServiceHostSuffix = ".blob.core.windows.net";

    // How long a user delegation key is asked for: longer than any URL lives, short enough that a key is rarely asked for.
    private static readonly TimeSpan KeyLifetime = TimeSpan.FromHours(1);

    private readonly Lock gate = new();

    private readonly TimeProvider time;

    private readonly IUserDelegationKeySource keys;

    private readonly Lazy<ModuleBlobLocation> location;

    private readonly Lazy<BlobContainerClient> signingContainer;

    private UserDelegationKey? key;

    // The request for a key under way, which every URL it outlives waits for instead of asking for one of its own.
    private Lazy<Task<UserDelegationKey>>? pending;

    private DateTimeOffset pendingExpiresOn;

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

        var accountName = AccountName(where.ServiceUri);
        // Keyless URLs are published ones: the browser uses them over https alone.
        sas.Protocol = SasProtocol.Https;
        var delegationKey = await DelegationKeyAsync(where.ServiceUri, now, expiresAt, cancellationToken);
        var url = new BlobUriBuilder(where.ServiceUri)
        {
            BlobContainerName = where.ContainerName,
            BlobName = name.Value,
            Sas = sas.ToSasQueryParameters(delegationKey, accountName),
        };
        return new BlobUrl(url.ToUri(), expiresAt);
    }

    /// <summary>
    /// The account a keyless URL is signed for, the first label of its blob service's host; an endpoint that is no
    /// account's blob service (a custom domain, a proxy) would be signed for an account it is not, so it fails, naming it.
    /// </summary>
    private static string AccountName(Uri serviceUri)
    {
        var host = serviceUri.Host;
        if (!host.EndsWith(BlobServiceHostSuffix, StringComparison.OrdinalIgnoreCase)
            || host[..^BlobServiceHostSuffix.Length] is not { Length: > 0 } account
            || account.Contains('.', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{ModuleBlobContainer.ConnectionStringName} has no {ModuleBlobContainer.AccountKeyKey}, and its endpoint {serviceUri} is not an account's blob service (https://<account>{BlobServiceHostSuffix}/); keyless blob URLs are signed for the account that host names.");
        }

        return account;
    }

    /// <summary>
    /// The user delegation key a URL that expires at <paramref name="expiresAt"/> is signed with: the one kept from before
    /// while it outlives the URL, else the one already asked for while it will, else a new one. URLs signed at once share
    /// one request, which no caller's cancellation stops; a key that arrives keeps its place only while no key kept since
    /// outlives it.
    /// </summary>
    private async Task<UserDelegationKey> DelegationKeyAsync(Uri serviceUri, DateTimeOffset now, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        Lazy<Task<UserDelegationKey>> request;
        lock (gate)
        {
            if (key is not null && key.SignedExpiresOn >= expiresAt)
            {
                return key;
            }

            if (pending is null || pendingExpiresOn < expiresAt)
            {
                var keyExpiresOn = now.Add(KeyLifetime);
                var asked = keyExpiresOn > expiresAt ? keyExpiresOn : expiresAt;
                pending = new Lazy<Task<UserDelegationKey>>(() => keys.GetKeyAsync(serviceUri, asked, CancellationToken.None));
                pendingExpiresOn = asked;
            }

            request = pending;
        }

        try
        {
            var issued = await request.Value.WaitAsync(cancellationToken);
            lock (gate)
            {
                if (key is null || issued.SignedExpiresOn > key.SignedExpiresOn)
                {
                    key = issued;
                }
            }

            return issued;
        }
        finally
        {
            lock (gate)
            {
                if (ReferenceEquals(pending, request) && request.Value.IsCompleted)
                {
                    pending = null;
                }
            }
        }
    }
}
