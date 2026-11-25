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
/// throws, naming the step and the status and never the secret. The account's effective realm roles are read too
/// (<c>/role-mappings/realm/composite</c>), so a staff account (<c>support</c>, <c>editor</c>, <c>manager</c>) is marked
/// and never deleted (owner decision on #115); a client's service account is marked by either shape Keycloak answers.
/// </summary>
public sealed class KeycloakAccountsTests : IDisposable
{
    private const string Address = "http://keycloak.internal:8080";
    private const string Realm = "pharma-training";
    private const string ClientId = "pharma-training-erasure";
    private const string Secret = "erasure-client-secret-0123456789abcdef";
    private const string Token = "minted-access-token";

    private static readonly Guid RealmManagement = Guid.CreateVersion7();
    private static readonly Guid ServiceAccount = Guid.CreateVersion7();

    private readonly RecordingHandler handler = new();
    private readonly PersonId person = PersonId.From(Guid.CreateVersion7());

    public void Dispose() => handler.Dispose();

    private string UserUri => $"{Address}/admin/realms/{Realm}/users/{person.Value:D}";

    private string RolesUri => $"{UserUri}/role-mappings/realm/composite";

    private static string ServiceAccountUri => $"{Address}/admin/realms/{Realm}/users?username=service-account-{ClientId}&exact=true";

    private static string ServiceAccountMappingsUri => $"{Address}/admin/realms/{Realm}/users/{ServiceAccount:D}/role-mappings";

    private string ClientRolesUri => $"{UserUri}/role-mappings/clients/{RealmManagement:D}/composite";

