using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// #115 slice 6: the realm's confidential client <c>pharma-training-erasure</c>, whose secret the AppHost generates as
/// <c>keycloak-erasure-client-secret</c> and Keycloak's import reads from its environment, mints a token with the client
/// credentials grant alone, and that token may read and delete a user of the realm through the admin API, as Organisation
/// does when it erases a person. Organisation's own reader, <see cref="KeycloakAccounts"/>, recognises the client's
/// service account as one, whatever shape Keycloak's admin API answers it in, so an erasure never deletes it.
/// </summary>
public class KeycloakErasureClientTests(AspireFixture aspire)
{
    private const string ClientId = "pharma-training-erasure";

    private const string SecretParameter = "keycloak-erasure-client-secret";

    private static readonly Uri TokenEndpoint = new($"/realms/{KeycloakAdmin.Realm}/protocol/openid-connect/token", UriKind.Relative);

    private readonly KeycloakAdmin keycloak = new(aspire);

    [Fact]
    public async Task The_erasure_client_mints_a_token_with_its_secret()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var identity = keycloak.Identity();

        using var response = await MintAsync(identity, await keycloak.ParameterAsync(SecretParameter, cancellationToken), cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        using var token = JsonDocument.Parse(body);
        token.RootElement.GetProperty("access_token").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task It_refuses_a_wrong_secret()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var identity = keycloak.Identity();

        using var response = await MintAsync(identity, "not-the-secret", cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Its_token_may_read_and_delete_a_user()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var address = $"erasure-client-{Guid.CreateVersion7():N}@example.com";
        var user = await keycloak.CreateUserAsync(address, emailVerified: true, cancellationToken);
        using var identity = keycloak.Identity();
        identity.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await ClientTokenAsync(identity, cancellationToken));
        var path = new Uri($"/admin/realms/{KeycloakAdmin.Realm}/users/{user:D}", UriKind.Relative);

        using var read = await identity.GetAsync(path, cancellationToken);
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync(cancellationToken));
        var account = await read.Content.ReadFromJsonAsync<RealmUser>(cancellationToken);
        account.ShouldNotBeNull().Email.ShouldBe(address);
        account.EmailVerified.ShouldBeTrue();

        using var deleted = await identity.DeleteAsync(path, cancellationToken);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync(cancellationToken));

        using var gone = await identity.GetAsync(path, cancellationToken);
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The token's subject is the client's service account; read through Organisation's own <see cref="KeycloakAccounts"/>
    /// as an erasure reads a person, it is marked a service account, so the guard that keeps it from being deleted holds
    /// against the admin API itself, not only against a hand-written answer.
    /// </summary>
    [Fact]
    public async Task Its_own_service_account_is_recognised_as_one()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var identity = keycloak.Identity();
        var subject = Subject(await ClientTokenAsync(identity, cancellationToken));
        using var http = keycloak.Identity();
        var accounts = await AccountsAsync(http, cancellationToken);

        var found = await accounts.FindAsync(PersonId.From(subject), cancellationToken);

        found.HasValue.ShouldBeTrue("the token's subject is a user of the realm, the client's service account");
        found.Value.IsServiceAccount.ShouldBeTrue("an erasure naming the client's service account must not delete it");
        (await keycloak.UserStatusAsync(subject, cancellationToken)).ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// A delegated realm administrator holds a <c>realm-management</c> client role and no staff realm role. Read through
    /// Organisation's own <see cref="KeycloakAccounts"/> with the erasure client's own permissions, which must let it find
    /// that client and read the user's roles of it, the account is marked as staff, so an erasure never deletes it; a
    /// user without such a role is not.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_delegated_realm_administrator_is_marked_as_staff(bool delegated)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await keycloak.CreateUserAsync($"erasure-delegated-{Guid.CreateVersion7():N}@example.com", emailVerified: true, cancellationToken);
        try
        {
            if (delegated)
            {
                await keycloak.GrantClientRoleAsync(user, "realm-management", "view-users", cancellationToken);
            }

            using var http = keycloak.Identity();
            var accounts = await AccountsAsync(http, cancellationToken);

            var found = await accounts.FindAsync(PersonId.From(user), cancellationToken);

            found.HasValue.ShouldBeTrue("the user exists in the realm");
            found.Value.HoldsStaffRole.ShouldBe(delegated, "a realm-management client role, and only one, marks a delegated realm administrator");
            found.Value.IsServiceAccount.ShouldBeFalse();
        }
        finally
        {
            await keycloak.DeleteUserAsync(user, cancellationToken);
        }
    }

    [Fact]
    public async Task It_refuses_the_password_grant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var identity = keycloak.Identity();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = ClientId,
            ["client_secret"] = await keycloak.ParameterAsync(SecretParameter, cancellationToken),
            ["username"] = "developer",
            ["password"] = "developer",
        });

        using var response = await identity.PostAsync(TokenEndpoint, form, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldNotBe(HttpStatusCode.OK, body);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("error").GetString().ShouldBeOneOf("unauthorized_client", "invalid_client");
    }

    /// <summary>Organisation's own reader over Keycloak's admin API, as the erasure client, at Keycloak's internal address.</summary>
    private async Task<KeycloakAccounts> AccountsAsync(HttpClient http, CancellationToken cancellationToken) =>
        new(
            http,
            Options.Create(new KeycloakAccountsSettings
            {
                Address = http.BaseAddress.ShouldNotBeNull().AbsoluteUri,
                Realm = KeycloakAdmin.Realm,
                ClientId = ClientId,
                ClientSecret = await keycloak.ParameterAsync(SecretParameter, cancellationToken),
            }));

    private static async Task<HttpResponseMessage> MintAsync(HttpClient identity, string secret, CancellationToken cancellationToken)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = ClientId,
            ["client_secret"] = secret,
        });
        return await identity.PostAsync(TokenEndpoint, form, cancellationToken);
    }

    private async Task<string> ClientTokenAsync(HttpClient identity, CancellationToken cancellationToken)
    {
        using var response = await MintAsync(identity, await keycloak.ParameterAsync(SecretParameter, cancellationToken), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));
        var token = await response.Content.ReadFromJsonAsync<ClientToken>(cancellationToken);
        return token.ShouldNotBeNull().AccessToken;
    }

    /// <summary>The access token's <c>sub</c>, read from its payload; the signature is Keycloak's own and not checked here.</summary>
    private static Guid Subject(string token)
    {
        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        using var claims = JsonDocument.Parse(Convert.FromBase64String(payload));
        return Guid.Parse(claims.RootElement.GetProperty("sub").GetString().ShouldNotBeNull("the client's token names no subject"));
    }

    private sealed record ClientToken([property: JsonPropertyName("access_token")] string AccessToken);

    private sealed record RealmUser(string Id, string? Email, bool EmailVerified);
}
