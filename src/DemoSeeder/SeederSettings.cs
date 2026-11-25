using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>
/// The <c>DemoSeeder:*</c> settings, read and guarded before any call: the target is Local or Staging and nothing else;
/// Local seeds a loopback edge with the master realm's administrator; Staging seeds an https edge with a client of its
/// own and the users' password, never a password-based administrator. A refusal names the key, never its value.
/// </summary>
internal sealed record SeederSettings(
    bool Staging,
    Uri EdgeUrl,
    Uri WebOrigin,
    Uri MailpitUrl,
    Uri? KeycloakAdminUrl,
    string? AdminUser,
    string? AdminPassword,
    string? ClientId,
    string? ClientSecret,
    string? UserPassword,
    TimeSpan PollTimeout)
{
    private const string Section = "DemoSeeder";

    private static readonly TimeSpan DefaultPollTimeout = TimeSpan.FromMinutes(10);

    /// <summary>The password a demo user signs in with: the staging secret, or locally their username.</summary>
    public string PasswordOf(string username) => UserPassword ?? username;

    public static bool TryRead(IConfiguration configuration, [NotNullWhen(true)] out SeederSettings? settings, [NotNullWhen(false)] out string? refusal)
    {
        settings = null;
        var target = Value(configuration, "Target");
        if (target is not ("Local" or "Staging"))
        {
            refusal = Refused("Target", "must be Local or Staging; the seeder seeds no other environment");
            return false;
        }

        var staging = target == "Staging";
        if (!TryUri(configuration, "EdgeUrl", out var edgeUrl, out refusal)
            || !TryUri(configuration, "WebOrigin", out var webOrigin, out refusal)
            || !TryUri(configuration, "MailpitUrl", out var mailpitUrl, out refusal))
        {
            return false;
        }

        if (!staging && !edgeUrl.IsLoopback)
        {
            refusal = Refused("EdgeUrl", "must name a loopback host with the target Local");
            return false;
        }

        if (staging && edgeUrl.Scheme != Uri.UriSchemeHttps)
        {
            refusal = Refused("EdgeUrl", "must be https with the target Staging");
            return false;
        }

        Uri? keycloakAdminUrl = null;
        if (Value(configuration, "KeycloakAdminUrl") is not null && !TryUri(configuration, "KeycloakAdminUrl", out keycloakAdminUrl, out refusal))
        {
            return false;
        }

        var adminUser = Value(configuration, "AdminUser");
        var adminPassword = Value(configuration, "AdminPassword");
        var clientId = Value(configuration, "ClientId");
        var clientSecret = Value(configuration, "ClientSecret");
        var userPassword = Value(configuration, "UserPassword");
        if (staging)
        {
            if (adminUser is not null || adminPassword is not null)
            {
                refusal = Refused(adminPassword is not null ? "AdminPassword" : "AdminUser", "is not accepted with the target Staging, which administers users with its client alone");
                return false;
            }

            var missing = new[] { ("ClientId", clientId), ("ClientSecret", clientSecret), ("UserPassword", userPassword) }.FirstOrDefault(each => each.Item2 is null).Item1;
            if (missing is not null)
            {
                refusal = Refused(missing, "is required with the target Staging");
                return false;
            }
        }
        else
        {
            var missing = new[] { ("KeycloakAdminUrl", keycloakAdminUrl?.ToString()), ("AdminUser", adminUser), ("AdminPassword", adminPassword) }.FirstOrDefault(each => each.Item2 is null).Item1;
            if (missing is not null)
            {
                refusal = Refused(missing, "is required with the target Local");
                return false;
            }
        }

        var pollTimeout = DefaultPollTimeout;
        var poll = Value(configuration, "PollTimeout");
        if (poll is not null && (!TimeSpan.TryParse(poll, CultureInfo.InvariantCulture, out pollTimeout) || pollTimeout <= TimeSpan.Zero))
        {
            refusal = Refused("PollTimeout", "must be a positive time span, such as 00:10:00");
            return false;
        }

        settings = new SeederSettings(staging, edgeUrl, webOrigin, mailpitUrl, keycloakAdminUrl, adminUser, adminPassword, clientId, clientSecret, userPassword, pollTimeout);
        refusal = null;
        return true;
    }

    private static string? Value(IConfiguration configuration, string key)
    {
        var value = configuration[$"{Section}:{key}"];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool TryUri(IConfiguration configuration, string key, [NotNullWhen(true)] out Uri? uri, [NotNullWhen(false)] out string? refusal)
    {
        if (Uri.TryCreate(Value(configuration, key), UriKind.Absolute, out uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            refusal = null;
            return true;
        }

        uri = null;
        refusal = Refused(key, "must be an absolute http or https URL");
        return false;
    }

    private static string Refused(string key, string reason) => $"Demo data: the settings were refused: {Section}:{key} {reason}.";
}
