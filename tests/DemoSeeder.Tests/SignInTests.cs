using System.Security.Cryptography;
using System.Text;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder.Tests;

/// <summary>
/// Sign-in is the authorization-code flow with PKCE against the realm's web client, without a browser (owner decision 2 on
/// #457): the login form is posted with the session cookie, the code read from the redirect, which is not followed, and
/// exchanged with the verifier. The published realm refuses the password grant, and so does the fake.
/// </summary>
public class SignInTests
{
    private const string User = "demo-markt-1";

    private const string Password = "the-users-password";

    [Fact]
    public async Task Sign_in_follows_the_login_form_and_exchanges_the_code_with_the_verifier()
    {
        var harness = new SeederHarness();
        harness.App.Identity.AddUser(User, Password);
        using var http = new HttpClient(harness.App, disposeHandler: false);
        var signIn = new KeycloakSignIn(http, new Uri(FakeKeycloak.RealmUrl), FakeKeycloak.ClientId, FakeApplication.WebOrigin);

        var token = await signIn.SignInAsync(User, Password, TestContext.Current.CancellationToken);

        token.ShouldBe(FakeKeycloak.TokenFor(User));
        var requests = harness.App.Requests;
        requests.Select(request => (request.Method, request.Route, request.Status)).ShouldBe(
            [("GET", "auth", 200), ("POST", "login", 302), ("POST", "token", 200)],
            "Authorize, post the login form, exchange the code: three calls, the redirect to the web app never followed.");

        var authorize = FakeRoute.Pairs(requests[0].Query);
        authorize["code_challenge_method"].ShouldBe("S256");
        authorize["redirect_uri"].ShouldBe(FakeKeycloak.RedirectUri);
        authorize["client_id"].ShouldBe(FakeKeycloak.ClientId);

        var login = FakeRoute.Pairs(requests[1].Body);
        login["username"].ShouldBe(User);
        login["password"].ShouldBe(Password);
        requests[1].Cookie.ShouldContain("AUTH_SESSION_ID=", Case.Sensitive, "The login form is posted with the cookie the login page set.");
        requests[1].Query.ShouldContain("session_code=", Case.Sensitive, "The form is posted to its action, &amp; decoded.");
        requests[1].Query.ShouldNotContain("&amp;", Case.Sensitive);

        var exchange = FakeRoute.Pairs(requests[2].Body);
        exchange["grant_type"].ShouldBe("authorization_code");
        var verifier = exchange["code_verifier"];
        verifier.Length.ShouldBeInRange(43, 128, "RFC 7636 asks for a verifier of 43 to 128 characters.");
        Challenge(verifier).ShouldBe(authorize["code_challenge"]);
    }

    [Fact]
    public async Task Every_sign_in_has_a_verifier_and_state_of_its_own()
    {
        var harness = new SeederHarness();
        harness.App.Identity.AddUser(User, Password);
        using var http = new HttpClient(harness.App, disposeHandler: false);
        var signIn = new KeycloakSignIn(http, new Uri(FakeKeycloak.RealmUrl), FakeKeycloak.ClientId, FakeApplication.WebOrigin);

        await signIn.SignInAsync(User, Password, TestContext.Current.CancellationToken);
        await signIn.SignInAsync(User, Password, TestContext.Current.CancellationToken);

        var authorizations = harness.App.Requests.Where(request => request.Route == "auth").Select(request => FakeRoute.Pairs(request.Query)).ToList();
        authorizations.Count.ShouldBe(2);
        authorizations[0]["state"].ShouldNotBe(authorizations[1]["state"]);
        authorizations[0]["code_challenge"].ShouldNotBe(authorizations[1]["code_challenge"]);
    }

    [Fact]
    public async Task A_login_page_that_does_not_redirect_fails_naming_the_user_not_the_page()
    {
        var harness = new SeederHarness();
        harness.App.Identity.AddUser(User, "another-password");
        using var http = new HttpClient(harness.App, disposeHandler: false);
        var signIn = new KeycloakSignIn(http, new Uri(FakeKeycloak.RealmUrl), FakeKeycloak.ClientId, FakeApplication.WebOrigin);

        var failure = await Should.ThrowAsync<SeedingException>(() => signIn.SignInAsync(User, Password, TestContext.Current.CancellationToken));

        failure.Message.ShouldContain(User);
        failure.Message.ShouldContain(FakeKeycloak.LoginTitle, Case.Sensitive, "The failure names the page by its title.");
        failure.Message.ShouldNotContain(FakeKeycloak.InvalidCredentials, Case.Sensitive, "Never the page's content.");
        failure.Message.ShouldNotContain(Password, Case.Sensitive, "Never a password.");
        harness.App.Requests.ShouldNotContain(request => request.Route == "token", "No code, no exchange.");
    }

    private static string Challenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
