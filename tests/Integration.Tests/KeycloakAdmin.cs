using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Keycloak's admin API, as the master realm's administrator ("admin", with the password the AppHost generated for it),
/// at Keycloak's own endpoint: a test's throwaway users in the realm <c>pharma-training</c>, and the values of the
/// AppHost's generated parameters. The fixture removes every volume, so each run imports the realm fresh.
/// </summary>
internal sealed class KeycloakAdmin(AspireFixture aspire)
{
    public const string Realm = "pharma-training";

    /// <summary>A client of Keycloak's own endpoint; the caller disposes it.</summary>
    public HttpClient Identity() => new() { BaseAddress = aspire.App.GetEndpoint("keycloak", "http") };

    /// <summary>The value the AppHost gave the parameter, generated or configured.</summary>
    public async Task<string> ParameterAsync(string name, CancellationToken cancellationToken)
    {
        var parameter = aspire.App.Services.GetRequiredService<DistributedApplicationModel>().Resources.OfType<ParameterResource>().SingleOrDefault(candidate => candidate.Name == name);
        parameter.ShouldNotBeNull($"The AppHost has no parameter {name}.");
        return await parameter.GetValueAsync(cancellationToken) ?? string.Empty;
    }

    /// <summary>A token of the master realm's administrator.</summary>
    public async Task<string> AdminTokenAsync(HttpClient identity, CancellationToken cancellationToken)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "admin-cli",
            ["username"] = "admin",
            ["password"] = await ParameterAsync("keycloak-password", cancellationToken),
        });
        using var response = await identity.PostAsync(new Uri("/realms/master/protocol/openid-connect/token", UriKind.Relative), form, cancellationToken);
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<AccessToken>(cancellationToken);
        return token?.Token ?? throw new InvalidOperationException("The master realm returned no token.");
    }

    /// <summary>A client of Keycloak's own endpoint that calls as the administrator; the caller disposes it.</summary>
    public async Task<HttpClient> AdministratorAsync(CancellationToken cancellationToken)
    {
        var identity = Identity();
        identity.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(identity, cancellationToken));
        return identity;
    }

    /// <summary>
    /// Creates a throwaway user in the realm, named by the address, without credentials, and answers its id, which Keycloak
    /// names in the answer's <c>Location</c>: the user's subject, and so its PersonId.
    /// </summary>
    public async Task<Guid> CreateUserAsync(string email, bool emailVerified, CancellationToken cancellationToken)
    {
        using var administrator = await AdministratorAsync(cancellationToken);
        using var created = await administrator.PostAsJsonAsync(
            new Uri($"/admin/realms/{Realm}/users", UriKind.Relative),
            new { username = email, email, emailVerified, enabled = true, firstName = "Erasure", lastName = "Probe" },
            cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(cancellationToken));
        var location = created.Headers.Location.ShouldNotBeNull("Keycloak named no location for the user it created.");
        return Guid.Parse(location.Segments[^1]);
    }

    /// <summary>The status the admin API answers for the user: 200 while it exists, 404 once it is deleted.</summary>
    public async Task<HttpStatusCode> UserStatusAsync(Guid user, CancellationToken cancellationToken)
    {
        using var administrator = await AdministratorAsync(cancellationToken);
        using var response = await administrator.GetAsync(new Uri($"/admin/realms/{Realm}/users/{user:D}", UriKind.Relative), cancellationToken);
        return response.StatusCode;
    }

    /// <summary>Gives the user the realm role, as an administrator does with kcadm.sh's <c>add-roles</c>.</summary>
    public async Task GrantRealmRoleAsync(Guid user, string role, CancellationToken cancellationToken)
    {
        using var administrator = await AdministratorAsync(cancellationToken);
        using var read = await administrator.GetAsync(new Uri($"/admin/realms/{Realm}/roles/{Uri.EscapeDataString(role)}", UriKind.Relative), cancellationToken);
        var body = await read.Content.ReadAsStringAsync(cancellationToken);
        read.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        using var granted = await administrator.PostAsync(
            new Uri($"/admin/realms/{Realm}/users/{user:D}/role-mappings/realm", UriKind.Relative),
            new StringContent($"[{body}]", System.Text.Encoding.UTF8, "application/json"),
            cancellationToken);
        granted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await granted.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>
    /// Gives the user a role of the client, as an administrator does with kcadm.sh's <c>add-roles --cclientid</c>; a
    /// <c>realm-management</c> role makes the user a delegated realm administrator.
    /// </summary>
    public async Task GrantClientRoleAsync(Guid user, string clientId, string role, CancellationToken cancellationToken)
    {
        using var administrator = await AdministratorAsync(cancellationToken);
        var clients = await administrator.GetFromJsonAsync<List<RealmClient>>(
            new Uri($"/admin/realms/{Realm}/clients?clientId={Uri.EscapeDataString(clientId)}", UriKind.Relative),
            cancellationToken);
        var client = clients.ShouldNotBeNull().ShouldHaveSingleItem($"the realm should hold one client {clientId}");
        using var read = await administrator.GetAsync(new Uri($"/admin/realms/{Realm}/clients/{client.Id}/roles/{Uri.EscapeDataString(role)}", UriKind.Relative), cancellationToken);
        var body = await read.Content.ReadAsStringAsync(cancellationToken);
        read.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        using var granted = await administrator.PostAsync(
            new Uri($"/admin/realms/{Realm}/users/{user:D}/role-mappings/clients/{client.Id}", UriKind.Relative),
            new StringContent($"[{body}]", System.Text.Encoding.UTF8, "application/json"),
            cancellationToken);
        granted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await granted.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>Deletes a test's throwaway user; one that is gone already is fine.</summary>
    public async Task DeleteUserAsync(Guid user, CancellationToken cancellationToken)
    {
        using var administrator = await AdministratorAsync(cancellationToken);
        using var deleted = await administrator.DeleteAsync(new Uri($"/admin/realms/{Realm}/users/{user:D}", UriKind.Relative), cancellationToken);
        deleted.StatusCode.ShouldBeOneOf(HttpStatusCode.NoContent, HttpStatusCode.NotFound);
    }

    private sealed record AccessToken([property: JsonPropertyName("access_token")] string Token);

    private sealed record RealmClient(string Id, string ClientId);
}
