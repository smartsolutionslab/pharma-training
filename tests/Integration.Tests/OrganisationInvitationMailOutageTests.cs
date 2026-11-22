using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The invitation mail survives an SMTP outage: a send that fails is retried and arrives once Mailpit is back, and one
/// that fails on every attempt is dead-lettered without personal data and arrives when it is replayed.
/// </summary>
[Collection(nameof(MailOutage))]
public partial class OrganisationInvitationMailOutageTests(AspireFixture aspire)
{
    private const string MailpitResource = "mailpit";

    // Seven attempts, the waits doubling from 1 to 32 seconds, take about a minute before the dead letter.
    private static readonly TimeSpan DeadLetterTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan RetryTimeout = TimeSpan.FromSeconds(60);

    private readonly OrganisationDatabase database = new(aspire);

    private readonly Mailpit mailpit = new(aspire, TimeSpan.FromSeconds(60));

    [Fact]
    public async Task A_mail_that_fails_while_smtp_is_down_is_retried_and_arrives_when_it_is_back()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var (pharmacy, _) = await RegisterAsync(owner, cancellationToken);
        var address = Invitee();

        await StopMailpitAsync(cancellationToken);
        try
        {
            var membership = await InviteAsync(owner, pharmacy, address, cancellationToken);
            await database.MailRequestEnvelopeAsync(membership, minimumAttempts: 2, RetryTimeout, cancellationToken);
            await StartMailpitAsync(cancellationToken);

            var mail = (await mailpit.MessagesToAsync(address, 1, cancellationToken)).ShouldHaveSingleItem();
            (await database.TokenHashAsync(membership, cancellationToken)).ShouldBe(HashOf(TokenIn(mail)));
        }
        finally
        {
            await StartMailpitAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_mail_that_fails_on_every_attempt_is_dead_lettered_without_personal_data_and_arrives_when_replayed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var (pharmacy, name) = await RegisterAsync(owner, cancellationToken);
        var address = Invitee();

        await StopMailpitAsync(cancellationToken);
        try
        {
            var membership = await InviteAsync(owner, pharmacy, address, cancellationToken);

            var deadLetter = await database.MailRequestDeadLetterAsync(membership, DeadLetterTimeout, cancellationToken);

            using (var body = JsonDocument.Parse(deadLetter.Body))
            {
                body.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(["membershipId", "metadata", "pharmacyId"], ignoreOrder: true, deadLetter.Body);
            }

            deadLetter.Row.ShouldNotContain(address, Case.Insensitive, "the dead letter holds the invited address");
            deadLetter.Row.ShouldNotContain(name, Case.Insensitive, "the dead letter holds the pharmacy's name");
            deadLetter.ExceptionMessage.ShouldNotContain("@", Case.Sensitive, $"the dead letter's exception names an address: {deadLetter.ExceptionMessage}");

            await StartMailpitAsync(cancellationToken);
            await database.ReplayAsync(deadLetter.Id, cancellationToken);

            var mail = (await mailpit.MessagesToAsync(address, 1, cancellationToken)).ShouldHaveSingleItem();
            (await database.TokenHashAsync(membership, cancellationToken)).ShouldBe(HashOf(TokenIn(mail)));
        }
        finally
        {
            await StartMailpitAsync(CancellationToken.None);
        }
    }

    /// <summary>A fresh address per test, so no test sees another's invitation or mail.</summary>
    private static string Invitee() => $"invitee-{Guid.CreateVersion7()}@example.com";

    /// <summary>The SHA-256 of the token in lower-case hex, as the row keeps it.</summary>
    private static string HashOf(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Invites the address as an employee through the API and answers the new membership's id; waits for no mail.</summary>
    private static async Task<Guid> InviteAsync(HttpClient client, Guid pharmacy, string address, CancellationToken cancellationToken)
    {
        using var invited = await client.PostAsJsonAsync(new Uri($"/organisation/pharmacies/{pharmacy}/invitations", UriKind.Relative), new { email = address, jobFunction = "Pta", role = "Employee" }, cancellationToken);
        invited.StatusCode.ShouldBe(HttpStatusCode.Created, await invited.Content.ReadAsStringAsync(cancellationToken));
        return await invited.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    /// <summary>Registers a pharmacy with a fresh name and answers its id and name.</summary>
    private static async Task<(Guid Id, string Name)> RegisterAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var name = $"Apotheke {Guid.CreateVersion7()}";
        using var created = await client.PostAsJsonAsync(new Uri("/organisation/pharmacies", UriKind.Relative), new { name, jobFunction = "Pharmacist" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await created.Content.ReadFromJsonAsync<Guid>(cancellationToken), name);
    }

    [GeneratedRegex("#token=(?<token>[A-Za-z0-9_-]{43})$")]
    private static partial Regex LinkToken();

    /// <summary>The token from the mail's link to the web app's accept page.</summary>
    private static string TokenIn(Mailpit.CaughtMail mail)
    {
        var line = mail.Text.ReplaceLineEndings("\n").Split('\n').Select(candidate => candidate.Trim()).SingleOrDefault(candidate => candidate.Contains("#token=", StringComparison.Ordinal));
        line.ShouldNotBeNull($"the mail holds no link with a token: {mail.Text}");
        var match = LinkToken().Match(line);
        match.Success.ShouldBeTrue($"the link's token is not 43 URL-safe characters: {line}");
        return match.Groups["token"].Value;
    }

    private async Task StopMailpitAsync(CancellationToken cancellationToken)
    {
        var stopped = await aspire.App.ResourceCommands.ExecuteCommandAsync(MailpitResource, KnownResourceCommands.StopCommand, cancellationToken);
        stopped.Success.ShouldBeTrue($"Mailpit did not stop: {stopped.Message}");
    }

    /// <summary>Starts Mailpit, unless it runs already, and waits until it is healthy.</summary>
    private async Task StartMailpitAsync(CancellationToken cancellationToken)
    {
        await aspire.App.ResourceCommands.ExecuteCommandAsync(MailpitResource, KnownResourceCommands.StartCommand, cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        await aspire.App.ResourceNotifications.WaitForResourceHealthyAsync(MailpitResource, timeout.Token);
    }
}
