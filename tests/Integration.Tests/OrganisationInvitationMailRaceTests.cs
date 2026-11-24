using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A link the mail consumer writes while a renewal of the same membership saves: one of the two loses its race, the way
/// any two writers of one pharmacy do, never with a 500 (#193's review, follow-up 5). The interleaving is forced, not
/// hoped for: the test holds the pharmacy's rows, hands the invitation's handled mail request back to the inbox so it is
/// delivered again, waits until that run is blocked at its link's commit, starts the renewal, waits until it is blocked
/// behind the same rows, and only then lets both go on. Which of the two wins is up to PostgreSQL, so the end state is
/// checked for either: the renewal's version's links alone, or the first version's links with the renewal refused as a
/// lost race.
/// </summary>
public partial class OrganisationInvitationMailRaceTests(AspireFixture aspire)
{
    private const string OrganisationService = "organisation";

    private static readonly Uri Pharmacies = new("/organisation/pharmacies", UriKind.Relative);

    // The durability agent recovers a handed-back envelope on its own schedule, every few seconds.
    private static readonly TimeSpan RedeliveryTimeout = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(60);

    private readonly OrganisationDatabase database = new(aspire);

    private readonly Mailpit mailpit = new(aspire, TimeSpan.FromSeconds(60));

    [Fact]
    public async Task A_link_the_mail_consumer_writes_while_a_renewal_saves_loses_or_wins_a_race_never_a_500_and_leaves_one_versions_links()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var address = Invitee();
        var membership = await InviteAsync(owner, pharmacy, address, cancellationToken);
        var first = HashOf(TokenIn((await mailpit.MessagesToAsync(address, 1, cancellationToken)).ShouldHaveSingleItem()));
        var request = await database.MailRequestEnvelopeAsync(membership, cancellationToken);

        // Unmarked, the redelivered request issues a link for the first version again instead of being skipped as mailed.
        await database.ClearInvitationMailedAsync(membership, cancellationToken);

        // Past the renew cooldown (docs/adr/0004), so the renewal reaches the pharmacy's rows instead of being refused first.
        await database.MoveInvitationRequestBackAsync(membership, TimeSpan.FromMinutes(10), cancellationToken);
        await using var log = await ServiceLog.WatchAsync(aspire, OrganisationService, cancellationToken);

        HttpResponseMessage renewed;
        await using (var gate = await database.LockPharmacyAsync(pharmacy, cancellationToken))
        {
            await database.RedeliverAsync(request.Id, cancellationToken);
            await database.WaitUntilBlockedAsync(gate, 1, RedeliveryTimeout, cancellationToken);
            var renew = owner.PostAsync(RenewUri(pharmacy, membership), content: null, cancellationToken);
            await database.WaitUntilBlockedAsync(gate, 2, cancellationToken);
            await gate.CommitAsync(cancellationToken);
            renewed = await renew;
        }

        using (renewed)
        {
            var body = await renewed.Content.ReadAsStringAsync(cancellationToken);
            renewed.StatusCode.ShouldBeOneOf([HttpStatusCode.OK, HttpStatusCode.Conflict], $"{(int)renewed.StatusCode} {body}");
            if (renewed.StatusCode == HttpStatusCode.Conflict)
            {
                JsonSerializer.Deserialize<Problem>(body, JsonSerializerOptions.Web)?.Code.ShouldBe("organisation.pharmacy-changed", body);
            }

            var (stored, mailed) = await SettledAsync(membership, address, cancellationToken);
            TestContext.Current.TestOutputHelper?.WriteLine($"renewal answered {(int)renewed.StatusCode}; {stored.Count} link(s) stored, {mailed.Count} mail(s) sent");
            (await database.MembershipAsync(membership, cancellationToken)).Status.ShouldBe("Invited");
            stored.ShouldNotBeEmpty("the invitation keeps no link");
            stored.ShouldBeSubsetOf(mailed, $"a stored link was never mailed; stored: {string.Join(", ", stored)}; mailed: {string.Join(", ", mailed)}");
            if (renewed.StatusCode == HttpStatusCode.OK)
            {
                stored.ShouldNotContain(first, "the renewal won, yet the first version's link still opens the invitation");
            }
            else
            {
                stored.ShouldContain(first, "the renewal lost, yet the first version's link is gone");
            }
        }

        (await database.MailRequestDeadLetterReasonAsync(membership, cancellationToken)).ShouldBeNull("a mail request of the membership was dead-lettered");
        var failures = Unhandled(log.Lines());
        failures.ShouldBeEmpty($"the Organisation service logged an unhandled exception:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    /// <summary>
    /// The lines of a request the host answered with a 500, or of a write PostgreSQL refused for anything other than the
    /// row version that a lost race moved on: a foreign key, a unique index or a check. The host logs a request it could
    /// not bind (<c>BadHttpRequestException</c>, answered 400) with the same header; other tests send such requests on
    /// purpose while this one watches the shared service, so those are not failures.
    /// </summary>
    private static List<string> Unhandled(List<string> lines)
    {
        List<string> failures = [];
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.Contains("PostgresException", StringComparison.Ordinal))
            {
                failures.Add(line);
            }
            else if (line.Contains("An unhandled exception has occurred", StringComparison.Ordinal)
                && !(index + 1 < lines.Count && lines[index + 1].Contains("BadHttpRequestException", StringComparison.Ordinal)))
            {
                failures.Add(line);
            }
        }

