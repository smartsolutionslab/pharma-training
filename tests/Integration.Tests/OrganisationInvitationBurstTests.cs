using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Many invitations of one pharmacy at once. Each mail request's consumer commits a token hash on the pharmacy, which
/// moves its row version, so the consumers race one another; every invitee must still get exactly one mail, and no
/// request may be dead-lettered for having lost those races.
/// </summary>
public class OrganisationInvitationBurstTests(AspireFixture aspire)
{
    private const int Invitations = 10;

    private const int InviteAttempts = 20;

    // Seven attempts take about 63 s of retry waits (1 + 2 + … + 32); the wait outlasts that, so a request that gives up
    // is seen dead-lettered rather than merely late.
    private static readonly TimeSpan MailWait = TimeSpan.FromMinutes(3);

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private static readonly Uri Pharmacies = new("/organisation/pharmacies", UriKind.Relative);

    private readonly OrganisationDatabase database = new(aspire);

    private readonly Mailpit mailpit = new(aspire);

    [Fact]
    public async Task Ten_invitations_of_one_pharmacy_at_once_each_mail_their_invitee_once_and_none_is_dead_lettered()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        List<string> addresses = [.. Enumerable.Range(0, Invitations).Select(_ => $"invitee-{Guid.CreateVersion7()}@example.com")];

        var memberships = await Task.WhenAll(addresses.Select(address => InviteAsync(owner, pharmacy, address, cancellationToken)));

        var invited = addresses.Zip(memberships, (address, membership) => (Address: address, Membership: membership)).ToList();
        var settled = await WaitUntilEveryRequestIsMailedOrDeadLetteredAsync(invited, cancellationToken);
        var deadLettered = settled.Where(outcome => outcome.DeadLetter is not null).Select(outcome => $"{outcome.Membership}: {outcome.DeadLetter}").ToList();
        deadLettered.ShouldBeEmpty($"{deadLettered.Count} of {Invitations} mail requests were dead-lettered with no SMTP fault");
        var unmailed = settled.Where(outcome => outcome.Mails == 0).Select(outcome => outcome.Address).ToList();
        unmailed.ShouldBeEmpty($"{unmailed.Count} of {Invitations} invitees got no mail within {MailWait.TotalSeconds:0} s");
        var counts = settled.Select(outcome => outcome.Mails).ToList();
        counts.ShouldAllBe(count => count == 1, $"each invitee must get exactly one mail; got {string.Join(", ", counts)}");
    }

    /// <summary>
    /// Polls Mailpit and the dead letters until every invitation's request is either mailed or dead-lettered, or the
    /// wait is over, and answers what each came to.
    /// </summary>
    private async Task<List<Outcome>> WaitUntilEveryRequestIsMailedOrDeadLetteredAsync(List<(string Address, Guid Membership)> invited, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            List<Outcome> outcomes = [];
            foreach (var (address, membership) in invited)
            {
                var mails = await mailpit.CountToAsync(address, cancellationToken);
                var deadLetter = await database.MailRequestDeadLetterReasonAsync(membership, cancellationToken);
                outcomes.Add(new Outcome(address, membership, mails, deadLetter));
            }

            if (outcomes.TrueForAll(outcome => outcome.Mails > 0 || outcome.DeadLetter is not null)
                || TimeProvider.System.GetElapsedTime(started) >= MailWait)
            {
                return outcomes;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    /// <summary>
    /// Invites the address as an employee and answers the membership. The invitations race one another on the
    /// pharmacy's row version, and so do the mail consumers' commits; an invite that loses such a race is told
    /// 409 organisation.pharmacy-changed and is asked again, as a manager would.
    /// </summary>
    private static async Task<Guid> InviteAsync(HttpClient client, Guid pharmacy, string address, CancellationToken cancellationToken)
    {
        List<string> refusals = [];
        for (var attempt = 0; attempt < InviteAttempts; attempt++)
        {
            using var invited = await client.PostAsJsonAsync(
                new Uri($"/organisation/pharmacies/{pharmacy}/invitations", UriKind.Relative),
                new { email = address, jobFunction = "Pta", role = "Employee" },
                cancellationToken);
            var body = await invited.Content.ReadAsStringAsync(cancellationToken);
            if (invited.StatusCode == HttpStatusCode.Created)
            {
                return JsonSerializer.Deserialize<Guid>(body, JsonSerializerOptions.Web);
            }

            var code = string.IsNullOrWhiteSpace(body) ? null : JsonSerializer.Deserialize<Problem>(body, JsonSerializerOptions.Web)?.Code;
            (invited.StatusCode, code).ShouldBe((HttpStatusCode.Conflict, "organisation.pharmacy-changed"), $"{(int)invited.StatusCode} with body: {body}");
            refusals.Add(code ?? string.Empty);
        }

        throw new InvalidOperationException($"The invitation of {address} lost {refusals.Count} races in a row.");
    }

    private static async Task<Guid> RegisterAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var created = await client.PostAsJsonAsync(Pharmacies, new { name = $"Apotheke {Guid.CreateVersion7()}", jobFunction = "Pharmacist" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private sealed record Outcome(string Address, Guid Membership, int Mails, string? DeadLetter);

    private sealed record Problem(string? Code);
}
