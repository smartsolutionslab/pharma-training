using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aspire.Hosting.Testing;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The realm's local users sign in with their names as passwords, and their access tokens carry a verified email and a
/// name. A visitor without an account registers through Keycloak's own form and verifies the address before signing in.
/// </summary>
public partial class RealmUsersTests(AspireFixture aspire)
{
    private const string WebClient = "pharma-training-web";

    /// <summary>The web client requires PKCE (S256); the form is only shown, the code is never exchanged, so a fixed challenge (RFC 7636, appendix B) will do.</summary>
    private const string PkceChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

    private const string WebApp = "http://localhost:5173/";

    private readonly Mailpit mailpit = new(aspire);

    [Theory]
    [InlineData("editor", "editor@example.com", "Emma Editor")]
    [InlineData("editor-b", "editor-b@example.com", "Bea Editor")]
    [InlineData("editor-c", "editor-c@example.com", "Cleo Editor")]
    [InlineData("owner", "owner@example.com", "Olaf Owner")]
    [InlineData("employee-a", "employee-a@example.com", "Anna Employee")]
    [InlineData("employee-b", "employee-b@example.com", "Ben Employee")]
    [InlineData("employee-c", "employee-c@example.com", "Clara Employee")]
    [InlineData("former-employee", "former-employee@example.com", "Frieda Former")]
    [InlineData("support", "support@example.com", "Sam Support")]
    public async Task A_local_user_signs_in_with_a_verified_email_and_a_name(string user, string email, string name)
    {
        var token = await aspire.RequestAccessTokenAsync(user);

        using var payload = Payload(token);

        payload.RootElement.GetProperty("email").GetString().ShouldBe(email);
        payload.RootElement.GetProperty("email_verified").ValueKind.ShouldBe(JsonValueKind.True);
        payload.RootElement.GetProperty("name").GetString().ShouldBe(name);
    }

    [Fact]
    public async Task The_unverified_user_signs_in_with_a_name_and_an_email_keycloak_has_not_verified()
    {
        var token = await aspire.RequestAccessTokenAsync("unverified");

        using var payload = Payload(token);

        payload.RootElement.GetProperty("email").GetString().ShouldBe("unverified@example.com");
        payload.RootElement.GetProperty("email_verified").ValueKind.ShouldBe(JsonValueKind.False);
        payload.RootElement.GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
        RealmRoles(payload).ShouldNotContain("manager");
        RealmRoles(payload).ShouldNotContain("editor");
    }

    [Fact]
    public async Task The_editor_holds_the_editor_role_and_the_owner_does_not()
    {
        var editorToken = await aspire.RequestAccessTokenAsync("editor");
        var ownerToken = await aspire.RequestAccessTokenAsync("owner");

        using var editor = Payload(editorToken);
        using var owner = Payload(ownerToken);

        RealmRoles(editor).ShouldContain("editor");
        RealmRoles(owner).ShouldNotContain("editor");
    }

    [Fact]
    public async Task The_support_user_holds_the_support_role_and_the_developer_does_not()
    {
        var supportToken = await aspire.RequestAccessTokenAsync("support");
        var developerToken = await aspire.RequestAccessTokenAsync("developer");

        using var support = Payload(supportToken);
        using var developer = Payload(developerToken);

        RealmRoles(support).ShouldContain("support");
        RealmRoles(support).ShouldNotContain("manager");
        RealmRoles(developer).ShouldNotContain("support");
    }

    [Fact]
    public async Task The_realm_offers_a_visitor_its_registration_form()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var browser = Browser();

        using var response = await browser.GetAsync(RegistrationForm(), cancellationToken);

