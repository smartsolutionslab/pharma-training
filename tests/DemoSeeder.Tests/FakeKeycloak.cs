using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder.Tests;

/// <summary>
/// Keycloak as the seeder meets it: the realm pharma-training, whose web client signs users in with the code flow and
/// PKCE only (the published realm refuses the password grant on every client; the fake refuses it locally too, so the
/// seeder takes the one path both share), its login form behind a session cookie, the master realm's admin-cli for the
/// local administrator, the confidential client demo-seeder, which staging's run administers users with (its
/// <c>client_credentials</c> grant at the realm's token endpoint), and the admin API's user calls.
/// </summary>
public sealed class FakeKeycloak
{
    public const string Realm = "pharma-training";

    public const string ClientId = "pharma-training-web";

    public const string Administrator = "admin";

    public const string AdministratorPassword = "admin-password-of-the-run";

    /// <summary>The confidential client staging's run administers users with (owner decision 4 on #457).</summary>
    public const string SeederClientId = "demo-seeder";

    /// <summary>Its secret, as the staging workflow hands it to the run (DEMO_SEEDER_SECRET).</summary>
    public const string SeederClientSecret = StagingSettings.ClientSecret;

    /// <summary>The login page's title, which a failed sign-in names.</summary>
    public const string LoginTitle = "Sign in to Pharma Training";

    /// <summary>What the login page says about wrong credentials: content a failed sign-in never repeats.</summary>
    public const string InvalidCredentials = "Invalid username or password.";

    private const string AdminToken = "admin-token-of-the-run";

    private const string SeederClientToken = "demo-seeder-token-of-the-run";

    private const string TokenPrefix = "token-for-";

    private static readonly string[] RealmRoles = ["editor", "manager"];

    private readonly Dictionary<string, RealmUser> users = [with(StringComparer.Ordinal)];

    private readonly Dictionary<string, LoginSession> logins = [with(StringComparer.Ordinal)];

    private readonly Dictionary<string, IssuedCode> codes = [with(StringComparer.Ordinal)];

    private readonly List<IReadOnlyDictionary<string, string>> tokenRequests = [];

    /// <summary>The web client's redirect URI: the web app's origin with a slash, which the realm allows (http://localhost:5173/*).</summary>
    public static string RedirectUri => FakeApplication.WebOrigin.ToString();

    /// <summary>Staging's redirect URI: the edge's origin, which serves the web app there.</summary>
    public static string StagingRedirectUri => FakeApplication.StagingEdgeUrl.ToString();

    /// <summary>The forms the realm's token endpoint was posted, in order: what each grant sent.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string>> TokenRequests => tokenRequests;

    public static string RealmUrl => $"{FakeApplication.KeycloakUrl.ToString().TrimEnd('/')}/realms/{Realm}";

    /// <summary>The access token the fake issues a user: the edge reads the user back from it.</summary>
    public static string TokenFor(string username) => TokenPrefix + username;

    public IReadOnlyCollection<RealmUser> Users => users.Values;

    /// <summary>How many times the admin API created or changed a user.</summary>
    public int Changes { get; private set; }

    /// <summary>A user of the realm with a password, as the realm import or an earlier run made them.</summary>
    public RealmUser AddUser(string username, string password, string? realmRole = null)
    {
        var user = new RealmUser(Guid.CreateVersion7().ToString(), username) { Password = password, Email = $"{username}@example.com", Enabled = true, EmailVerified = true };
        if (realmRole is not null)
        {
            user.Roles.Add(realmRole);
        }

        users[username] = user;
        return user;
    }

