using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Accounts;

/// <summary>
/// #462: the token request of <c>KeycloakAccounts</c> gets one attempt through the host's real resilience pipeline (owner
/// decision A on #115), while its reads are retried. The client is the one <c>AddKeycloakAccounts</c> registers, on a host
/// built with <c>AddServiceDefaults()</c>, with only its primary handler swapped for a counting fake Keycloak, as
/// <c>tests/Architecture.Tests/Rules/RetryProbe.cs</c> does.
/// </summary>
public sealed class KeycloakAccountsResilienceTests
{
    private const string Address = "http://keycloak.internal:8080";
    private const string Realm = "pharma-training";

    /// <summary>
    /// Held for a whole test: the first pipeline built in a process sets up the resilience options' RangeAttributes, and
    /// that setup is not thread-safe.
    /// </summary>
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    private readonly PersonId person = PersonId.From(Guid.CreateVersion7());

    private static string TokenUri => $"{Address}/realms/{Realm}/protocol/openid-connect/token";

    private string UserUri => $"{Address}/admin/realms/{Realm}/users/{person.Value:D}";

    [Fact]
    public async Task The_token_request_gets_one_attempt()
    {
        var keycloak = new FakeKeycloak(TokenUri, HttpStatusCode.ServiceUnavailable, UserUri, HttpStatusCode.OK);

        await FindThroughTheHostAsync(keycloak);

        keycloak.Count(HttpMethod.Post, TokenUri).ShouldBe(1, "a POST gets one attempt; Wolverine retries the consumer instead");
    }

    [Fact]
    public async Task A_failing_user_read_is_retried()
    {
        var keycloak = new FakeKeycloak(TokenUri, HttpStatusCode.OK, UserUri, HttpStatusCode.ServiceUnavailable);

        await FindThroughTheHostAsync(keycloak);

        keycloak.Count(HttpMethod.Get, UserUri).ShouldBe(4, "one attempt plus the standard handler's three retries: the pipeline is attached");
    }

    private async Task FindThroughTheHostAsync(FakeKeycloak keycloak)
    {
        await OneAtATime.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            var builder = Microsoft.Extensions.Hosting.Host.CreateEmptyApplicationBuilder(settings: null);
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:Accounts:Address"] = Address,
                ["Keycloak:Accounts:Realm"] = Realm,
                ["Keycloak:Accounts:ClientId"] = "pharma-training-erasure",
                ["Keycloak:Accounts:ClientSecret"] = "erasure-client-secret-0123456789abcdef",
            });
            builder.AddServiceDefaults();
            builder.Services.AddKeycloakAccounts(builder.Configuration);
            builder.Services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => keycloak));

            // The attempt count is what these tests are about; the backoff does not change it.
            builder.Services.PostConfigureAll<HttpStandardResilienceOptions>(options =>
            {
                options.Retry.Delay = TimeSpan.Zero;
                options.Retry.UseJitter = false;
            });

            await using var provider = builder.Services.BuildServiceProvider();
            var accounts = provider.GetRequiredService<IAccounts>();

            await Should.ThrowAsync<AccountsUnavailableException>(() => accounts.FindAsync(person, TestContext.Current.CancellationToken));
        }
        finally
        {
            OneAtATime.Release();
        }
    }

    /// <summary>Answers the token request and the user read with fixed statuses, and counts every request by method and URI.</summary>
    private sealed class FakeKeycloak(string tokenUri, HttpStatusCode token, string userUri, HttpStatusCode user) : HttpMessageHandler
    {
        private readonly List<(HttpMethod Method, string Uri)> requests = [];

        public int Count(HttpMethod method, string uri)
        {
            lock (requests)
            {
                return requests.Count(request => request.Method == method && request.Uri == uri);
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            lock (requests)
            {
                requests.Add((request.Method, uri));
            }

            if (uri == tokenUri)
            {
                return Task.FromResult(Answer(request, token, """{"access_token":"minted-access-token","token_type":"Bearer"}"""));
            }

            if (uri == userUri)
            {
                return Task.FromResult(Answer(request, user, """{"username":"someone","email":"someone@example.org","emailVerified":true}"""));
            }

            return Task.FromResult(Answer(request, HttpStatusCode.NotFound, "{}"));
        }

        private static HttpResponseMessage Answer(HttpRequestMessage request, HttpStatusCode status, string json) =>
            new(status) { RequestMessage = request, Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
