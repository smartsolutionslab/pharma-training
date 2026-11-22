using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;

/// <summary>
/// Links into the web app at <c>Frontend:BaseUrl</c>, which the AppHost passes to every service. The setting is read at
/// every link, so a host without it starts, fails only where a link is needed, naming the key, and links once it is
/// supplied. A link carries a token, so the base is https, or http only to a loopback host (the local dev server).
/// </summary>
public sealed class FrontendLinks : IFrontendLinks
{
    private const string BaseUrlKey = "Frontend:BaseUrl";

    private readonly IConfiguration configuration;

    public FrontendLinks(IConfiguration configuration)
    {
        Ensure.That(configuration).IsNotNull();
        this.configuration = configuration;
    }

    public Uri LinkTo(string pathAndFragment)
    {
        Ensure.That(pathAndFragment).IsNotNullOrWhiteSpace().StartsWith("/", StringComparison.Ordinal);
        return new Uri(BaseUrl(configuration).AbsoluteUri.TrimEnd('/') + pathAndFragment, UriKind.Absolute);
    }

    internal static Uri BaseUrl(IConfiguration configuration)
    {
        var value = configuration[BaseUrlKey];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttps && !(url.Scheme == Uri.UriSchemeHttp && SmtpSettings.IsLoopback(url))))
        {
            throw new InvalidOperationException($"The web app's address is missing: set {BaseUrlKey} to its absolute https URL (http only for a loopback host).");
        }

        return url;
    }
}
