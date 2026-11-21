using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;

/// <summary>
/// Links into the web app at <c>Frontend:BaseUrl</c>, which the AppHost passes to every service. The setting is read at
/// the first link, so a host without it starts and fails only where a link is needed, naming the key.
/// </summary>
public sealed class FrontendLinks : IFrontendLinks
{
    private const string BaseUrlKey = "Frontend:BaseUrl";

    private readonly Lazy<Uri> baseUrl;

    public FrontendLinks(IConfiguration configuration)
    {
        Ensure.That(configuration).IsNotNull();
        baseUrl = new Lazy<Uri>(() => BaseUrl(configuration));
    }

    public Uri LinkTo(string pathAndFragment)
    {
        Ensure.That(pathAndFragment).IsNotNullOrWhiteSpace().StartsWith("/", StringComparison.Ordinal);
        return new Uri(baseUrl.Value.AbsoluteUri.TrimEnd('/') + pathAndFragment, UriKind.Absolute);
    }

    private static Uri BaseUrl(IConfiguration configuration)
    {
        var value = configuration[BaseUrlKey];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"The web app's address is missing: set {BaseUrlKey} to its absolute http or https URL.");
        }

        return url;
    }
}
