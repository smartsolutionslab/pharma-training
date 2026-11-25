using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure;

/// <summary>
/// The people's accounts over Keycloak's admin API, as the client <c>pharma-training-erasure</c>: it mints a token with
/// the client credentials grant for each call, then reads or deletes <c>/admin/realms/{realm}/users/{id}</c>; a read also
/// reads the user's effective realm roles (<c>/role-mappings/realm/composite</c>) and their effective roles of the
/// <c>realm-management</c> client (<c>/role-mappings/clients/{id}/composite</c>, the id read from the client's own service
/// account) with the same token, so a staff account (<see cref="StaffRoles"/>) or a delegated realm administrator is
/// marked. A client's service account is marked by <c>serviceAccountClientId</c> or by the username Keycloak gives every
/// one, <c>service-account-{clientId}</c>, since the admin API may leave the field out.
/// Erasures are rare, so no token is kept. The token request is a POST and gets one attempt (owner decision A on #115): a transient
/// failure throws, and Wolverine retries the consumer, which is idempotent; the read and the delete are retried by the
/// host's pipeline. Any answer but success or "not found" throws <see cref="AccountsUnavailableException"/>, naming the
/// step and the status and never the secret, a token, an address or Keycloak's answer.
/// </summary>
public sealed class KeycloakAccounts(HttpClient http, IOptions<KeycloakAccountsSettings> settings) : IAccounts
{
    private const string ServiceAccountPrefix = "service-account-";
    private const string RealmManagementClientId = "realm-management";

    private readonly HttpClient http = http;
    private readonly IOptions<KeycloakAccountsSettings> settings = settings;

    public async Task<Option<Account>> FindAsync(PersonId person, CancellationToken cancellationToken)
    {
        Ensure.That(person).IsNotNull();

        var token = await MintTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, UserUri(person));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Option<Account>.None;
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw Unavailable("read", response.StatusCode);
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var user = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        var staff = await HoldsStaffRoleAsync(person, token, cancellationToken);
        if (!staff.HasValue)
        {
            return Option<Account>.None;
        }

