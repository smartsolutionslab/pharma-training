using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Invitation mail is also limited per address and per sender, across pharmacies, in Organisation's database (#265,
/// docs/adr/0004): an address is mailed at most once per 10 minutes, whichever pharmacy or invitation the mail is for,
/// so a revoke and re-invite waits too; a signed-in user sends at most so many invitation mails per UTC day, across
/// every pharmacy they manage (<see cref="AspireFixture.InvitationMailsPerSenderPerDay"/> in this run). Over a limit,
/// invite and renew answer 429 with the limit's code, a <c>Retry-After</c> and <c>retryAt</c>, and nothing is stored,
/// counted or mailed. The tests travel in time by moving the stored instants and days back, never by waiting. The
/// sender's cap is reached as <c>editor-b</c>, a user no other test invites as, so the tally the tests seed refuses no
/// other test's invitations.
/// </summary>
[Collection(InvitationSenders.Name)]
public class OrganisationMailRecipientAndSenderLimitsTests(AspireFixture aspire)
{
    private const string RecipientMailedRecently = "organisation.recipient-mailed-recently";

    private const string TooManySentToday = "organisation.too-many-invitations-sent-today";

    private const string Sender = "editor-b";

    private static readonly Uri Pharmacies = new("/organisation/pharmacies", UriKind.Relative);

    private readonly OrganisationDatabase database = new(aspire);

    private readonly Mailpit mailpit = new(aspire);

    [Fact]
    public async Task Revoking_and_inviting_an_address_again_within_ten_minutes_is_429_recipient_mailed_recently_and_once_they_have_passed_is_mailed_again()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var address = Invitee();
        var membership = await InviteAsync(owner, pharmacy, address, cancellationToken);
        (await mailpit.MessagesToAsync(address, 1, cancellationToken)).ShouldHaveSingleItem();
        using (var revoked = await owner.DeleteAsync(InvitationUri(pharmacy, membership), cancellationToken))
        {
            revoked.StatusCode.ShouldBe(HttpStatusCode.OK, await revoked.Content.ReadAsStringAsync(cancellationToken));
        }

        using (var atOnce = await owner.PostAsJsonAsync(InvitationsUri(pharmacy), Invitation(address), cancellationToken))
        {
            await ShouldBeRefusedLaterAsync(atOnce, RecipientMailedRecently, TimeSpan.FromMinutes(10), cancellationToken);
        }

        (await mailpit.CountToAsync(address, cancellationToken)).ShouldBe(1, "the refused re-invite sent a mail");
        await database.MoveRecipientMailBackAsync(address, TimeSpan.FromMinutes(9), cancellationToken);

        using (var aMinuteBefore = await owner.PostAsJsonAsync(InvitationsUri(pharmacy), Invitation(address), cancellationToken))
        {
            await ShouldBeRefusedLaterAsync(aMinuteBefore, RecipientMailedRecently, TimeSpan.FromMinutes(1), cancellationToken);
        }

        await database.MoveRecipientMailBackAsync(address, TimeSpan.FromMinutes(1), cancellationToken);

