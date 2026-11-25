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
/// the client credentials grant for each call, then reads or deletes <c>/admin/realms/{realm}/users/{id}</c>. Erasures are
/// rare, so no token is kept. The token request is a POST and gets one attempt (owner decision A on #115): a transient
/// failure throws, and Wolverine retries the consumer, which is idempotent; the read and the delete are retried by the
/// host's pipeline. Any answer but success or "not found" throws <see cref="AccountsUnavailableException"/>, naming the
/// step and the status and never the secret, a token, an address or Keycloak's answer.
/// </summary>
public sealed class KeycloakAccounts(HttpClient http, IOptions<KeycloakAccountsSettings> settings) : IAccounts
{
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
        return Option<Account>.Some(new Account(VerifiedAddress(user.RootElement), IsServiceAccount(user.RootElement)));
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

    private static bool IsServiceAccount(JsonElement user) =>
        user.TryGetProperty("serviceAccountClientId", out var client)
        && client.ValueKind == JsonValueKind.String
        && !string.IsNullOrEmpty(client.GetString());

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
