using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// #115 slice 6: the realm's confidential client <c>pharma-training-erasure</c>, whose secret the AppHost generates as
/// <c>keycloak-erasure-client-secret</c> and Keycloak's import reads from its environment, mints a token with the client
/// credentials grant alone, and that token may read and delete a user of the realm through the admin API, as Organisation
/// does when it erases a person.
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

    private sealed record ClientToken([property: JsonPropertyName("access_token")] string AccessToken);

    private sealed record RealmUser(string Id, string? Email, bool EmailVerified);
}