        await InviteAsync(owner, pharmacy, address, cancellationToken);
        (await mailpit.MessagesToAsync(address, 2, cancellationToken)).Count.ShouldBe(2, "the refused re-invites sent a mail besides the two invites'");
        (await database.StatusesOfAsync(pharmacy, address, cancellationToken)).ShouldBe(["Revoked", "Invited"]);
    }

    [Fact]
    public async Task An_address_mailed_by_one_pharmacy_is_429_recipient_mailed_recently_for_another_pharmacy_while_another_address_is_invited()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var first = await RegisterAsync(owner, cancellationToken);
        var second = await RegisterAsync(owner, cancellationToken);
        var address = Invitee();
        var another = Invitee();
        await InviteAsync(owner, first, address, cancellationToken);

        using (var elsewhere = await owner.PostAsJsonAsync(InvitationsUri(second), Invitation(address), cancellationToken))
        {
            await ShouldBeRefusedLaterAsync(elsewhere, RecipientMailedRecently, TimeSpan.FromMinutes(10), cancellationToken);
        }

        await InviteAsync(owner, second, another, cancellationToken);
        (await database.StatusesOfAsync(second, address, cancellationToken)).ShouldBeEmpty("the refused invitation was stored");
        (await mailpit.MessagesToAsync(another, 1, cancellationToken)).ShouldHaveSingleItem();
        (await mailpit.CountToAsync(address, cancellationToken)).ShouldBe(1, "the second pharmacy's refused invitation sent a mail");
    }

    [Fact]
    public async Task Renewing_another_invitation_of_an_address_mailed_moments_ago_is_429_recipient_mailed_recently()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var first = await RegisterAsync(owner, cancellationToken);
        var second = await RegisterAsync(owner, cancellationToken);
        var address = Invitee();
        var membership = await InviteAsync(owner, first, address, cancellationToken);
        (await mailpit.MessagesToAsync(address, 1, cancellationToken)).ShouldHaveSingleItem();
        await database.MoveInvitationRequestBackAsync(membership, TimeSpan.FromMinutes(10), cancellationToken);
        await InviteAsync(owner, second, address, cancellationToken);

        using var renewed = await owner.PostAsync(RenewUri(first, membership), content: null, cancellationToken);

        await ShouldBeRefusedLaterAsync(renewed, RecipientMailedRecently, TimeSpan.FromMinutes(10), cancellationToken);
    }

    [Fact]
    public async Task A_sender_at_the_daily_cap_is_429_too_many_invitations_sent_today_in_every_pharmacy_while_another_user_invites_and_the_next_day_may_again()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var sender = await aspire.CreateHostClientAsync(Sender);
        using var owner = await aspire.CreateHostClientAsync("owner");
        var person = await database.SubjectOfAsync(Sender);
        var first = await RegisterAsync(sender, cancellationToken);
        var second = await RegisterAsync(sender, cancellationToken);
        var owners = await RegisterAsync(owner, cancellationToken);
        await database.SetSenderMailsAsync(person, await database.TodayUtcAsync(cancellationToken), AspireFixture.InvitationMailsPerSenderPerDay - 1, cancellationToken);
        var last = await InviteAsync(sender, first, Invitee(), cancellationToken);
        (await database.SenderMailsThatDayAsync(person, cancellationToken)).ShouldBe(AspireFixture.InvitationMailsPerSenderPerDay);
        var refusedAddress = Invitee();

        using (var elsewhere = await sender.PostAsJsonAsync(InvitationsUri(second), Invitation(refusedAddress), cancellationToken))
        {
            await ShouldBeRefusedLaterAsync(elsewhere, TooManySentToday, TimeSpan.FromDays(1), cancellationToken);
        }

        await database.MoveInvitationRequestBackAsync(last, TimeSpan.FromMinutes(10), cancellationToken);
        using (var renewal = await sender.PostAsync(RenewUri(first, last), content: null, cancellationToken))
        {
            await ShouldBeRefusedLaterAsync(renewal, TooManySentToday, TimeSpan.FromDays(1), cancellationToken);
        }

        await InviteAsync(owner, owners, Invitee(), cancellationToken);
        await database.MoveSenderMailDayBackAsync(person, cancellationToken);

        await InviteAsync(sender, second, refusedAddress, cancellationToken);
        (await database.SenderMailsThatDayAsync(person, cancellationToken)).ShouldBe(1, "the next day still counted the day before's mails");
    }

    [Fact]
    public async Task A_refused_invitation_stores_nothing_counts_nothing_and_mails_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var sender = await aspire.CreateHostClientAsync(Sender);
        var person = await database.SubjectOfAsync(Sender);
        var pharmacy = await RegisterAsync(sender, cancellationToken);
        await database.SetSenderMailsAsync(person, await database.TodayUtcAsync(cancellationToken), AspireFixture.InvitationMailsPerSenderPerDay, cancellationToken);
        var memberships = await database.MembershipsOfAsync(pharmacy, cancellationToken);
        var pharmacyMails = await database.InvitationMailsThatDayAsync(pharmacy, cancellationToken);
        var refusedAddress = Invitee();

        using (var tooMany = await sender.PostAsJsonAsync(InvitationsUri(pharmacy), Invitation(refusedAddress), cancellationToken))
        {
            await ShouldBeRefusedLaterAsync(tooMany, TooManySentToday, TimeSpan.FromDays(1), cancellationToken);
        }

        (await database.MembershipsOfAsync(pharmacy, cancellationToken)).ShouldBe(memberships, "the refused invitation was stored");
        (await database.StatusesOfAsync(pharmacy, refusedAddress, cancellationToken)).ShouldBeEmpty();
        (await database.RecipientLastMailedAtAsync(refusedAddress, cancellationToken)).ShouldBeNull("the refused invitation counted on its address");
        (await database.SenderMailsThatDayAsync(person, cancellationToken)).ShouldBe(AspireFixture.InvitationMailsPerSenderPerDay, "the refused invitation counted on its sender");
        (await database.InvitationMailsThatDayAsync(pharmacy, cancellationToken)).ShouldBe(pharmacyMails, "the refused invitation counted on its pharmacy");

        // A mail requested by the refusal would have been sent before the one an invite requests after it.
        await database.MoveSenderMailDayBackAsync(person, cancellationToken);
        var laterAddress = Invitee();
        await InviteAsync(sender, pharmacy, laterAddress, cancellationToken);
        (await mailpit.MessagesToAsync(laterAddress, 1, cancellationToken)).ShouldHaveSingleItem();
        (await mailpit.CountToAsync(refusedAddress, cancellationToken)).ShouldBe(0, "the refused invitation sent a mail");
    }

    /// <summary>
    /// The recipient cooldown needs no lock (docs/adr/0004): two pharmacies inviting one new address both find no row
    /// for it, and the database's key on the derived id lets only the first insert it; the other answers 409.
    /// </summary>
    [Fact]
    public async Task Of_two_pharmacies_inviting_one_new_address_at_once_one_is_stored_and_mailed_and_the_other_is_a_conflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var first = await RegisterAsync(owner, cancellationToken);
        var second = await RegisterAsync(owner, cancellationToken);
        var address = Invitee();

        HttpResponseMessage[] responses;
        await using (var gate = await database.LockInvitationRecipientsAsync(cancellationToken))
        {
            var one = owner.PostAsJsonAsync(InvitationsUri(first), Invitation(address), cancellationToken);
            var other = owner.PostAsJsonAsync(InvitationsUri(second), Invitation(address), cancellationToken);
            await database.WaitUntilBlockedAsync(gate, 2, cancellationToken);
            await gate.CommitAsync(cancellationToken);
            responses = await Task.WhenAll(one, other);
        }

        try
        {
            List<string> answers = [];
            foreach (var response in responses)
            {
                answers.Add($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancellationToken)}");
            }

            var described = string.Join(" | ", answers);
            responses.Select(response => response.StatusCode).ShouldBe([HttpStatusCode.Created, HttpStatusCode.Conflict], ignoreOrder: true, described);
            var lost = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
            using var problem = JsonDocument.Parse(await lost.Content.ReadAsStringAsync(cancellationToken));
            problem.RootElement.GetProperty("code").GetString().ShouldBeOneOf(["already-exists", "organisation.pharmacy-changed"], described);
            var stored = (await database.StatusesOfAsync(first, address, cancellationToken)).Concat(await database.StatusesOfAsync(second, address, cancellationToken));
            stored.ShouldBe(["Invited"], described);
            (await mailpit.MessagesToAsync(address, 1, cancellationToken)).ShouldHaveSingleItem("the invitation that lost must send no mail");
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    private static object Invitation(string address) => new { email = address, jobFunction = "Pta", role = "Employee" };

    private static string Invitee() => $"invitee-{Guid.CreateVersion7()}@example.com";

    private static Uri InvitationsUri(Guid pharmacy) => new($"/organisation/pharmacies/{pharmacy}/invitations", UriKind.Relative);

    private static Uri InvitationUri(Guid pharmacy, Guid membership) => new($"/organisation/pharmacies/{pharmacy}/invitations/{membership}", UriKind.Relative);

    private static Uri RenewUri(Guid pharmacy, Guid membership) => new($"/organisation/pharmacies/{pharmacy}/invitations/{membership}/renew", UriKind.Relative);

    private static async Task<Guid> RegisterAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var created = await client.PostAsJsonAsync(Pharmacies, new { name = $"Apotheke {Guid.CreateVersion7()}", jobFunction = "Pharmacist" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(cancellationToken));
        return await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    /// <summary>Invites the address as an employee through the API and answers the new membership's id.</summary>
    private static async Task<Guid> InviteAsync(HttpClient client, Guid pharmacy, string address, CancellationToken cancellationToken)
    {
        using var invited = await client.PostAsJsonAsync(InvitationsUri(pharmacy), Invitation(address), cancellationToken);
        invited.StatusCode.ShouldBe(HttpStatusCode.Created, await invited.Content.ReadAsStringAsync(cancellationToken));
        return await invited.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    /// <summary>
    /// A 429 with the code, a <c>Retry-After</c> of at least one second and at most <paramref name="atMost"/>, and the
    /// instant itself as <c>retryAt</c> in the problem, which <c>Retry-After</c> counts down to.
    /// </summary>
    private static async Task ShouldBeRefusedLaterAsync(HttpResponseMessage response, string code, TimeSpan atMost, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests, body);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().ShouldBe(code, body);
        problem.RootElement.TryGetProperty("retryAt", out var retryAt).ShouldBeTrue($"the problem carries no retryAt: {body}");
        retryAt.GetDateTimeOffset().ShouldBeGreaterThan(TimeProvider.System.GetUtcNow(), body);
        var retryAfter = response.Headers.RetryAfter?.Delta;
        retryAfter.ShouldNotBeNull($"the 429 carries no Retry-After in seconds: {body}");
        retryAfter.Value.ShouldBeInRange(TimeSpan.FromSeconds(1), atMost, body);
        (retryAfter.Value.TotalSeconds % 1).ShouldBe(0, "Retry-After is not in whole seconds");
    }
}
