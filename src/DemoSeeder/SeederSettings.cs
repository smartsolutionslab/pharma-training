using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>
/// The <c>DemoSeeder:*</c> settings, read and guarded before any call: the target is Local or Staging and nothing else.
/// Local seeds a loopback edge with the master realm's administrator, and every address that receives a credential
/// (Keycloak's admin endpoint, Mailpit) is loopback too. Staging seeds an https edge with a client of its own and the users'
/// password, never a password-based administrator, at an https Keycloak and an https Mailpit behind its basic auth. Once
/// the edge's <c>/api/config.json</c> is read, <see cref="TryAccept"/> checks what it reports before any credential is sent:
/// with the target Staging the deployment must be staging, and the realm users sign in at must be https on Keycloak's
/// host; with Local, loopback. A refusal names the key, never its value.
/// </summary>
internal sealed record SeederSettings(
    bool Staging,
    Uri EdgeUrl,
    Uri WebOrigin,
    Uri MailpitUrl,
    string? MailpitAuth,
    Uri KeycloakAdminUrl,
    string? AdminUser,
    string? AdminPassword,
    string? ClientId,
    string? ClientSecret,
    string? UserPassword,
    TimeSpan PollTimeout)
{
    private const string Section = "DemoSeeder";

    /// <summary>What staging's edge reports in <c>/api/config.json</c>'s <c>environment</c> (the AppHost's <c>aspire publish -e</c>).</summary>
    private const string StagingEnvironment = "staging";

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

        if (!TryUri(configuration, "KeycloakAdminUrl", out var keycloakAdminUrl, out refusal))
        {
            return false;
        }

        // Every address the run sends a credential to: the edge (users' tokens), Keycloak (the administrator's password
        // or the client's secret) and Mailpit (its basic auth on staging).
        var destinations = new[] { ("EdgeUrl", edgeUrl), ("KeycloakAdminUrl", keycloakAdminUrl), ("MailpitUrl", mailpitUrl) };
        foreach (var (key, uri) in destinations)
        {
            if (!staging && !uri.IsLoopback)
            {
                refusal = Refused(key, "must name a loopback host with the target Local");
                return false;
            }

            if (staging && uri.Scheme != Uri.UriSchemeHttps)
            {
                refusal = Refused(key, "must be https with the target Staging");
                return false;
            }
        }

        var adminUser = Value(configuration, "AdminUser");
        var adminPassword = Value(configuration, "AdminPassword");
        var clientId = Value(configuration, "ClientId");
        var clientSecret = Value(configuration, "ClientSecret");
        var userPassword = Value(configuration, "UserPassword");
        var mailpitAuth = Value(configuration, "MailpitAuth");
        if (staging)
        {
            if (adminUser is not null || adminPassword is not null)
            {
                refusal = Refused(adminPassword is not null ? "AdminPassword" : "AdminUser", "is not accepted with the target Staging, which administers users with its client alone");
                return false;
            }

            var missing = new[] { ("ClientId", clientId), ("ClientSecret", clientSecret), ("UserPassword", userPassword), ("MailpitAuth", mailpitAuth) }.FirstOrDefault(each => each.Item2 is null).Item1;
            if (missing is not null)
            {
                refusal = Refused(missing, "is required with the target Staging");
                return false;
            }
        }
        else
        {
            var missing = new[] { ("AdminUser", adminUser), ("AdminPassword", adminPassword) }.FirstOrDefault(each => each.Item2 is null).Item1;
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

        settings = new SeederSettings(staging, edgeUrl, webOrigin, mailpitUrl, mailpitAuth, keycloakAdminUrl, adminUser, adminPassword, clientId, clientSecret, userPassword, pollTimeout);
        refusal = null;
        return true;
    }

    /// <summary>
    /// Checks what the edge's <c>/api/config.json</c> reports before any credential is sent: with the target Staging, the
    /// deployment staging (production's edge is https too) and a realm that is https on Keycloak's host; with Local, a
    /// loopback realm. Answers the realm users sign in at and its web client.
    /// </summary>
    public bool TryAccept(JsonNode configuration, [NotNullWhen(true)] out EdgeRealm? realm, [NotNullWhen(false)] out string? refusal)
    {
        realm = null;
        if (Staging && Json.Text(configuration, "environment") != StagingEnvironment)
        {
            refusal = Reported("environment", $"must be {StagingEnvironment} with the target Staging; the seeder seeds no other deployment");
            return false;
        }

        if (!Uri.TryCreate(Json.Text(configuration, "oidcAuthority"), UriKind.Absolute, out var authority))
        {
            refusal = Reported("oidcAuthority", "must be an absolute URL");
            return false;
        }

        if (!Staging && !authority.IsLoopback)
        {
            refusal = Reported("oidcAuthority", "must name a loopback host with the target Local");
            return false;
        }

        if (Staging && (authority.Scheme != Uri.UriSchemeHttps || !string.Equals(authority.Host, KeycloakAdminUrl.Host, StringComparison.OrdinalIgnoreCase)))
        {
            refusal = Reported("oidcAuthority", $"must be https on the host of {Section}:KeycloakAdminUrl with the target Staging");
            return false;
        }

        if (Json.Text(configuration, "clientId") is not { } clientId)
        {
            refusal = Reported("clientId", "is missing");
            return false;
        }

        realm = new EdgeRealm(authority, clientId);
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

    private static string Reported(string field, string reason) => $"Demo data: the edge was refused: /api/config.json's {field} {reason}.";
}
