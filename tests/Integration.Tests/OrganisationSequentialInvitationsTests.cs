using System.Net;
using System.Net.Http.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Two invitations of one pharmacy, one after the other (#405). The first one's mail consumer issues its link and records
/// its mail on the membership's own row version, not the pharmacy's (docs/adr/0037), so it never makes a second invitation
/// that loaded the pharmacy before it lose a race nobody ran.
/// </summary>
[Collection(InvitationSenders.Name)]
public class OrganisationSequentialInvitationsTests(AspireFixture aspire)
{
    // The mail consumer may run before the pharmacy's row is first read; such a run shows nothing and is started again.
    private const int Attempts = 5;

    private static readonly TimeSpan MailWait = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly OrganisationDatabase database = new(aspire);

    [Fact]
    public async Task Mailing_an_invitation_leaves_the_pharmacys_row_alone_and_the_next_invitation_is_created()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var pharmacy = await RegisterAsync(owner, cancellationToken);
            var first = await InviteAsync(owner, pharmacy, cancellationToken);
            var beforeTheMail = await database.PharmacyRowVersionAsync(pharmacy, cancellationToken);
            if (await database.InvitationMailedForAsync(first, cancellationToken) is not null)
            {
                // Read after the consumer committed, the row version could not tell whether the consumer moved it.
                TestContext.Current.TestOutputHelper?.WriteLine($"Attempt {attempt}: the mail was recorded before the pharmacy's row was read; starting again.");
                continue;
            }

            await WaitUntilMailedAsync(first, cancellationToken);

            (await database.PharmacyRowVersionAsync(pharmacy, cancellationToken))
                .ShouldBe(beforeTheMail, "the first invitation's mail consumer updated the pharmacy's row");
            var second = await InviteAsync(owner, pharmacy, cancellationToken);
            await WaitUntilMailedAsync(second, cancellationToken);
            return;
        }

        throw new InvalidOperationException($"In each of {Attempts} attempts the mail was recorded before the pharmacy's row was read, so the test observed nothing.");
    }

    private static async Task<Guid> RegisterAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var registered = await client.PostAsJsonAsync(
            new Uri("/organisation/pharmacies", UriKind.Relative),
            new { name = $"Apotheke {Guid.CreateVersion7()}", jobFunction = "Pharmacist" },
            cancellationToken);
        registered.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await registered.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static async Task<Guid> InviteAsync(HttpClient client, Guid pharmacy, CancellationToken cancellationToken)
    {
        using var invited = await client.PostAsJsonAsync(
            new Uri($"/organisation/pharmacies/{pharmacy}/invitations", UriKind.Relative),
            new { email = $"invitee-{Guid.CreateVersion7()}@example.com", jobFunction = "Pta", role = "Employee" },
            cancellationToken);
        var body = await invited.Content.ReadAsStringAsync(cancellationToken);
        invited.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return await invited.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private async Task WaitUntilMailedAsync(Guid membership, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (await database.InvitationMailedForAsync(membership, cancellationToken) is null)
        {
            TimeProvider.System.GetElapsedTime(started).ShouldBeLessThan(MailWait, $"the invitation of membership {membership} was not mailed");
            await Task.Delay(PollInterval, cancellationToken);
        }
    }
}
