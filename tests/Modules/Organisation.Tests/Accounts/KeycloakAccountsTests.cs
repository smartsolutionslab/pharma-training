using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Accounts;

/// <summary>
/// #115 slice 6: Organisation reads and deletes a person's Keycloak account over the admin API, as the confidential client
/// <c>pharma-training-erasure</c>. Each call mints a token with the client credentials grant at Keycloak's internal
/// address, then reads or deletes <c>/admin/realms/pharma-training/users/{id}</c> with it. Only an address Keycloak
/// verified is read (docs/adr/0014); "not found" means no account, or one that is deleted already; any other answer
/// throws, naming the step and the status and never the secret.
/// </summary>
public sealed class KeycloakAccountsTests : IDisposable
{
    private const string Address = "http://keycloak.internal:8080";
    private const string Realm = "pharma-training";
    private const string ClientId = "pharma-training-erasure";
    private const string Secret = "erasure-client-secret-0123456789abcdef";
    private const string Token = "minted-access-token";

    private readonly RecordingHandler handler = new();
    private readonly PersonId person = PersonId.From(Guid.CreateVersion7());

    public void Dispose() => handler.Dispose();

    private string UserUri => $"{Address}/admin/realms/{Realm}/users/{person.Value:D}";

    [Fact]
    public async Task Finding_an_account_mints_a_client_credentials_token_and_reads_the_user_with_it()
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.OK, User(email: "Account@Example.org", emailVerified: true));

        var found = await Accounts().FindAsync(person, TestContext.Current.CancellationToken);

        handler.Requests.Count.ShouldBe(2);
        var mint = handler.Requests[0];
        mint.Method.ShouldBe(HttpMethod.Post);
        mint.Uri.ShouldBe($"{Address}/realms/{Realm}/protocol/openid-connect/token");
        mint.Authorization.ShouldBeNull("the token request authenticates with the client's credentials in the form");
        Form(mint.Body).ShouldBe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = ClientId,
                ["client_secret"] = Secret,
            },
            ignoreOrder: true);
        var read = handler.Requests[1];
        read.Method.ShouldBe(HttpMethod.Get);
        read.Uri.ShouldBe(UserUri);
        read.Authorization.ShouldBe($"Bearer {Token}");
        found.HasValue.ShouldBeTrue("Keycloak answered the user");
        found.Value.VerifiedAddress.ShouldBe(Option<EmailAddress>.Some(EmailAddress.From("account@example.org")));
        found.Value.IsServiceAccount.ShouldBeFalse();
    }

    [Fact]
    public async Task An_account_keycloak_does_not_have_is_none()
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.NotFound, """{"error":"User not found"}""");

        var found = await Accounts().FindAsync(person, TestContext.Current.CancellationToken);

        found.HasValue.ShouldBeFalse();
    }

    [Theory]
    [InlineData("account@example.org", true, "account@example.org")]
    [InlineData("account@example.org", false, null)]
    [InlineData(null, true, null)]
    [InlineData(null, null, null)]
    [InlineData("account@example.org", null, null)]
    [InlineData("not an address", true, null)]
    [InlineData("Someone <account@example.org>", true, null)]
    public async Task Only_a_verified_valid_address_is_read(string? email, bool? emailVerified, string? expected)
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.OK, User(email, emailVerified));

        var found = await Accounts().FindAsync(person, TestContext.Current.CancellationToken);

        found.HasValue.ShouldBeTrue("Keycloak answered the user");
        found.Value.VerifiedAddress.ShouldBe(expected is null ? Option<EmailAddress>.None : Option<EmailAddress>.Some(EmailAddress.From(expected)));
    }

    [Fact]
    public async Task A_service_account_is_marked()
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.OK, User(email: null, emailVerified: false, serviceAccountClientId: ClientId));

        var found = await Accounts().FindAsync(person, TestContext.Current.CancellationToken);

        found.HasValue.ShouldBeTrue("Keycloak answered the user");
        found.Value.IsServiceAccount.ShouldBeTrue();
    }

    [Fact]
    public async Task Deleting_sends_delete_for_the_users_id()
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.NoContent, string.Empty);

        await Accounts().DeleteAsync(person, TestContext.Current.CancellationToken);

        handler.Requests.Count.ShouldBe(2);
        handler.Requests[0].Method.ShouldBe(HttpMethod.Post);
        handler.Requests[0].Uri.ShouldBe($"{Address}/realms/{Realm}/protocol/openid-connect/token");
        var delete = handler.Requests[1];
        delete.Method.ShouldBe(HttpMethod.Delete);
        delete.Uri.ShouldBe(UserUri);
        delete.Authorization.ShouldBe($"Bearer {Token}");
    }

    [Fact]
    public async Task Deleting_an_account_that_is_gone_succeeds()
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.NotFound, """{"error":"User not found"}""");

        await Should.NotThrowAsync(() => Accounts().DeleteAsync(person, TestContext.Current.CancellationToken));

        handler.Requests[1].Method.ShouldBe(HttpMethod.Delete);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    public async Task A_refused_token_mint_throws_without_the_secret(HttpStatusCode status, bool deleting)
    {
        handler.Answer(status, $$"""{"error":"unauthorized_client","error_description":"Invalid client or Invalid client credentials {{Secret}}"}""");

        var thrown = await Should.ThrowAsync<AccountsUnavailableException>(() => Call(deleting));

        thrown.Message.ShouldContain(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture));
        thrown.Message.ShouldContain("token");
        thrown.Message.ShouldNotContain(Secret);
        thrown.ToString().ShouldNotContain(Secret);
        handler.Requests.ShouldHaveSingleItem("nothing is read or deleted without a token");
    }

    [Theory]
    [InlineData(false, "read")]
    [InlineData(true, "delete")]
    public async Task A_forbidden_admin_call_throws(bool deleting, string step)
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.Forbidden, """{"error":"HTTP 403 Forbidden"}""");

        var thrown = await Should.ThrowAsync<AccountsUnavailableException>(() => Call(deleting));

        thrown.Message.ShouldContain("403");
        thrown.Message.ShouldContain(step);
        thrown.Message.ShouldNotContain(Token);
        thrown.Message.ShouldNotContain(Secret);
    }

    [Theory]
    [InlineData(false, "read")]
    [InlineData(true, "delete")]
    public async Task A_server_error_throws(bool deleting, string step)
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.InternalServerError, """{"error":"unknown_error"}""");

        var thrown = await Should.ThrowAsync<AccountsUnavailableException>(() => Call(deleting));

        thrown.Message.ShouldContain("500");
        thrown.Message.ShouldContain(step);
    }

    [Fact]
    public async Task The_user_read_does_not_leak_the_address_into_a_failure()
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.BadGateway, """{"email":"account@example.org"}""");

        var thrown = await Should.ThrowAsync<AccountsUnavailableException>(() => Accounts().FindAsync(person, TestContext.Current.CancellationToken));

        thrown.Message.ShouldNotContain("account@example.org");
    }

    private static Dictionary<string, string> Form(string body) =>
        body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')), pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')), StringComparer.Ordinal);

    private static (HttpStatusCode Status, string Body) TokenMinted() =>
        (HttpStatusCode.OK, $$"""{"access_token":"{{Token}}","expires_in":300,"token_type":"Bearer"}""");

    /// <summary>A user as Keycloak's admin API answers it, with each field only where it is given.</summary>
    private string User(string? email, bool? emailVerified, string? serviceAccountClientId = null)
    {
        var fields = new List<string> { $"\"id\":\"{person.Value:D}\"", "\"username\":\"someone\"", "\"enabled\":true" };
        if (email is not null)
        {
            fields.Add($"\"email\":\"{email}\"");
        }

        if (emailVerified is not null)
        {
            fields.Add($"\"emailVerified\":{(emailVerified.Value ? "true" : "false")}");
        }

        if (serviceAccountClientId is not null)
        {
            fields.Add($"\"serviceAccountClientId\":\"{serviceAccountClientId}\"");
        }

        return "{" + string.Join(',', fields) + "}";
    }

    private Task Call(bool deleting) =>
        deleting
            ? Accounts().DeleteAsync(person, TestContext.Current.CancellationToken)
            : Accounts().FindAsync(person, TestContext.Current.CancellationToken);

    private KeycloakAccounts Accounts() =>
        new(
            new HttpClient(handler, disposeHandler: false),
            Options.Create(new KeycloakAccountsSettings { Address = Address, Realm = Realm, ClientId = ClientId, ClientSecret = Secret }));

    /// <summary>Answers each request with the next scripted response, and records what was asked.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> answers = new();

        public List<RecordedRequest> Requests { get; } = [];

        public void Answer((HttpStatusCode Status, string Body) answer) => answers.Enqueue(answer);

        public void Answer(HttpStatusCode status, string body) => answers.Enqueue((status, body));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri?.AbsoluteUri ?? string.Empty, request.Headers.Authorization?.ToString(), body));
            var (status, answer) = answers.Count > 0 ? answers.Dequeue() : (HttpStatusCode.InternalServerError, "no answer scripted");
            return new HttpResponseMessage(status) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Uri, string? Authorization, string Body);
}