        return Option<Account>.Some(new Account(VerifiedAddress(user.RootElement), IsServiceAccount(user.RootElement), staff.Value));
    }

    public async Task DeleteAsync(PersonId person, CancellationToken cancellationToken)
    {
        Ensure.That(person).IsNotNull();

        var token = await MintTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Delete, UserUri(person));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode is not (HttpStatusCode.NoContent or HttpStatusCode.OK or HttpStatusCode.NotFound))
        {
            throw Unavailable("delete", response.StatusCode);
        }
    }

    /// <summary>Only an address Keycloak verified, and a valid one, is read (docs/adr/0014); anything else is none.</summary>
    private static Option<EmailAddress> VerifiedAddress(JsonElement user)
    {
        var verified = user.TryGetProperty("emailVerified", out var flag) && flag.ValueKind == JsonValueKind.True;
        if (verified
            && user.TryGetProperty("email", out var email)
            && email.ValueKind == JsonValueKind.String
            && EmailAddress.TryFrom(email.GetString(), out var address))
        {
            return Option<EmailAddress>.Some(address);
        }

        return Option<EmailAddress>.None;
    }

    /// <summary>
    /// A client's service account, by either shape Keycloak answers: <c>serviceAccountClientId</c> set (an export fills
    /// it; the admin API may not), or the username Keycloak gives every service account. A person who chose such a
    /// username is kept too, which leaves their erasure unfinished for an administrator, never an account deleted.
    /// </summary>
    private static bool IsServiceAccount(JsonElement user) =>
        (user.TryGetProperty("serviceAccountClientId", out var client)
            && client.ValueKind == JsonValueKind.String
            && !string.IsNullOrEmpty(client.GetString()))
        || (user.TryGetProperty("username", out var username)
            && username.ValueKind == JsonValueKind.String
            && username.GetString() is { } name
            && name.StartsWith(ServiceAccountPrefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether the user's effective realm roles, composites resolved, include a staff role, or the user holds a role of the
    /// <c>realm-management</c> client; none when the user is gone by now. An answer of another shape throws: a guard that
    /// cannot read the roles must not let the account be deleted.
    /// </summary>
    private async Task<Option<bool>> HoldsStaffRoleAsync(PersonId person, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{UserUri(person).AbsoluteUri}/role-mappings/realm/composite"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Option<bool>.None;
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw Unavailable("roles", response.StatusCode);
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var roles = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        var staff = false;
        foreach (var name in RoleNames(roles.RootElement, "roles"))
        {
            staff |= StaffRoles.Includes(name);
        }

        var admin = await HoldsRealmManagementRoleAsync(person, token, cancellationToken);
        if (!admin.HasValue)
        {
            return Option<bool>.None;
        }

        return Option<bool>.Some(staff || admin.Value);
    }

    /// <summary>
    /// Whether the user holds any role of the <c>realm-management</c> client, composites resolved: a delegated realm
    /// administrator, who is staff though they may hold no staff realm role. None when the user is gone by now. It fails
    /// closed like the realm read: any other status, or an answer of another shape, throws.
    /// </summary>
    private async Task<Option<bool>> HoldsRealmManagementRoleAsync(PersonId person, string token, CancellationToken cancellationToken)
    {
        var client = await RealmManagementClientAsync(token, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{UserUri(person).AbsoluteUri}/role-mappings/clients/{Uri.EscapeDataString(client)}/composite"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Option<bool>.None;
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw Unavailable("client roles", response.StatusCode);
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var roles = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        return Option<bool>.Some(RoleNames(roles.RootElement, "client roles").Count > 0);
    }

    /// <summary>
    /// The internal id of the realm's <c>realm-management</c> client, read from the erasure client's own service account
    /// (<c>service-account-{clientId}</c>), which holds that client's <c>manage-users</c>: its role mappings name the client
    /// and its id. A holder of <c>manage-users</c> may read them, where listing clients would need <c>view-clients</c>
    /// (Keycloak answers a holder of <c>query-clients</c> alone with an empty list). Exactly one service account must
    /// answer and its mappings must name <c>realm-management</c>; anything else throws, so the guard fails closed.
    /// </summary>
    private async Task<string> RealmManagementClientAsync(string token, CancellationToken cancellationToken)
    {
        var current = settings.Value;
        var username = ServiceAccountPrefix + current.ClientId.ToLowerInvariant();
        var realm = $"{Address(current)}/admin/realms/{Uri.EscapeDataString(current.Realm)}";
        using var lookup = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"{realm}/users?username={Uri.EscapeDataString(username)}&exact=true"));
        lookup.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var found = await http.SendAsync(lookup, cancellationToken);
        if (found.StatusCode != HttpStatusCode.OK)
        {
            throw Unavailable("service account lookup", found.StatusCode);
        }

        await using var users = await found.Content.ReadAsStreamAsync(cancellationToken);
        using var matches = await JsonDocument.ParseAsync(users, cancellationToken: cancellationToken);
        var serviceAccount = ServiceAccountId(matches.RootElement, username);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{realm}/users/{Uri.EscapeDataString(serviceAccount)}/role-mappings"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw Unavailable("service account mappings", response.StatusCode);
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var mappings = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        if (mappings.RootElement.ValueKind == JsonValueKind.Object
            && mappings.RootElement.TryGetProperty("clientMappings", out var clients)
            && clients.ValueKind == JsonValueKind.Object
            && clients.TryGetProperty(RealmManagementClientId, out var client)
            && client.ValueKind == JsonValueKind.Object
            && client.TryGetProperty("id", out var id)
            && id.ValueKind == JsonValueKind.String
            && id.GetString() is { Length: > 0 } value)
        {
            return value;
        }

        throw Unreadable("service account mappings");
    }

    /// <summary>The id of the one user named exactly <paramref name="username"/> in a user search's answer; anything else throws.</summary>
    private static string ServiceAccountId(JsonElement users, string username)
    {
        if (users.ValueKind != JsonValueKind.Array)
        {
            throw Unreadable("service account lookup");
        }

        List<string> ids = [];
        foreach (var user in users.EnumerateArray())
        {
            if (user.ValueKind == JsonValueKind.Object
                && user.TryGetProperty("username", out var name)
                && name.ValueKind == JsonValueKind.String
                && name.GetString() == username)
            {
                if (!user.TryGetProperty("id", out var id)
                    || id.ValueKind != JsonValueKind.String
                    || id.GetString() is not { Length: > 0 } value)
                {
                    throw Unreadable("service account lookup");
                }

                ids.Add(value);
            }
        }

        if (ids.Count != 1)
        {
            throw Unreadable("service account lookup");
        }

        return ids[0];
    }

    /// <summary>The names of an array of role representations; any other shape throws, naming the step.</summary>
    private static List<string> RoleNames(JsonElement roles, string step)
    {
        if (roles.ValueKind != JsonValueKind.Array)
        {
            throw Unreadable(step);
        }

        List<string> names = [];
        foreach (var role in roles.EnumerateArray())
        {
            if (role.ValueKind != JsonValueKind.Object
                || !role.TryGetProperty("name", out var name)
                || name.ValueKind != JsonValueKind.String
                || name.GetString() is not { } value)
            {
                throw Unreadable(step);
            }

            names.Add(value);
        }

        return names;
    }

    private static AccountsUnavailableException Unreadable(string step) =>
        new($"Keycloak answered the account {step} read in a shape Organisation does not read.");

    private static AccountsUnavailableException Unavailable(string step, HttpStatusCode status) =>
        new($"Keycloak answered the account {step} with {((int)status).ToString(CultureInfo.InvariantCulture)} ({status}).");

    private async Task<string> MintTokenAsync(CancellationToken cancellationToken)
    {
        var current = settings.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Address(current)}/realms/{Uri.EscapeDataString(current.Realm)}/protocol/openid-connect/token")
        {
            Content = new FormUrlEncodedContent(
            [
                new("grant_type", "client_credentials"),
                new("client_id", current.ClientId),
                new("client_secret", current.ClientSecret),
            ]),
        };
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw Unavailable("token", response.StatusCode);
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var answer = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        if (answer.RootElement.ValueKind == JsonValueKind.Object
            && answer.RootElement.TryGetProperty("access_token", out var token)
            && token.ValueKind == JsonValueKind.String
            && token.GetString() is { Length: > 0 } value)
        {
            return value;
        }

        throw new AccountsUnavailableException("Keycloak answered the account token request without an access token.");
    }

    private Uri UserUri(PersonId person)
    {
        var current = settings.Value;
        return new Uri($"{Address(current)}/admin/realms/{Uri.EscapeDataString(current.Realm)}/users/{person.Value.ToString("D", CultureInfo.InvariantCulture)}");
    }

    private static string Address(KeycloakAccountsSettings current) => current.Address.TrimEnd('/');
}
