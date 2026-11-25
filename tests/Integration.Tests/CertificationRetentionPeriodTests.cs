using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.CertificationIssuing;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.EditorialWorkflow;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A pharmacy's manager reads and sets how many years the pharmacy keeps a certificate in its view, through the edge
/// (#391): <c>GET</c> and <c>PUT /certification/pharmacies/{id}/retention-period</c>. Who may is decided from
/// Certification's own copy of the caller's membership and its current role (docs/adr/0003): a non-member gets 404, a
/// member whose role lacks the capability 403, a caller without a token 401. A period is 2 to 30 whole years.
/// </summary>
public class CertificationRetentionPeriodTests(AspireFixture aspire)
{
    private static readonly DateTimeOffset LongAgo = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_manager_reads_the_default_sets_a_period_and_reads_it_back()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await aspire.CreateHostClientAsync("developer");
        var pharmacy = await ManagedPharmacyAsync(aspire, developer, cancellationToken);

        (await ReadAsync(developer, pharmacy, cancellationToken)).ShouldBe(new CertificationRetentionPeriodDto(5, true));

        using (var put = await PutAsync(developer, pharmacy, """{"years":7}""", cancellationToken))
        {
            put.StatusCode.ShouldBe(HttpStatusCode.NoContent, await put.Content.ReadAsStringAsync(cancellationToken));
        }