    /// <summary>The user a bearer token names, or null for none or an unknown one.</summary>
    public string? UserOf(string? authorization)
    {
        if (authorization is null || !authorization.StartsWith("Bearer " + TokenPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var username = authorization[("Bearer " + TokenPrefix).Length..];
        return users.ContainsKey(username) ? username : null;
    }

    public bool HasRole(string username, string role) => users.TryGetValue(username, out var user) && user.Roles.Contains(role);

    public string NameOf(string username) => users.TryGetValue(username, out var user) ? $"{user.FirstName} {user.LastName}".Trim() : username;

    internal FakeAnswer Handle(HttpRequestMessage request, string body, string cookie)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        var query = FakeRoute.Pairs(request.RequestUri?.Query ?? string.Empty);
        var method = request.Method.Method;

        if (method == "GET" && path == $"/realms/{Realm}/protocol/openid-connect/auth")
        {
            return new FakeAnswer("auth", null, Authorize(query));
        }

        if (method == "POST" && path == $"/realms/{Realm}/login-actions/authenticate")
        {
            return Login(query, FakeRoute.Pairs(body), cookie);
        }

        if (method == "POST" && path == $"/realms/{Realm}/protocol/openid-connect/token")
        {
            return Token(FakeRoute.Pairs(body));
        }

        if (method == "POST" && path == "/realms/master/protocol/openid-connect/token")
        {
            return new FakeAnswer("master token", null, MasterToken(FakeRoute.Pairs(body)));
        }

        if (path.StartsWith($"/admin/realms/{Realm}/", StringComparison.Ordinal))
        {
            var authorization = request.Headers.Authorization?.ToString();
            if (authorization != "Bearer " + AdminToken && authorization != "Bearer " + SeederClientToken)
            {
                return new FakeAnswer("admin", null, new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }

            // demo-seeder's service account holds realm-management's manage-users and view-users alone: reading a realm
            // role by name asks view-realm too, which it does not hold.
            if (authorization == "Bearer " + SeederClientToken && path.StartsWith($"/admin/realms/{Realm}/roles/", StringComparison.Ordinal))
            {
                return new FakeAnswer("/roles/{name}", null, new HttpResponseMessage(HttpStatusCode.Forbidden));
            }

            return Admin(method, path[$"/admin/realms/{Realm}".Length..], query, body);
        }

        return new FakeAnswer(string.Empty, null, FakeResponses.Unscripted(request));
    }

    private static string Challenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string LoginPage(string sessionCode, string? error) =>
        $"""
        <!DOCTYPE html>
        <html lang="en">
        <head><meta charset="utf-8"><title>{LoginTitle}</title></head>
        <body>
        {(error is null ? string.Empty : $"<span id=\"input-error\" class=\"kc-feedback-text\">{error}</span>")}
        <form id="kc-form-login" onsubmit="login.disabled = true; return true;" action="{RealmUrl}/login-actions/authenticate?session_code={sessionCode}&amp;execution=b7c1&amp;client_id={ClientId}&amp;tab_id=Xq1" method="post">
        <input tabindex="1" id="username" name="username" value="" type="text" autofocus autocomplete="username" />
        <input tabindex="2" id="password" name="password" type="password" autocomplete="current-password" />
        <input type="hidden" id="id-hidden-input" name="credentialId" />
        <input tabindex="4" name="login" id="kc-login" type="submit" value="Sign In" />
        </form>
        </body>
        </html>
        """;

    private HttpResponseMessage Authorize(Dictionary<string, string> query)
    {
        var valid = query.GetValueOrDefault("client_id") == ClientId
            && query.GetValueOrDefault("response_type") == "code"
            && query.GetValueOrDefault("redirect_uri") is { } redirectUri && (redirectUri == RedirectUri || redirectUri == StagingRedirectUri)
            && query.GetValueOrDefault("code_challenge_method") == "S256"
            && !string.IsNullOrEmpty(query.GetValueOrDefault("code_challenge"))
            && !string.IsNullOrEmpty(query.GetValueOrDefault("state"));
        if (!valid)
        {
            return FakeResponses.Html(HttpStatusCode.BadRequest, "<html><head><title>Error</title></head><body>Invalid parameter</body></html>");
        }

        var session = new LoginSession(Guid.CreateVersion7().ToString("N"), Guid.CreateVersion7().ToString("N"), query["state"], query["code_challenge"], query["redirect_uri"]);
        logins[session.Code] = session;
        var response = FakeResponses.Html(HttpStatusCode.OK, LoginPage(session.Code, null));
        response.Headers.Add("Set-Cookie", $"AUTH_SESSION_ID={session.Cookie}.keycloak; Version=1; Path=/realms/{Realm}/; HttpOnly");
        response.Headers.Add("Set-Cookie", $"KC_RESTART=eyJhbGciOiJIUzUxMiJ9.{session.Code}; Version=1; Path=/realms/{Realm}/; HttpOnly");
        return response;
    }

    private FakeAnswer Login(Dictionary<string, string> query, Dictionary<string, string> form, string cookie)
    {
        var username = form.GetValueOrDefault("username");
        if (!logins.TryGetValue(query.GetValueOrDefault("session_code") ?? string.Empty, out var session)
            || query.GetValueOrDefault("client_id") != ClientId
            || !cookie.Contains($"AUTH_SESSION_ID={session.Cookie}.keycloak", StringComparison.Ordinal))
        {
            return new FakeAnswer("login", username, FakeResponses.Html(HttpStatusCode.BadRequest, "<html><head><title>Cookie not found</title></head><body>Cookie not found. Please make sure cookies are enabled in your browser.</body></html>"));
        }

        if (username is null || !users.TryGetValue(username, out var user) || user.Password != form.GetValueOrDefault("password") || !user.Enabled)
        {
            return new FakeAnswer("login", username, FakeResponses.Html(HttpStatusCode.OK, LoginPage(session.Code, InvalidCredentials)));
        }

        logins.Remove(session.Code);
        var code = Guid.CreateVersion7().ToString("N");
        codes[code] = new IssuedCode(username, session.Challenge, session.RedirectUri);
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri($"{session.RedirectUri}?state={Uri.EscapeDataString(session.State)}&session_state={Guid.CreateVersion7()}&iss={Uri.EscapeDataString(RealmUrl)}&code={code}");
        return new FakeAnswer("login", username, response);
    }

    private FakeAnswer Token(Dictionary<string, string> form)
    {
        tokenRequests.Add(form);
        if (form.GetValueOrDefault("grant_type") == "client_credentials")
        {
            return new FakeAnswer("token", null, form.GetValueOrDefault("client_id") == SeederClientId && form.GetValueOrDefault("client_secret") == SeederClientSecret
                ? FakeResponses.Ok(new JsonObject { ["access_token"] = SeederClientToken, ["token_type"] = "Bearer", ["expires_in"] = 300 })
                : FakeResponses.Json(HttpStatusCode.Unauthorized, new { error = "unauthorized_client", error_description = "Invalid client or Invalid client credentials" }));
        }

        if (form.GetValueOrDefault("grant_type") != "authorization_code")
        {
            return new FakeAnswer("token", null, FakeResponses.Json(HttpStatusCode.BadRequest, new { error = "unauthorized_client", error_description = "Client not allowed for direct access grants" }));
        }

        var code = form.GetValueOrDefault("code") ?? string.Empty;
        if (!codes.Remove(code, out var issued)
            || form.GetValueOrDefault("client_id") != ClientId
            || form.GetValueOrDefault("redirect_uri") != issued.RedirectUri
            || Challenge(form.GetValueOrDefault("code_verifier") ?? string.Empty) != issued.Challenge)
        {
            return new FakeAnswer("token", null, FakeResponses.Json(HttpStatusCode.BadRequest, new { error = "invalid_grant" }));
        }

        var token = new JsonObject
        {
            ["access_token"] = TokenFor(issued.Username),
            ["token_type"] = "Bearer",
            ["expires_in"] = 300,
            ["refresh_token"] = "refresh-" + issued.Username,
            ["id_token"] = "id-" + issued.Username,
        };
        return new FakeAnswer("token", issued.Username, FakeResponses.Ok(token));
    }

    private static HttpResponseMessage MasterToken(Dictionary<string, string> form)
    {
        if (form.GetValueOrDefault("grant_type") == "password"
            && form.GetValueOrDefault("client_id") == "admin-cli"
            && form.GetValueOrDefault("username") == Administrator
            && form.GetValueOrDefault("password") == AdministratorPassword)
        {
            return FakeResponses.Ok(new JsonObject { ["access_token"] = AdminToken, ["token_type"] = "Bearer", ["expires_in"] = 60 });
        }

        return FakeResponses.Json(HttpStatusCode.Unauthorized, new { error = "invalid_grant" });
    }

    private FakeAnswer Admin(string method, string path, Dictionary<string, string> query, string body)
    {
        if (method == "GET" && path == "/users")
        {
            var username = query.GetValueOrDefault("username") ?? string.Empty;
            var found = users.Values.Where(user => query.GetValueOrDefault("exact") == "true" ? user.Username == username : user.Username.Contains(username, StringComparison.Ordinal));
            return new FakeAnswer("/users", null, FakeResponses.Ok(found.Select(Representation).ToList()));
        }

        if (method == "POST" && path == "/users")
        {
            var created = JsonNode.Parse(body) ?? new JsonObject();
            var username = created["username"]?.GetValue<string>() ?? string.Empty;
            if (users.ContainsKey(username))
            {
                return new FakeAnswer("/users", null, FakeResponses.Json(HttpStatusCode.Conflict, new { errorMessage = "User exists with same username" }));
            }

            var user = new RealmUser(Guid.CreateVersion7().ToString(), username);
            Apply(user, created);
            foreach (var credential in created["credentials"]?.AsArray() ?? [])
            {
                user.Password = credential?["value"]?.GetValue<string>();
            }

            users[username] = user;
            Changes++;
            var response = new HttpResponseMessage(HttpStatusCode.Created);
            response.Headers.Location = new Uri($"{FakeApplication.KeycloakUrl.ToString().TrimEnd('/')}/admin/realms/{Realm}/users/{user.Id}");
            return new FakeAnswer("/users", null, response);
        }

        foreach (var template in new[] { "/users/{id}", "/users/{id}/reset-password", "/users/{id}/role-mappings/realm", "/users/{id}/role-mappings/realm/available", "/roles/{name}" })
        {
            if (!FakeRoute.TryMatch(template, path, out var values))
            {
                continue;
            }

            if (template == "/roles/{name}")
            {
                return new FakeAnswer(template, null, method == "GET" && RealmRoles.Contains(values["name"])
                    ? FakeResponses.Ok(Role(values["name"]))
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var user = users.Values.SingleOrDefault(each => each.Id == values["id"]);
            if (user is null)
            {
                return new FakeAnswer(template, null, new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var answer = (method, template) switch
            {
                ("GET", "/users/{id}") => FakeResponses.Ok(Representation(user)),
                ("PUT", "/users/{id}") => Change(() => Apply(user, JsonNode.Parse(body) ?? new JsonObject())),
                ("PUT", "/users/{id}/reset-password") => Change(() => user.Password = JsonNode.Parse(body)?["value"]?.GetValue<string>()),
                ("GET", "/users/{id}/role-mappings/realm") => FakeResponses.Ok(user.Roles.Select(Role).ToList()),
                ("GET", "/users/{id}/role-mappings/realm/available") => FakeResponses.Ok(RealmRoles.Where(role => !user.Roles.Contains(role)).Select(Role).ToList()),
                ("POST", "/users/{id}/role-mappings/realm") => Change(() =>
                {
                    foreach (var role in JsonNode.Parse(body)?.AsArray() ?? [])
                    {
                        user.Roles.Add(role?["name"]?.GetValue<string>() ?? string.Empty);
                    }
                }),
                _ => new HttpResponseMessage(HttpStatusCode.MethodNotAllowed),
            };
            return new FakeAnswer(template, null, answer);
        }

        return new FakeAnswer(string.Empty, null, new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private HttpResponseMessage Change(Action change)
    {
        change();
        Changes++;
        return FakeResponses.NoContent();
    }

    private static void Apply(RealmUser user, JsonNode representation)
    {
        user.Email = representation["email"]?.GetValue<string>() ?? user.Email;
        user.FirstName = representation["firstName"]?.GetValue<string>() ?? user.FirstName;
        user.LastName = representation["lastName"]?.GetValue<string>() ?? user.LastName;
        user.Enabled = representation["enabled"]?.GetValue<bool>() ?? user.Enabled;
        user.EmailVerified = representation["emailVerified"]?.GetValue<bool>() ?? user.EmailVerified;
    }

    private static JsonObject Role(string name) => new() { ["id"] = "role-" + name, ["name"] = name };

    private static JsonObject Representation(RealmUser user) => new()
    {
        ["id"] = user.Id,
        ["username"] = user.Username,
        ["email"] = user.Email,
        ["firstName"] = user.FirstName,
        ["lastName"] = user.LastName,
        ["enabled"] = user.Enabled,
        ["emailVerified"] = user.EmailVerified,
    };

    /// <summary>A user of the realm, as the fake keeps them.</summary>
    public sealed class RealmUser(string id, string username)
    {
        public string Id { get; } = id;

        public string Username { get; } = username;

        public string? Password { get; set; }

        public string? Email { get; set; }

        public string? FirstName { get; set; }

        public string? LastName { get; set; }

        public bool Enabled { get; set; }

        public bool EmailVerified { get; set; }

        public HashSet<string> Roles { get; } = [with(StringComparer.Ordinal)];
    }

    private sealed record LoginSession(string Code, string Cookie, string State, string Challenge, string RedirectUri);

    private sealed record IssuedCode(string Username, string Challenge, string RedirectUri);
}