        var page = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, page);
        page.ShouldContain("id=\"kc-register-form\"");
    }

    [Fact]
    public async Task A_visitor_who_registers_verifies_the_address_before_signing_in()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var username = $"invitee-{Guid.CreateVersion7():N}";
        var email = $"{username}@example.com";
        using var browser = Browser();

        using var posted = await RegisterAsync(browser, username, email, cancellationToken);
        // Keycloak answers the form with a redirect to its next step, the address to verify, as a browser follows it.
        posted.StatusCode.ShouldBe(HttpStatusCode.Found, await posted.Content.ReadAsStringAsync(cancellationToken));
        var next = posted.Headers.Location ?? throw new InvalidOperationException("Keycloak answered the registration without a next step.");
        using var registered = await FollowWithinKeycloakAsync(browser, next.IsAbsoluteUri ? next : new Uri(browser.BaseAddress ?? next, next), cancellationToken);

        var verifyPage = await registered.Content.ReadAsStringAsync(cancellationToken);
        registered.StatusCode.ShouldBe(HttpStatusCode.OK, verifyPage);
        verifyPage.ShouldContain("You need to verify your email address to activate your account.");
        var mails = await mailpit.MessagesToAsync(email, 1, cancellationToken);
        using var beforeVerifying = await PasswordGrantAsync(username, cancellationToken);
        var refusal = await beforeVerifying.Content.ReadAsStringAsync(cancellationToken);
        beforeVerifying.StatusCode.ShouldBe(HttpStatusCode.BadRequest, refusal);
        refusal.ShouldContain("invalid_grant");

        var link = VerificationLink().Match(mails[^1].Text);
        link.Success.ShouldBeTrue($"no verification link in: {mails[^1].Text}");
        using var verified = await FollowWithinKeycloakAsync(browser, new Uri(WebUtility.HtmlDecode(link.Value)), cancellationToken);
        verified.Headers.Location.ShouldNotBeNull().ToString().ShouldStartWith(WebApp);

        using var afterVerifying = await PasswordGrantAsync(username, cancellationToken);
        afterVerifying.StatusCode.ShouldBe(HttpStatusCode.OK, await afterVerifying.Content.ReadAsStringAsync(cancellationToken));
        var token = await afterVerifying.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        using var payload = Payload(token.GetProperty("access_token").GetString() ?? string.Empty);
        payload.RootElement.GetProperty("email").GetString().ShouldBe(email);
        payload.RootElement.GetProperty("email_verified").ValueKind.ShouldBe(JsonValueKind.True);
    }

    private static Uri RegistrationForm() =>
        new($"/realms/pharma-training/protocol/openid-connect/registrations?client_id={WebClient}&response_type=code&scope=openid&redirect_uri={Uri.EscapeDataString(WebApp)}&code_challenge={PkceChallenge}&code_challenge_method=S256", UriKind.Relative);

    /// <summary>Fills in Keycloak's registration form as a browser does, with the cookies of the page that showed it.</summary>
    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient browser, string username, string email, CancellationToken cancellationToken)
    {
        using var form = await browser.GetAsync(RegistrationForm(), cancellationToken);
        var page = await form.Content.ReadAsStringAsync(cancellationToken);
        form.StatusCode.ShouldBe(HttpStatusCode.OK, page);
        var action = FormAction().Match(page);
        action.Success.ShouldBeTrue($"no registration form in: {page}");

        using var fields = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["firstName"] = "Ina",
            ["lastName"] = "Invitee",
            ["email"] = email,
            ["username"] = username,
            ["password"] = username,
            ["password-confirm"] = username,
        });
        return await browser.PostAsync(new Uri(WebUtility.HtmlDecode(action.Groups["action"].Value)), fields, cancellationToken);
    }

    /// <summary>Follows redirects while they stay on Keycloak, and answers the first that leaves it, or the last page.</summary>
    private static async Task<HttpResponseMessage> FollowWithinKeycloakAsync(HttpClient browser, Uri address, CancellationToken cancellationToken)
    {
        var response = await browser.GetAsync(address, cancellationToken);
        for (var hops = 0; hops < 10; hops++)
        {
            var location = response.Headers.Location;
            if (location is null || location.ToString().StartsWith(WebApp, StringComparison.Ordinal))
            {
                return response;
            }

            var next = location.IsAbsoluteUri ? location : new Uri(browser.BaseAddress ?? address, location);
            response.Dispose();
            response = await browser.GetAsync(next, cancellationToken);
        }

        return response;
    }

    private async Task<HttpResponseMessage> PasswordGrantAsync(string username, CancellationToken cancellationToken)
    {
        using var identity = new HttpClient { BaseAddress = aspire.App.GetEndpoint("keycloak", "http") };
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = WebClient,
            ["username"] = username,
            ["password"] = username,
        });
        return await identity.PostAsync(new Uri("/realms/pharma-training/protocol/openid-connect/token", UriKind.Relative), form, cancellationToken);
    }

    /// <summary>A client that keeps Keycloak's cookies as a browser does and stops at every redirect.</summary>
    private HttpClient Browser() =>
        new(new LocalhostCookies())
        {
            BaseAddress = aspire.App.GetEndpoint("keycloak", "http"),
        };

    [GeneratedRegex("<form(?=[^>]*id=\"kc-register-form\")[^>]*action=\"(?<action>[^\"]+)\"")]
    private static partial Regex FormAction();

    [GeneratedRegex("https?://[^\\s\"<>]+/login-actions/action-token\\?[^\\s\"<>]+")]
    private static partial Regex VerificationLink();

    private static JsonDocument Payload(string token)
    {
        var bytes = Base64Url.DecodeFromChars(token.Split('.')[1]);
        return JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
    }

    private static List<string> RealmRoles(JsonDocument payload)
    {
        List<string> roles = [];
        if (payload.RootElement.TryGetProperty("realm_access", out var realmAccess)
            && realmAccess.TryGetProperty("roles", out var granted))
        {
            foreach (var role in granted.EnumerateArray())
            {
                roles.Add(role.GetString() ?? string.Empty);
            }
        }

        return roles;
    }

    /// <summary>
    /// Keeps cookies by name and sends them back, as a browser does on localhost. Keycloak marks its session cookies
    /// Secure; a browser treats http://localhost as secure and sends them, .NET's CookieContainer does not over http.
    /// </summary>
    private sealed class LocalhostCookies() : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        private readonly Dictionary<string, string> cookies = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (cookies.Count > 0)
            {
                request.Headers.Add("Cookie", string.Join("; ", cookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
            {
                foreach (var value in values)
                {
                    Keep(value);
                }
            }

            return response;
        }

        private void Keep(string setCookie)
        {
            var pair = setCookie.Split(';', 2)[0];
            var at = pair.IndexOf('=', StringComparison.Ordinal);
            if (at <= 0)
            {
                return;
            }

            var name = pair[..at].Trim();
            var content = pair[(at + 1)..];
            if (content.Length == 0 || setCookie.Contains("Max-Age=0", StringComparison.OrdinalIgnoreCase))
            {
                cookies.Remove(name);
                return;
            }

            cookies[name] = content;
        }
    }
}
