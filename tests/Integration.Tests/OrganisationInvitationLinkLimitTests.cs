using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A mail request of an invitation whose version already holds <c>Pharmacy.MaxInvitationLinksPerVersion</c> links is
/// dead-lettered on its first attempt, from the durable local queue into the Organisation database's
/// <c>wolverine.wolverine_dead_letters</c>, where it stays replayable (docs/adr/0004). The test fills the version up to
/// the cap by hand, clears its mailed mark and hands the handled request back to the inbox, so it is delivered again.
/// </summary>
[Collection(InvitationSenders.Name)]
public class OrganisationInvitationLinkLimitTests(AspireFixture aspire)
{
    private const int MaxLinksPerVersion = 5;

    private static readonly Uri Pharmacies = new("/organisation/pharmacies", UriKind.Relative);

    // The durability agent recovers a handed-back envelope on its own schedule, every few seconds.
    private static readonly TimeSpan RedeliveryTimeout = TimeSpan.FromSeconds(60);

    // Once taken, the first attempt dead-letters at once; the retry ladder would take about a minute.
    private static readonly TimeSpan DeadLetterTimeout = TimeSpan.FromSeconds(10);

    private readonly OrganisationDatabase database = new(aspire);

    private readonly Mailpit mailpit = new(aspire, TimeSpan.FromSeconds(60));

    [Fact]
    public async Task A_mail_request_at_the_link_cap_is_dead_lettered_on_its_first_attempt_replayable_and_mails_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var address = Invitee();
        var membership = await InviteAsync(owner, pharmacy, address, cancellationToken);
        (await mailpit.MessagesToAsync(address, 1, cancellationToken)).ShouldHaveSingleItem();
        var request = await database.MailRequestEnvelopeAsync(membership, cancellationToken);

        for (var link = 1; link < MaxLinksPerVersion; link++)
        {
            await database.InsertInvitationLinkAsync(membership, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), cancellationToken);
        }

        (await database.TokenHashesAsync(membership, cancellationToken)).Count.ShouldBe(MaxLinksPerVersion);

        // Unmarked, the redelivered request tries to issue a sixth link instead of being skipped as mailed.
        await database.ClearInvitationMailedAsync(membership, cancellationToken);
        await database.RedeliverAsync(request.Id, cancellationToken);
        await database.WaitUntilTakenAsync(request.Id, RedeliveryTimeout, cancellationToken);

        var deadLetter = await database.MailRequestDeadLetterAsync(membership, DeadLetterTimeout, cancellationToken);
        TestContext.Current.TestOutputHelper?.WriteLine($"dead letter: {deadLetter.ExceptionType}, {deadLetter.Attempts} attempt(s): {deadLetter.ExceptionMessage}");

        deadLetter.Id.ShouldBe(request.Id, "the dead letter is not the redelivered request's envelope");
        deadLetter.ExceptionType.ShouldContain("InvitationLinkLimitReachedException", Case.Sensitive, deadLetter.Row);
        deadLetter.ExceptionMessage.ShouldContain("organisation.invitation-link-limit-reached", Case.Sensitive, deadLetter.Row);
        deadLetter.Attempts.ShouldBe(1, $"the request was retried before it was dead-lettered: {deadLetter.Row}");
        using (var row = JsonDocument.Parse(deadLetter.Row))
        {
            row.RootElement.TryGetProperty("replayable", out var replayable).ShouldBeTrue($"the dead letter has no replayable column: {deadLetter.Row}");
            replayable.ValueKind.ShouldBe(JsonValueKind.False, deadLetter.Row);
        }

        (await mailpit.CountToAsync(address, cancellationToken)).ShouldBe(1, "a mail went out for the request at the cap");
        (await database.TokenHashesAsync(membership, cancellationToken)).Count.ShouldBe(MaxLinksPerVersion, "the request at the cap changed the version's links");
    }

    /// <summary>A fresh address per test, so no test sees another's invitation or mail.</summary>
    private static string Invitee() => $"invitee-{Guid.CreateVersion7()}@example.com";

    private static async Task<Guid> RegisterAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var created = await client.PostAsJsonAsync(Pharmacies, new { name = $"Apotheke {Guid.CreateVersion7()}", jobFunction = "Pharmacist" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static async Task<Guid> InviteAsync(HttpClient client, Guid pharmacy, string address, CancellationToken cancellationToken)
    {
        using var invited = await client.PostAsJsonAsync(new Uri($"/organisation/pharmacies/{pharmacy}/invitations", UriKind.Relative), new { email = address, jobFunction = "Pta", role = "Employee" }, cancellationToken);
        invited.StatusCode.ShouldBe(HttpStatusCode.Created, await invited.Content.ReadAsStringAsync(cancellationToken));
        return await invited.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }
}