        (await ReadAsync(developer, pharmacy, cancellationToken)).ShouldBe(new CertificationRetentionPeriodDto(7, false));
    }

    [Fact]
    public async Task An_employee_of_the_pharmacy_gets_403()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await aspire.CreateHostClientAsync("developer");
        var pharmacy = Guid.CreateVersion7();
        await CertificationDatabase.DeliverAsync(aspire, StartedType, StartedIn(SubjectOf(developer), pharmacy, "Employee", LongAgo), cancellationToken);

        await ShouldBeRefusedOnBothRoutesAsync(developer, pharmacy, HttpStatusCode.Forbidden, "certification.capability-missing", cancellationToken);
    }

    [Fact]
    public async Task A_manager_demoted_by_a_membership_change_gets_403()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await aspire.CreateHostClientAsync("developer");
        var pharmacy = Guid.CreateVersion7();
        var started = StartedIn(SubjectOf(developer), pharmacy, "Manager", LongAgo);
        await CertificationDatabase.DeliverAsync(aspire, StartedType, started, cancellationToken);
        (await ReadAsync(developer, pharmacy, cancellationToken)).ShouldBe(new CertificationRetentionPeriodDto(5, true));

        await CertificationDatabase.DeliverAsync(aspire, ChangedType, Changed(started, "Employee", 2), cancellationToken);

        await ShouldBeRefusedOnBothRoutesAsync(developer, pharmacy, HttpStatusCode.Forbidden, "certification.capability-missing", cancellationToken);
    }

    [Fact]
    public async Task Someone_who_is_no_member_of_the_pharmacy_gets_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await aspire.CreateHostClientAsync("developer");

        await ShouldBeRefusedOnBothRoutesAsync(developer, Guid.CreateVersion7(), HttpStatusCode.NotFound, "certification.pharmacy-not-found", cancellationToken);
    }

    /// <summary>
    /// #422: access is checked before the period, so a non-member learns nothing from what they sent, whatever it is.
    /// </summary>
    [Theory]
    [InlineData("""{"years":31}""")]
    [InlineData("""{}""")]
    public async Task Someone_who_is_no_member_of_the_pharmacy_gets_404_whatever_the_period(string body)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await aspire.CreateHostClientAsync("developer");
        var pharmacy = Guid.CreateVersion7();

        using var response = await PutAsync(developer, pharmacy, body, cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.NotFound, "certification.pharmacy-not-found", cancellationToken);
    }

    /// <summary>#422: a member whose role lacks the capability is forbidden before the period is looked at.</summary>
    [Theory]
    [InlineData("""{"years":31}""")]
    [InlineData("""{}""")]
    public async Task An_employee_of_the_pharmacy_gets_403_whatever_the_period(string body)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await aspire.CreateHostClientAsync("developer");
        var pharmacy = Guid.CreateVersion7();
        await CertificationDatabase.DeliverAsync(aspire, StartedType, StartedIn(SubjectOf(developer), pharmacy, "Employee", LongAgo), cancellationToken);

        using var response = await PutAsync(developer, pharmacy, body, cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.Forbidden, "certification.capability-missing", cancellationToken);
    }

    /// <summary>#422: the empty Guid names no pharmacy; it is not found, whatever the period.</summary>
    [Theory]
    [InlineData("""{"years":7}""")]
    [InlineData("""{"years":31}""")]
    public async Task Setting_the_period_of_the_empty_pharmacy_id_gets_404(string body)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await aspire.CreateHostClientAsync("developer");

        using var response = await PutAsync(developer, Guid.Empty, body, cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.NotFound, "certification.pharmacy-not-found", cancellationToken);
    }

    [Fact]
    public async Task Without_a_token_it_answers_401()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = aspire.CreateHostClient();
        var pharmacy = Guid.CreateVersion7();

        using var get = await client.GetAsync(RetentionPeriod(pharmacy), cancellationToken);
        using var put = await PutAsync(client, pharmacy, """{"years":7}""", cancellationToken);

        get.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        put.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("""{"years":1}""")]
    [InlineData("""{"years":31}""")]
    [InlineData("""{"years":0}""")]
    [InlineData("""{}""")]
    public async Task A_period_outside_two_to_thirty_years_gets_400(string body)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await aspire.CreateHostClientAsync("developer");
        var pharmacy = await ManagedPharmacyAsync(aspire, developer, cancellationToken);

        using var response = await PutAsync(developer, pharmacy, body, cancellationToken);

        await ShouldBeRefusedAsync(response, HttpStatusCode.BadRequest, "certification.invalid-retention-period", cancellationToken);
        (await ReadAsync(developer, pharmacy, cancellationToken)).ShouldBe(new CertificationRetentionPeriodDto(5, true));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(30)]
    public async Task The_shortest_and_the_longest_period_are_accepted(int years)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await aspire.CreateHostClientAsync("developer");
        var pharmacy = await ManagedPharmacyAsync(aspire, developer, cancellationToken);

        using (var response = await PutAsync(developer, pharmacy, $$"""{"years":{{years}}}""", cancellationToken))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(cancellationToken));
        }

        (await ReadAsync(developer, pharmacy, cancellationToken)).ShouldBe(new CertificationRetentionPeriodDto(years, false));
    }

    /// <summary>
    /// Two managers setting the period at once both succeed, also the first time the pharmacy sets one: the upsert takes
    /// the second insert as an update, so neither gets a 409 or a 500, and the period read after is one of the two.
    /// </summary>
    [Fact]
    public async Task Two_periods_set_at_once_both_succeed_and_one_of_them_is_kept()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await aspire.CreateHostClientAsync("developer");

        for (var round = 0; round < 10; round++)
        {
            var pharmacy = await ManagedPharmacyAsync(aspire, developer, cancellationToken);

            var responses = await Task.WhenAll(
                PutAsync(developer, pharmacy, """{"years":10}""", cancellationToken),
                PutAsync(developer, pharmacy, """{"years":20}""", cancellationToken));
            try
            {
                foreach (var response in responses)
                {
                    response.StatusCode.ShouldBe(HttpStatusCode.NoContent, $"round {round}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
                }
            }
            finally
            {
                foreach (var response in responses)
                {
                    response.Dispose();
                }
            }

            var kept = await ReadAsync(developer, pharmacy, cancellationToken);
            kept.IsDefault.ShouldBeFalse();
            kept.Years.ShouldBeOneOf(10, 20);
        }
    }

    private static Uri RetentionPeriod(Guid pharmacy) => new($"/certification/pharmacies/{pharmacy:D}/retention-period", UriKind.Relative);

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, Guid pharmacy, string json, CancellationToken cancellationToken)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PutAsync(RetentionPeriod(pharmacy), content, cancellationToken);
    }

    /// <summary>The pharmacy's period as a 200 answers it.</summary>
    private static async Task<CertificationRetentionPeriodDto> ReadAsync(HttpClient client, Guid pharmacy, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(RetentionPeriod(pharmacy), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));
        return (await response.Content.ReadFromJsonAsync<CertificationRetentionPeriodDto>(cancellationToken)).ShouldNotBeNull();
    }

    private static async Task ShouldBeRefusedOnBothRoutesAsync(HttpClient client, Guid pharmacy, HttpStatusCode status, string code, CancellationToken cancellationToken)
    {
        using var get = await client.GetAsync(RetentionPeriod(pharmacy), cancellationToken);
        await ShouldBeRefusedAsync(get, status, code, cancellationToken);

        using var put = await PutAsync(client, pharmacy, """{"years":7}""", cancellationToken);
        await ShouldBeRefusedAsync(put, status, code, cancellationToken);
    }

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldBe(status, $"{response.RequestMessage?.Method} answered: {body}");
        (await CodeOfAsync(response, cancellationToken)).ShouldBe(code);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("title").GetString().ShouldBe(ErrorCodeCatalog.MessageOf(code));
    }
}
