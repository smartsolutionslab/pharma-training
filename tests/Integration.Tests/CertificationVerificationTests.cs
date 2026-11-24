using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Public verification through the edge (#51, Z2): anyone holding a certificate's code, without a token, sees the
/// holder's name, the course title, the day of issue, the last valid day and whether it is valid today, and nothing
/// else. Every code that finds no certificate gets one and the same 404, and an address over the limit gets 429 with
/// <c>Retry-After</c>. No answer may be cached. The run lowers the limit to
/// <see cref="AspireFixture.CertificationVerificationLimit"/> a minute; all local callers share one address, so these
/// tests run in a collection of their own and leave the minute's window reset.
/// </summary>
[Collection(CertificationVerification.Name)]
public class CertificationVerificationTests(AspireFixture aspire)
{
    private const string Unknown = "k7Qp2xV9mL4rT8wZ1nB6cY3hAbCdEfGh";

    [Fact]
    public async Task An_anonymous_caller_sees_exactly_what_the_certificate_shows_and_nothing_is_cached()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var passedAt = TimeProvider.System.GetUtcNow().AddDays(-1);
        var certificate = await CertificationIssuing.IssueAsync(aspire, 12, passedAt, cancellationToken);
        using var anonymous = aspire.CreateHostClient();

        using var response = await anonymous.GetAsync(Verify(certificate.VerificationCode), cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        NoStore(response).ShouldBeTrue("a verification is never cached");
        var json = JsonNode.Parse(body).ShouldNotBeNull().AsObject();
        json.Select(property => property.Key).ShouldBe(["holderName", "courseTitle", "issuedOn", "validUntil", "validity"], ignoreOrder: true);
        json["holderName"]!.GetValue<string>().ShouldBe(certificate.HolderName);
        json["courseTitle"]!.GetValue<string>().ShouldBe(certificate.CourseTitle);
        json["issuedOn"]!.GetValue<string>().ShouldBe(GermanDay(certificate.IssuedAt));
        json["validUntil"]!.GetValue<string>().ShouldBe(certificate.ValidUntil.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        json["validity"]!.GetValue<string>().ShouldBe("Valid");
        body.ShouldNotContain(certificate.VerificationCode);
        body.ShouldNotContain(certificate.Id.ToString());
    }

    [Fact]
    public async Task A_certificate_past_its_last_valid_day_is_shown_expired()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var certificate = await CertificationIssuing.IssueAsync(aspire, 1, new DateTimeOffset(2020, 1, 15, 9, 0, 0, TimeSpan.Zero), cancellationToken);
        using var anonymous = aspire.CreateHostClient();

        using var response = await anonymous.GetAsync(Verify(certificate.VerificationCode), cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var json = JsonNode.Parse(body).ShouldNotBeNull();
        json["issuedOn"]!.GetValue<string>().ShouldBe("2020-01-15");
        json["validUntil"]!.GetValue<string>().ShouldBe("2020-02-15");
        json["validity"]!.GetValue<string>().ShouldBe("Expired");
    }

    [Fact]
    public async Task An_unknown_code_a_certificates_id_and_malformed_codes_get_one_and_the_same_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var certificate = await CertificationIssuing.IssueAsync(aspire, 12, TimeProvider.System.GetUtcNow().AddDays(-1), cancellationToken);
        using var anonymous = aspire.CreateHostClient();
        string[] codes = [Unknown, certificate.Id.ToString(), "abc", new string('k', 65)];

        List<JsonObject> bodies = [];
        foreach (var code in codes)
        {
            using var response = await anonymous.GetAsync(Verify(code), cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, $"{code}: {body}");
            NoStore(response).ShouldBeTrue($"{code} is never cached");
            var problem = JsonNode.Parse(body).ShouldNotBeNull().AsObject();
            problem["code"]!.GetValue<string>().ShouldBe("certification.certificate-not-verifiable");
            problem["title"]!.GetValue<string>().ShouldBe("No certificate matches this code.");

            // The trace id is random hex, so it may contain a short code such as "abc" by chance; the code must not be
            // echoed anywhere else in the answer.
            problem.Remove("traceId");
            problem.ToJsonString().ShouldNotContain(code);
            bodies.Add(problem);
        }

        foreach (var body in bodies.Skip(1))
        {
            JsonNode.DeepEquals(body, bodies[0]).ShouldBeTrue($"{body.ToJsonString()} differs from {bodies[0].ToJsonString()}");
        }
    }

    [Fact]
    public async Task A_signed_in_caller_sees_the_same_answer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var certificate = await CertificationIssuing.IssueAsync(aspire, 12, TimeProvider.System.GetUtcNow().AddDays(-1), cancellationToken);
        using var anonymous = aspire.CreateHostClient();
        using var developer = await aspire.CreateHostClientAsync("developer");

        using var anonymousResponse = await anonymous.GetAsync(Verify(certificate.VerificationCode), cancellationToken);
        using var signedInResponse = await developer.GetAsync(Verify(certificate.VerificationCode), cancellationToken);

        var anonymousBody = await anonymousResponse.Content.ReadAsStringAsync(cancellationToken);
        var signedInBody = await signedInResponse.Content.ReadAsStringAsync(cancellationToken);
        anonymousResponse.StatusCode.ShouldBe(HttpStatusCode.OK, anonymousBody);
        signedInResponse.StatusCode.ShouldBe(HttpStatusCode.OK, signedInBody);
        JsonNode.DeepEquals(JsonNode.Parse(signedInBody), JsonNode.Parse(anonymousBody)).ShouldBeTrue($"{signedInBody} differs from {anonymousBody}");
    }

    /// <summary>
    /// Spends what is left of the minute's limit, whatever the collection's other tests used of it, and waits out the
    /// refusal, so the window is reset when it ends. Costs up to a minute.
    /// </summary>
    [Fact]
    public async Task An_address_over_the_limit_gets_429_with_retry_after_until_the_window_ends()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var certificate = await CertificationIssuing.IssueAsync(aspire, 12, TimeProvider.System.GetUtcNow().AddDays(-1), cancellationToken);
        using var anonymous = aspire.CreateHostClient();

        TimeSpan? retryAfter = null;
        for (var call = 1; call <= AspireFixture.CertificationVerificationLimit + 1 && retryAfter is null; call++)
        {
            using var response = await anonymous.GetAsync(Verify(certificate.VerificationCode), cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                NoStore(response).ShouldBeTrue("a refusal is never cached");
                JsonNode.Parse(body).ShouldNotBeNull()["code"]!.GetValue<string>().ShouldBe("certification.verification-limit-reached");
                retryAfter = response.Headers.RetryAfter?.Delta;
                retryAfter.ShouldNotBeNull("Retry-After in seconds");
            }
            else
            {
                response.StatusCode.ShouldBe(HttpStatusCode.OK, $"call {call}: {body}");
            }
        }

        retryAfter.ShouldNotBeNull($"a 429 within {AspireFixture.CertificationVerificationLimit + 1} calls");
        retryAfter.Value.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
        retryAfter.Value.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(60));
        using (var again = await anonymous.GetAsync(Verify(certificate.VerificationCode), cancellationToken))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests, "the window has not ended");
        }

        await Task.Delay(retryAfter.Value, cancellationToken);

        using var served = await anonymous.GetAsync(Verify(certificate.VerificationCode), cancellationToken);
        served.StatusCode.ShouldBe(HttpStatusCode.OK, await served.Content.ReadAsStringAsync(cancellationToken));
    }

    private static Uri Verify(string code) => new($"/certification/verify/{Uri.EscapeDataString(code)}", UriKind.Relative);

    private static string GermanDay(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")).DateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool NoStore(HttpResponseMessage response) => response.Headers.CacheControl?.NoStore == true;
}
