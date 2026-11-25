using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>
/// Ensures the demo users through Keycloak's admin API: created if missing, enabled with a verified address and their
/// names, holding their realm role. Locally as the master realm's administrator (admin-cli), with a user's password their
/// username; on staging as the demo-seeder client, resetting each user's password to the shared secret on every run.
/// </summary>
internal sealed class KeycloakUsers(HttpClient http, SeederSettings settings, Uri authority)
{
    private string? token;

    private Uri AdminBase => settings.KeycloakAdminUrl;

    private string Realm => authority.AbsolutePath.TrimEnd('/').Split('/')[^1];

    /// <summary>Ensures one user; answers whether they were created.</summary>
    public async Task<bool> EnsureAsync(DemoUser user, CancellationToken cancellationToken)
    {
        var password = settings.PasswordOf(user.Username);
        var found = await SendAsync(HttpMethod.Get, $"/users?username={Uri.EscapeDataString(user.Username)}&exact=true", null, cancellationToken);
        var existing = (found.Body as JsonArray)?.OfType<JsonNode>().FirstOrDefault(each => Json.Text(each, "username") == user.Username);
        string id;
        var created = existing is null;
        if (existing is null)
        {
            var representation = Representation(user);
            representation["username"] = user.Username;
            representation["credentials"] = new JsonArray(Credential(password));
            var answer = await SendAsync(HttpMethod.Post, "/users", representation, cancellationToken);
            id = answer.Location?.AbsolutePath.Split('/')[^1] ?? throw new SeedingException($"Creating {user.Username} answered without its location.");
            // Keycloak gives a new user the realm's default required actions (the development realm's VERIFY_EMAIL among
            // them), whatever the representation says; they are cleared below, or the login form would never redirect.
            existing = (await SendAsync(HttpMethod.Get, $"/users/{id}", null, cancellationToken)).Body;
        }
        else
        {
            id = Json.Required(existing, "id", $"Looking up {user.Username}");
            if (settings.Staging)
            {
                await SendAsync(HttpMethod.Put, $"/users/{id}/reset-password", Credential(password), cancellationToken);
            }
        }

        if (Json.Text(existing, "email") != user.Email || Json.Text(existing, "firstName") != user.FirstName || Json.Text(existing, "lastName") != user.LastName
            || !Json.Bool(existing, "enabled") || !Json.Bool(existing, "emailVerified") || Json.Items(existing, "requiredActions").Count > 0)
        {
            await SendAsync(HttpMethod.Put, $"/users/{id}", Representation(user), cancellationToken);
        }

        if (user.RealmRole is { } role)
        {
            var held = await SendAsync(HttpMethod.Get, $"/users/{id}/role-mappings/realm", null, cancellationToken);
            if (!((held.Body as JsonArray)?.OfType<JsonNode>().Any(each => Json.Text(each, "name") == role) ?? false))
            {
                // Read from the roles the user may still be given, which manage-users answers alone; reading the role by
                // name would ask view-realm too, which staging's demo-seeder client does not hold (as X1's x1Users.ts).
                var available = await SendAsync(HttpMethod.Get, $"/users/{id}/role-mappings/realm/available", null, cancellationToken);
                var definition = (available.Body as JsonArray)?.OfType<JsonNode>().FirstOrDefault(each => Json.Text(each, "name") == role)
                    ?? throw new SeedingException($"The realm role {role} cannot be given to {user.Username}.");
                await SendAsync(HttpMethod.Post, $"/users/{id}/role-mappings/realm", new JsonArray(definition.DeepClone()), cancellationToken);
            }
        }

        return created;
    }

    private static JsonObject Representation(DemoUser user) => new()
    {
        ["email"] = user.Email,
        ["firstName"] = user.FirstName,
        ["lastName"] = user.LastName,
        ["enabled"] = true,
        ["emailVerified"] = true,
        ["requiredActions"] = new JsonArray(),
    };

    private static JsonObject Credential(string password) => new() { ["type"] = "password", ["value"] = password, ["temporary"] = false };

    private async Task<(JsonNode? Body, Uri? Location)> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            token ??= await TokenAsync(cancellationToken);
            using var request = new HttpRequestMessage(method, new Uri($"{AdminBase.ToString().TrimEnd('/')}/admin/realms/{Realm}{path}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null)
            {
                request.Content = Json.Content(body);
            }

            using var response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                // The administrator's token lives a minute; a 401 ran nothing, so the call is sent again with a new one.
                token = null;
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new SeedingException($"{method} the admin API's {path.Split('?')[0]} answered {(int)response.StatusCode}.");
            }

            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return (string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text), response.Headers.Location);
        }
    }

    private async Task<string> TokenAsync(CancellationToken cancellationToken)
    {
        Uri endpoint;
        List<KeyValuePair<string, string>> form;
        if (settings.Staging)
        {
            endpoint = new Uri($"{authority.ToString().TrimEnd('/')}/protocol/openid-connect/token");
            form = [new("grant_type", "client_credentials"), new("client_id", settings.ClientId ?? string.Empty), new("client_secret", settings.ClientSecret ?? string.Empty)];
        }
        else
        {
            endpoint = new Uri($"{AdminBase.ToString().TrimEnd('/')}/realms/master/protocol/openid-connect/token");
            form = [new("grant_type", "password"), new("client_id", "admin-cli"), new("username", settings.AdminUser ?? string.Empty), new("password", settings.AdminPassword ?? string.Empty)];
        }

        using var content = new FormUrlEncodedContent(form);
        using var response = await http.PostAsync(endpoint, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new SeedingException($"The administrator's sign-in at Keycloak answered {(int)response.StatusCode}.");
        }

        return Json.Text(JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken)), "access_token")
            ?? throw new SeedingException("The administrator's sign-in at Keycloak answered without an access token.");
    }
}
