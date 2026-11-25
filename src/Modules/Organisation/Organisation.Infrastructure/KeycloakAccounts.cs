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
/// reads the user's effective realm roles (<c>/role-mappings/realm/composite</c>) with the same token, so a staff account
/// is marked (<see cref="StaffRoles"/>). A client's service account is marked by <c>serviceAccountClientId</c> or by the
/// username Keycloak gives every one, <c>service-account-{clientId}</c>, since the admin API may leave the field out.
/// Erasures are rare, so no token is kept. The token request is a POST and gets one attempt (owner decision A on #115): a transient
/// failure throws, and Wolverine retries the consumer, which is idempotent; the read and the delete are retried by the
/// host's pipeline. Any answer but success or "not found" throws <see cref="AccountsUnavailableException"/>, naming the
/// step and the status and never the secret, a token, an address or Keycloak's answer.
/// </summary>
public sealed class KeycloakAccounts(HttpClient http, IOptions<KeycloakAccountsSettings> settings) : IAccounts
{
    private const string ServiceAccountPrefix = "service-account-";

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
    /// Whether the user's effective realm roles, composites resolved, include a staff role; none when the user is gone
    /// by now. An answer of another shape throws: a guard that cannot read the roles must not let the account be deleted.
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
        if (roles.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw Unreadable();
        }

        var staff = false;
        foreach (var role in roles.RootElement.EnumerateArray())
        {
            if (role.ValueKind != JsonValueKind.Object
                || !role.TryGetProperty("name", out var name)
                || name.ValueKind != JsonValueKind.String
                || name.GetString() is not { } value)
            {
                throw Unreadable();
            }

            staff |= StaffRoles.Includes(value);
        }

        return Option<bool>.Some(staff);
    }

    private static AccountsUnavailableException Unreadable() =>
        new("Keycloak answered the account roles read in a shape Organisation does not read.");

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
