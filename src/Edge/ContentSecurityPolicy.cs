using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartSolutionsLab.PharmaTraining.Edge;

/// <summary>
/// The Content-Security-Policy the edge answers <c>index.html</c> with (the security review of #114, docs/adr/0005): scripts
/// from the edge's own origin and the page's inline scripts by their SHA-256 hash alone, never <c>'unsafe-inline'</c>;
/// styles from the edge's origin and inline (the components set inline styles); images, fonts and everything else from the
/// edge's origin; video from <c>blob:</c> too, where the editorial module reads an upload's duration from the file itself;
/// calls to the edge and to the origins it is given (the identity provider's discovery and token endpoints, blob storage's
/// signed upload URLs). No plugins, no other base URL, and no page may frame the app.
/// </summary>
public static partial class ContentSecurityPolicy
{
    /// <summary>The policy for <paramref name="page"/>, whose inline scripts it allows by hash, calling <paramref name="connectOrigins"/> besides the edge.</summary>
    public static string For(string page, IEnumerable<string> connectOrigins)
    {
        Ensure.That(page).IsNotNull();
        Ensure.That(connectOrigins).IsNotNull();

        string[] scripts = ["'self'", .. InlineScripts(page).Select(Hash).Distinct(StringComparer.Ordinal)];
        string[] connections = ["'self'", .. connectOrigins.Distinct(StringComparer.OrdinalIgnoreCase)];
        string[] directives =
        [
            "default-src 'self'",
            $"script-src {string.Join(' ', scripts)}",
            "style-src 'self' 'unsafe-inline'",
            "img-src 'self' data:",
            "font-src 'self'",
            "media-src 'self' blob:",
            $"connect-src {string.Join(' ', connections)}",
            "object-src 'none'",
            "base-uri 'self'",
            "frame-ancestors 'none'",
            "form-action 'self'",
        ];
        return string.Join("; ", directives);
    }

    /// <summary>The text between the tags of each <c>&lt;script&gt;</c> without <c>src</c>, exactly as it stands: what a browser hashes.</summary>
    public static IEnumerable<string> InlineScripts(string page)
    {
        Ensure.That(page).IsNotNull();

        return Script().Matches(page)
            .Where(script => !Source().IsMatch(script.Groups["attributes"].Value))
            .Select(script => script.Groups["body"].Value);
    }

    /// <summary>The origin of an absolute address (<c>https://auth.example/realms/x</c> is <c>https://auth.example</c>), as a source a policy names.</summary>
    public static string OriginOf(string address)
    {
        Ensure.That(address).IsNotNullOrWhiteSpace()
            .Satisfies(value => Uri.TryCreate(value, UriKind.Absolute, out _), "The address must be absolute.");

        return new Uri(address, UriKind.Absolute).GetLeftPart(UriPartial.Authority);
    }

    private static string Hash(string script) => $"'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)))}'";

    [GeneratedRegex(@"<script(?<attributes>(?:\s[^>]*)?)>(?<body>.*?)</script\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Script();

    [GeneratedRegex(@"(?:^|\s)src\s*=", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Source();
}
