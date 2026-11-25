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

    private const int RaceAttempts = 3;

    private static readonly Uri Pharmacies = new("/organisation/pharmacies", UriKind.Relative);

    // How long the gate waits for the second of two racing invitations once the first waits for it. The first's wait
    // counts against the store's lock timeout (5 seconds) from the moment it starts, so the gate lets go well before
    // that, whether the second has come or not; a race whose second came too late is run again, at most so many times.
    private static readonly TimeSpan RaceWindow = TimeSpan.FromSeconds(2);

    // How long a test lets a request wait for a row a gate holds: well past the store's lock timeout of a few seconds, so
    // a request that would wait for the gate forever fails the test instead of hanging it.
    private static readonly TimeSpan LockWaitBound = TimeSpan.FromSeconds(30);

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
    /// Two pharmacies inviting one new address at once (#375): both find no recipient row, since the gate holds the
    /// recipients' table until both wait on it, and both are let go together. The row is created once and held by the
    /// first until it commits, so the second waits for it and then counts on the row as the first committed it: within
    /// the address's cooldown, as it would be alone, it is told 429 organisation.recipient-mailed-recently, never 409
    /// already-exists. Only the first is stored, counted and mailed. One manager invites from both pharmacies, so the
    /// sender's row orders them too (#381).
    /// </summary>
    [Fact]
    public async Task Of_two_pharmacies_inviting_one_new_address_at_once_the_first_is_stored_and_mailed_and_the_second_waits_and_is_told_the_address_was_mailed_recently()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var person = await database.SubjectOfAsync("owner");
        // The sender's row is made by an invitation from a third pharmacy. Its mail is sent by a consumer that saves that
        // pharmacy once more after the request has answered; from first or second, that save could move the pharmacy on
        // under the race below and answer it 409 organisation.pharmacy-changed instead of the cooldown's 429.
        var elsewhere = await RegisterAsync(owner, cancellationToken);
        await InviteAsync(owner, elsewhere, Invitee(), cancellationToken);

        var raced = false;
        for (var attempt = 0; attempt < RaceAttempts && !raced; attempt++)
        {
            var first = await RegisterAsync(owner, cancellationToken);
            var second = await RegisterAsync(owner, cancellationToken);
            raced = await InviteOneNewAddressAtOnceAsync((owner, first), (owner, second), [person], cancellationToken);
        }

        raced.ShouldBeTrue($"in {RaceAttempts} attempts the second invitation never came to the gate within {RaceWindow} of the first");
    }

    /// <summary>
    /// A request waits a few seconds at most for a recipient's row another transaction holds (#375): past the lock
    /// timeout it is told to try again (409 organisation.concurrent-save), never a 500 and never left hanging, and it
    /// leaves nothing behind. Once the row is free, the same invitation is stored.
    /// </summary>
    [Fact]
    public async Task An_invitation_whose_recipient_row_is_held_past_the_lock_timeout_is_told_to_try_again_and_stores_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var person = await database.SubjectOfAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        await InviteAsync(owner, pharmacy, Invitee(), cancellationToken);
        var senderMails = await database.SenderMailsThatDayAsync(person, cancellationToken);
        var address = Invitee();

        await using (await database.HoldNewInvitationRecipientAsync(address, cancellationToken))
        {
            using var refused = await owner.PostAsJsonAsync(InvitationsUri(pharmacy), Invitation(address), cancellationToken).WaitAsync(LockWaitBound, cancellationToken);
            await ShouldBeToldToTryAgainAsync(refused, cancellationToken);
        }

        (await database.StatusesOfAsync(pharmacy, address, cancellationToken)).ShouldBeEmpty("the invitation that timed out was stored");
        (await database.RecipientLastMailedAtAsync(address, cancellationToken)).ShouldBeNull("the invitation that timed out counted on its address");
        (await database.SenderMailsThatDayAsync(person, cancellationToken)).ShouldBe(senderMails, "the invitation that timed out counted on its sender");
        await InviteAsync(owner, pharmacy, address, cancellationToken);
    }

    /// <summary>
    /// As above for the sender's row (#375, review of #381): a request waits a few seconds at most for it, is then told to
    /// try again, and rolls back the recipient row it had created already.
    /// </summary>
    [Fact]
    public async Task An_invitation_whose_sender_row_is_held_past_the_lock_timeout_is_told_to_try_again_and_stores_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var person = await database.SubjectOfAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        await InviteAsync(owner, pharmacy, Invitee(), cancellationToken);
        var senderMails = await database.SenderMailsThatDayAsync(person, cancellationToken);
        var address = Invitee();

        await using (await database.HoldInvitationSenderAsync(person, cancellationToken))
        {
            using var refused = await owner.PostAsJsonAsync(InvitationsUri(pharmacy), Invitation(address), cancellationToken).WaitAsync(LockWaitBound, cancellationToken);
            await ShouldBeToldToTryAgainAsync(refused, cancellationToken);
        }

        (await database.StatusesOfAsync(pharmacy, address, cancellationToken)).ShouldBeEmpty("the invitation that timed out was stored");
        (await database.RecipientLastMailedAtAsync(address, cancellationToken)).ShouldBeNull("the invitation that timed out left its new recipient row behind");
        (await database.SenderMailsThatDayAsync(person, cancellationToken)).ShouldBe(senderMails, "the invitation that timed out counted on its sender");
        await InviteAsync(owner, pharmacy, address, cancellationToken);
    }

    /// <summary>
    /// A sender's very first two mails at once (#369): both find no sender row, since the gate holds the senders' table
    /// until both wait on it, and both are let go together. The row is created once and both are counted on it, one
    /// after the other; neither loses on the row's key. The two invitations are for two pharmacies and two addresses,
    /// so they share nothing but their sender. <c>owner</c>'s row is deleted first, which only this collection may do.
    /// </summary>
    [Fact]
    public async Task A_senders_first_two_invitations_at_once_are_both_created_and_both_counted_on_one_new_row()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var person = await database.SubjectOfAsync("owner");
        var first = await RegisterAsync(owner, cancellationToken);
        var second = await RegisterAsync(owner, cancellationToken);
        await database.ForgetSenderAsync(person, cancellationToken);

        HttpResponseMessage[] responses;
        await using (var gate = await database.LockInvitationSendersAsync(cancellationToken))
        {
            var one = owner.PostAsJsonAsync(InvitationsUri(first), Invitation(Invitee()), cancellationToken);
            var other = owner.PostAsJsonAsync(InvitationsUri(second), Invitation(Invitee()), cancellationToken);
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
            responses.Select(response => response.StatusCode).ShouldBe([HttpStatusCode.Created, HttpStatusCode.Created], described);
            (await database.SenderMailsThatDayAsync(person, cancellationToken)).ShouldBe(2, $"both first mails must count on the one new row; {described}");
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    /// <summary>
    /// Invites one new address from both pharmacies while the gate holds the recipients' table, lets both go together and
    /// checks the outcome: one 201, one 429 organisation.recipient-mailed-recently, one invitation stored, counted on the
    /// address and on its sender alone, and one mail. Answers whether both waited at the gate together, which only then
    /// raced. The first to wait starts its lock timeout as it does, so the gate waits <see cref="RaceWindow"/> at most for
    /// the second and then lets go regardless: a second that came later finds the first's row and is refused all the
    /// same, but ran no race, and the caller tries again.
    /// </summary>
    private async Task<bool> InviteOneNewAddressAtOnceAsync(
        (HttpClient Client, Guid Pharmacy) one,
        (HttpClient Client, Guid Pharmacy) other,
        IReadOnlyList<Guid> senders,
        CancellationToken cancellationToken)
    {
        var address = Invitee();
        var senderMails = 0;
        foreach (var sender in senders)
        {
            senderMails += await database.SenderMailsThatDayAsync(sender, cancellationToken);
        }

        bool raced;
        HttpResponseMessage[] responses;
        await using (var gate = await database.LockInvitationRecipientsAsync(cancellationToken))
        {
            var first = one.Client.PostAsJsonAsync(InvitationsUri(one.Pharmacy), Invitation(address), cancellationToken);
            var second = other.Client.PostAsJsonAsync(InvitationsUri(other.Pharmacy), Invitation(address), cancellationToken);
            await database.WaitUntilAnyBlockedAsync(gate, cancellationToken);
            raced = await database.TryWaitUntilBlockedAsync(gate, 2, RaceWindow, cancellationToken);
            await gate.CommitAsync(cancellationToken);
            responses = await Task.WhenAll(first, second);
        }

        try
        {
            List<string> answers = [];
            foreach (var response in responses)
            {
                answers.Add($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancellationToken)}");
            }

            var described = $"{string.Join(" | ", answers)} (both waited at the gate: {raced})";
            responses.Select(response => response.StatusCode).ShouldBe([HttpStatusCode.Created, HttpStatusCode.TooManyRequests], ignoreOrder: true, described);
            // The second read the database's clock before it waited, a moment before the first counted its mail, so its
            // wait is the cooldown plus that moment, rounded up to whole seconds.
            await ShouldBeRefusedLaterAsync(responses.Single(response => response.StatusCode != HttpStatusCode.Created), RecipientMailedRecently, TimeSpan.FromMinutes(10).Add(TimeSpan.FromSeconds(5)), cancellationToken);
            var stored = (await database.StatusesOfAsync(one.Pharmacy, address, cancellationToken)).Concat(await database.StatusesOfAsync(other.Pharmacy, address, cancellationToken));
            stored.ShouldBe(["Invited"], described);
            (await database.RecipientLastMailedAtAsync(address, cancellationToken)).ShouldNotBeNull(described);
            var counted = 0;
            foreach (var sender in senders)
            {
                counted += await database.SenderMailsThatDayAsync(sender, cancellationToken);
            }

            counted.ShouldBe(senderMails + 1, $"only the first invitation may count on its sender; {described}");
            (await mailpit.MessagesToAsync(address, 1, cancellationToken)).ShouldHaveSingleItem("the refused invitation must send no mail");
            return raced;
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    private static async Task ShouldBeToldToTryAgainAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, body);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().ShouldBe("organisation.concurrent-save", body);
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