        return failures;
    }

    /// <summary>
    /// Waits until every link the membership keeps has gone out in a mail to the address, so no run is still between
    /// its link's commit and its send, and answers the stored hashes and the hashes of every mail's token.
    /// </summary>
    private async Task<(List<string> Stored, List<string> Mailed)> SettledAsync(Guid membership, string address, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            var stored = await database.TokenHashesAsync(membership, cancellationToken);
            List<string> mailed = [.. (await mailpit.MessagesToAsync(address, 1, cancellationToken)).Select(mail => HashOf(TokenIn(mail)))];
            if ((stored.Count > 0 && stored.TrueForAll(mailed.Contains)) || TimeProvider.System.GetElapsedTime(started) >= SettleTimeout)
            {
                return (stored, mailed);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
    }

    private static string Invitee() => $"invitee-{Guid.CreateVersion7()}@example.com";

    private static Uri InvitationsUri(Guid pharmacy) => new($"/organisation/pharmacies/{pharmacy}/invitations", UriKind.Relative);

    private static Uri RenewUri(Guid pharmacy, Guid membership) => new($"/organisation/pharmacies/{pharmacy}/invitations/{membership}/renew", UriKind.Relative);

    /// <summary>The SHA-256 of the token in lower-case hex, as the row keeps it.</summary>
    private static string HashOf(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static async Task<Guid> RegisterAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var created = await client.PostAsJsonAsync(Pharmacies, new { name = $"Apotheke {Guid.CreateVersion7()}", jobFunction = "Pharmacist" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static async Task<Guid> InviteAsync(HttpClient client, Guid pharmacy, string address, CancellationToken cancellationToken)
    {
        using var invited = await client.PostAsJsonAsync(InvitationsUri(pharmacy), new { email = address, jobFunction = "Pta", role = "Employee" }, cancellationToken);
        invited.StatusCode.ShouldBe(HttpStatusCode.Created, await invited.Content.ReadAsStringAsync(cancellationToken));
        return await invited.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    /// <summary>The token from the mail's link, which leads to the web app's accept page with the token in the fragment.</summary>
    private static string TokenIn(Mailpit.CaughtMail mail)
    {
        var match = LinkToken().Match(mail.Text);
        match.Success.ShouldBeTrue($"the mail holds no link with a token: {mail.Text}");
        return match.Groups["token"].Value;
    }

    [GeneratedRegex("#token=(?<token>[A-Za-z0-9_-]{43})")]
    private static partial Regex LinkToken();

    private sealed record Problem(string? Code);

    /// <summary>
    /// The lines a service's console log gains while the test runs: what it logged before the watch started is read and
    /// dropped first, so another test's failure earlier in the run does not count.
    /// </summary>
    private sealed class ServiceLog : IAsyncDisposable
    {
        private readonly List<string> lines = [];
        private readonly CancellationTokenSource stop;
        private Task watching = Task.CompletedTask;
        private bool recording;

        private ServiceLog(CancellationToken cancellationToken) => stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        public static async Task<ServiceLog> WatchAsync(AspireFixture aspire, string resource, CancellationToken cancellationToken)
        {
            var logs = aspire.App.Services.GetRequiredService<ResourceLoggerService>();
            var service = aspire.App.Services.GetRequiredService<DistributedApplicationModel>().Resources.Single(candidate => candidate.Name == resource);
            var log = new ServiceLog(cancellationToken);
            var lastBatch = TimeProvider.System.GetTimestamp();
            log.watching = Task.Run(
                async () =>
                {
                    try
                    {
                        await foreach (var batch in logs.WatchAsync(service).WithCancellation(log.stop.Token))
                        {
                            lock (log.lines)
                            {
                                lastBatch = TimeProvider.System.GetTimestamp();
                                if (log.recording)
                                {
                                    log.lines.AddRange(batch.Select(line => line.Content));
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // The watch is over.
                    }
                },
                CancellationToken.None);

            // The backlog arrives at once; recording starts once it has stopped coming for a second.
            while (true)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                lock (log.lines)
                {
                    if (TimeProvider.System.GetElapsedTime(lastBatch) >= TimeSpan.FromSeconds(1))
                    {
                        log.recording = true;
                        return log;
                    }
                }
            }
        }

        public List<string> Lines()
        {
            lock (lines)
            {
                return [.. lines];
            }
        }

        public async ValueTask DisposeAsync()
        {
            await stop.CancelAsync();
            await watching;
            stop.Dispose();
        }
    }
}