    [Fact]
    public async Task Finding_an_account_mints_a_client_credentials_token_and_reads_the_user_with_it()
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.OK, User(email: "Account@Example.org", emailVerified: true));
        AnswerRoles(Roles("default-roles-pharma-training", "offline_access", "uma_authorization"));

        var found = await Accounts().FindAsync(person, TestContext.Current.CancellationToken);

        handler.Requests.Count.ShouldBe(6);
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
        var roles = handler.Requests[2];
        roles.Method.ShouldBe(HttpMethod.Get);
        roles.Uri.ShouldBe(RolesUri);
        roles.Authorization.ShouldBe($"Bearer {Token}", "the role mappings are read with the same token");
        var serviceAccount = handler.Requests[3];
        serviceAccount.Method.ShouldBe(HttpMethod.Get);
        serviceAccount.Uri.ShouldBe(ServiceAccountUri, "realm-management's id comes from the client's own service account, which needs no role to list clients");
        serviceAccount.Authorization.ShouldBe($"Bearer {Token}");
        var mappings = handler.Requests[4];
        mappings.Method.ShouldBe(HttpMethod.Get);
        mappings.Uri.ShouldBe(ServiceAccountMappingsUri);
        mappings.Authorization.ShouldBe($"Bearer {Token}");
        var clientRoles = handler.Requests[5];
        clientRoles.Method.ShouldBe(HttpMethod.Get);
        clientRoles.Uri.ShouldBe(ClientRolesUri);
        clientRoles.Authorization.ShouldBe($"Bearer {Token}");
        found.HasValue.ShouldBeTrue("Keycloak answered the user");
        found.Value.VerifiedAddress.ShouldBe(Option<EmailAddress>.Some(EmailAddress.From("account@example.org")));
        found.Value.IsServiceAccount.ShouldBeFalse();
        found.Value.HoldsStaffRole.ShouldBeFalse("the realm's default roles are no staff role");
    }

    [Fact]
    public async Task An_account_keycloak_does_not_have_is_none()
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.NotFound, """{"error":"User not found"}""");

        var found = await Accounts().FindAsync(person, TestContext.Current.CancellationToken);

        found.HasValue.ShouldBeFalse();
        handler.Requests.Count.ShouldBe(2, "no roles are read for an account Keycloak does not have");
    }

    [Fact]
    public async Task An_account_deleted_before_its_roles_are_read_is_none()
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.OK, User(email: null, emailVerified: null));
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
        AnswerRoles(Roles());

        var found = await Accounts().FindAsync(person, TestContext.Current.CancellationToken);

        found.HasValue.ShouldBeTrue("Keycloak answered the user");
        found.Value.VerifiedAddress.ShouldBe(expected is null ? Option<EmailAddress>.None : Option<EmailAddress>.Some(EmailAddress.From(expected)));
    }

    [Fact]
    public async Task A_service_account_is_marked_by_its_client_id()
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.OK, User(email: null, emailVerified: false, serviceAccountClientId: ClientId));
        AnswerRoles(Roles());

        var found = await Accounts().FindAsync(person, TestContext.Current.CancellationToken);

        found.HasValue.ShouldBeTrue("Keycloak answered the user");
        found.Value.IsServiceAccount.ShouldBeTrue();
    }

    /// <summary>
    /// Keycloak's admin API may leave <c>serviceAccountClientId</c> out of a user it answers (it is filled in exports);
    /// the username Keycloak gives every service account, <c>service-account-{clientId}</c>, marks it all the same.
    /// </summary>
    [Theory]
    [InlineData("service-account-pharma-training-erasure")]
    [InlineData("service-account-x1-runner")]
    [InlineData("Service-Account-x1-runner")]
    public async Task A_service_account_is_marked_by_its_username_alone(string username)
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.OK, User(email: null, emailVerified: false, username: username));
        AnswerRoles(Roles());

        var found = await Accounts().FindAsync(person, TestContext.Current.CancellationToken);

        found.HasValue.ShouldBeTrue("Keycloak answered the user");
        found.Value.IsServiceAccount.ShouldBeTrue();
    }

    [Theory]
    [InlineData("someone@example.org")]
    [InlineData("service-account")]
    [InlineData("my-service-account-x")]
    public async Task A_person_named_otherwise_is_no_service_account(string username)
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.OK, User(email: null, emailVerified: false, username: username));
        AnswerRoles(Roles());

        var found = await Accounts().FindAsync(person, TestContext.Current.CancellationToken);

        found.HasValue.ShouldBeTrue("Keycloak answered the user");
        found.Value.IsServiceAccount.ShouldBeFalse();
    }

    [Theory]
    [InlineData("support")]
    [InlineData("editor")]
    [InlineData("manager")]
    public async Task A_staff_account_is_marked(string role)
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.OK, User(email: "account@example.org", emailVerified: true));
        AnswerRoles(Roles("default-roles-pharma-training", role, "offline_access"));

        var found = await Accounts().FindAsync(person, TestContext.Current.CancellationToken);

        found.HasValue.ShouldBeTrue("Keycloak answered the user");
        found.Value.HoldsStaffRole.ShouldBeTrue($"the realm role {role} is a staff role");
        found.Value.IsServiceAccount.ShouldBeFalse();
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("owner")]
    [InlineData("employee")]
    [InlineData("supporter")]
    public async Task Another_realm_role_is_no_staff_role(string role)
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.OK, User(email: "account@example.org", emailVerified: true));
        AnswerRoles(Roles(role));

        var found = await Accounts().FindAsync(person, TestContext.Current.CancellationToken);

        found.HasValue.ShouldBeTrue("Keycloak answered the user");
        found.Value.HoldsStaffRole.ShouldBeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_refused_role_read_throws(HttpStatusCode status)
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.OK, User(email: "account@example.org", emailVerified: true));
        handler.Answer(status, """{"error":"refused","name":"account@example.org"}""");

        var thrown = await Should.ThrowAsync<AccountsUnavailableException>(() => Accounts().FindAsync(person, TestContext.Current.CancellationToken));

        thrown.Message.ShouldContain(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture));
        thrown.Message.ShouldContain("roles");
        thrown.Message.ShouldNotContain("account@example.org");
        thrown.Message.ShouldNotContain(Token);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[{\"id\":\"1\"}]")]
    [InlineData("[null,\"manager\"]")]
    public async Task Role_mappings_keycloak_answers_in_another_shape_throw(string body)
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.OK, User(email: "account@example.org", emailVerified: true));
        handler.Answer(HttpStatusCode.OK, body);

        var thrown = await Should.ThrowAsync<AccountsUnavailableException>(() => Accounts().FindAsync(person, TestContext.Current.CancellationToken));

        thrown.Message.ShouldContain("roles");
    }

    /// <summary>
    /// A delegated realm administrator holds a <c>realm-management</c> client role, not a staff realm role; any such role,
    /// composites resolved, marks the account as staff, so it is never deleted.
    /// </summary>
    [Theory]
    [InlineData("manage-users")]
    [InlineData("view-users")]
    [InlineData("realm-admin")]
    public async Task A_realm_management_client_role_marks_a_delegated_admin(string role)
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.OK, User(email: "account@example.org", emailVerified: true));
        AnswerRoles(Roles("default-roles-pharma-training"), ClientRoles(role));

        var found = await Accounts().FindAsync(person, TestContext.Current.CancellationToken);

        found.HasValue.ShouldBeTrue("Keycloak answered the user");
        found.Value.HoldsStaffRole.ShouldBeTrue($"the realm-management role {role} administers the realm");
        handler.Requests[3].Uri.ShouldBe(ServiceAccountUri);
        handler.Requests[4].Uri.ShouldBe(ServiceAccountMappingsUri);
        handler.Requests[5].Uri.ShouldBe(ClientRolesUri);
    }

    [Fact]
    public async Task An_account_deleted_before_its_client_roles_are_read_is_none()
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.OK, User(email: "account@example.org", emailVerified: true));
        handler.Answer(HttpStatusCode.OK, Roles());
        AnswerRealmManagement();
        handler.Answer(HttpStatusCode.NotFound, """{"error":"User not found"}""");

        var found = await Accounts().FindAsync(person, TestContext.Current.CancellationToken);

        found.HasValue.ShouldBeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_refused_service_account_lookup_throws(HttpStatusCode status)
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.OK, User(email: "account@example.org", emailVerified: true));
        handler.Answer(HttpStatusCode.OK, Roles());
        handler.Answer(status, """{"error":"refused"}""");

        var thrown = await Should.ThrowAsync<AccountsUnavailableException>(() => Accounts().FindAsync(person, TestContext.Current.CancellationToken));

        thrown.Message.ShouldContain(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture));
        thrown.Message.ShouldContain("service account");
        thrown.Message.ShouldNotContain(Token);
    }

    /// <summary>The service account must answer exactly once, by its exact username, with a string id.</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[{\"username\":\"service-account-pharma-training-erasure\"}]")]
    [InlineData("[{\"id\":7,\"username\":\"service-account-pharma-training-erasure\"}]")]
    [InlineData("[{\"id\":\"a\",\"username\":\"service-account-pharma-training-erasure\"},{\"id\":\"b\",\"username\":\"service-account-pharma-training-erasure\"}]")]
    [InlineData("[{\"id\":\"a\",\"username\":\"service-account-pharma-training-erasure-2\"}]")]
    public async Task A_service_account_lookup_keycloak_answers_in_another_shape_throws(string body)
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.OK, User(email: "account@example.org", emailVerified: true));
        handler.Answer(HttpStatusCode.OK, Roles());
        handler.Answer(HttpStatusCode.OK, body);

        var thrown = await Should.ThrowAsync<AccountsUnavailableException>(() => Accounts().FindAsync(person, TestContext.Current.CancellationToken));

        thrown.Message.ShouldContain("service account");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_refused_service_account_mappings_read_throws(HttpStatusCode status)
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.OK, User(email: "account@example.org", emailVerified: true));
        handler.Answer(HttpStatusCode.OK, Roles());
        handler.Answer(HttpStatusCode.OK, ServiceAccountFound());
        handler.Answer(status, """{"error":"refused"}""");

        var thrown = await Should.ThrowAsync<AccountsUnavailableException>(() => Accounts().FindAsync(person, TestContext.Current.CancellationToken));

        thrown.Message.ShouldContain(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture));
        thrown.Message.ShouldContain("service account");
        thrown.Message.ShouldNotContain(Token);
    }

    /// <summary>
    /// The service account's own mappings must name <c>realm-management</c> with a string id: it holds that client's
    /// <c>manage-users</c>, so anything else means the realm is not what Organisation expects, and the read fails closed.
    /// </summary>
    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"realmMappings\":[]}")]
    [InlineData("{\"clientMappings\":{\"account\":{\"id\":\"a\",\"client\":\"account\"}}}")]
    [InlineData("{\"clientMappings\":{\"realm-management\":{\"client\":\"realm-management\"}}}")]
    [InlineData("{\"clientMappings\":{\"realm-management\":{\"id\":7,\"client\":\"realm-management\"}}}")]
    [InlineData("{\"clientMappings\":{\"realm-management\":{\"id\":\"\",\"client\":\"realm-management\"}}}")]
    public async Task Service_account_mappings_keycloak_answers_in_another_shape_throw(string body)
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.OK, User(email: "account@example.org", emailVerified: true));
        handler.Answer(HttpStatusCode.OK, Roles());
        handler.Answer(HttpStatusCode.OK, ServiceAccountFound());
        handler.Answer(HttpStatusCode.OK, body);

        var thrown = await Should.ThrowAsync<AccountsUnavailableException>(() => Accounts().FindAsync(person, TestContext.Current.CancellationToken));

        thrown.Message.ShouldContain("service account");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_refused_client_role_read_throws(HttpStatusCode status)
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.OK, User(email: "account@example.org", emailVerified: true));
        handler.Answer(HttpStatusCode.OK, Roles());
        AnswerRealmManagement();
        handler.Answer(status, """{"error":"refused","name":"account@example.org"}""");

        var thrown = await Should.ThrowAsync<AccountsUnavailableException>(() => Accounts().FindAsync(person, TestContext.Current.CancellationToken));

        thrown.Message.ShouldContain(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture));
        thrown.Message.ShouldContain("client roles");
        thrown.Message.ShouldNotContain("account@example.org");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[{\"id\":\"1\"}]")]
    [InlineData("[null]")]
    public async Task Client_role_mappings_keycloak_answers_in_another_shape_throw(string body)
    {
        handler.Answer(TokenMinted());
        handler.Answer(HttpStatusCode.OK, User(email: "account@example.org", emailVerified: true));
        handler.Answer(HttpStatusCode.OK, Roles());
        AnswerRealmManagement();
        handler.Answer(HttpStatusCode.OK, body);

        var thrown = await Should.ThrowAsync<AccountsUnavailableException>(() => Accounts().FindAsync(person, TestContext.Current.CancellationToken));

        thrown.Message.ShouldContain("roles");
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
    private string User(string? email, bool? emailVerified, string? serviceAccountClientId = null, string username = "someone")
    {
        var fields = new List<string> { $"\"id\":\"{person.Value:D}\"", $"\"username\":\"{username}\"", "\"enabled\":true" };
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

    /// <summary>
    /// Scripts the role reads a found account takes: its effective realm roles, the client's own service account and
    /// its mappings, which name <c>realm-management</c>'s id, and the account's effective roles of that client (none
    /// unless given).
    /// </summary>
    private void AnswerRoles(string realmRoles, string? clientRoles = null)
    {
        handler.Answer(HttpStatusCode.OK, realmRoles);
        AnswerRealmManagement();
        handler.Answer(HttpStatusCode.OK, clientRoles ?? ClientRoles());
    }

    /// <summary>Scripts how <c>realm-management</c>'s id is found: the service account, then its own mappings.</summary>
    private void AnswerRealmManagement()
    {
        handler.Answer(HttpStatusCode.OK, ServiceAccountFound());
        handler.Answer(HttpStatusCode.OK, ServiceAccountMappings());
    }

    /// <summary>The answer to <c>GET /users?username=service-account-{clientId}&amp;exact=true</c>: the user, in a list.</summary>
    private static string ServiceAccountFound() =>
        $$"""[{"id":"{{ServiceAccount:D}}","username":"service-account-{{ClientId}}","enabled":true}]""";

    /// <summary>The service account's own role mappings, as Keycloak 26 answers them to a holder of <c>manage-users</c> alone.</summary>
    private static string ServiceAccountMappings() =>
        "{\"clientMappings\":{\"realm-management\":{\"id\":\"" + RealmManagement.ToString("D") + "\",\"client\":\"realm-management\",\"mappings\":"
        + "[{\"id\":\"" + Guid.CreateVersion7().ToString("D") + "\",\"name\":\"manage-users\",\"composite\":false,\"clientRole\":true,\"containerId\":\"" + RealmManagement.ToString("D") + "\"}]}}}";

    /// <summary>The user's effective roles of the <c>realm-management</c> client: client role representations.</summary>
    private static string ClientRoles(params string[] names) =>
        "[" + string.Join(',', names.Select(name => $"{{\"id\":\"{Guid.CreateVersion7():D}\",\"name\":\"{name}\",\"composite\":false,\"clientRole\":true,\"containerId\":\"{RealmManagement:D}\"}}")) + "]";

    /// <summary>The user's effective realm roles as Keycloak's admin API answers them: role representations.</summary>
    private static string Roles(params string[] names) =>
        "[" + string.Join(',', names.Select(name => $"{{\"id\":\"{Guid.CreateVersion7():D}\",\"name\":\"{name}\",\"composite\":false,\"clientRole\":false,\"containerId\":\"{Realm}\"}}")) + "]";

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
