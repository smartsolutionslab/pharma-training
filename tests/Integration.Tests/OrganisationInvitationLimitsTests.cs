using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Invitation mail is limited in Organisation's database (docs/adr/0004): an invitation is renewed at most once per
/// 10 minutes, and a pharmacy requests at most 50 invitation mails per UTC day. Over a limit, invite and renew answer
/// 429 with the limit's code, a <c>Retry-After</c> and <c>retryAt</c>, and nothing is stored or mailed. The tests
/// travel in time by moving the stored instants back, never by waiting.
/// </summary>
[Collection(InvitationSenders.Name)]
public class OrganisationInvitationLimitsTests(AspireFixture aspire)
{
    private static readonly Uri Pharmacies = new("/organisation/pharmacies", UriKind.Relative);

    private readonly OrganisationDatabase database = new(aspire);

    private readonly Mailpit mailpit = new(aspire);

    [Fact]
    public async Task Renewing_an_invitation_again_within_ten_minutes_is_429_with_retry_after_until_the_cooldown_ends_and_once_it_has_passed_is_ok()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var address = Invitee();
        var membership = await InviteAsync(owner, pharmacy, address, cancellationToken);
        (await mailpit.MessagesToAsync(address, 1, cancellationToken)).ShouldHaveSingleItem();

        using (var tooSoon = await owner.PostAsync(RenewUri(pharmacy, membership), content: null, cancellationToken))
        {
            await ShouldBeRefusedLaterAsync(tooSoon, "organisation.invitation-renewed-too-soon", TimeSpan.FromMinutes(10), cancellationToken);
        }

        (await mailpit.CountToAsync(address, cancellationToken)).ShouldBe(1, "the refused renewal sent a mail");
        await database.MoveInvitationRequestBackAsync(membership, TimeSpan.FromMinutes(10), cancellationToken);

        using var renewed = await owner.PostAsync(RenewUri(pharmacy, membership), content: null, cancellationToken);

        renewed.StatusCode.ShouldBe(HttpStatusCode.OK, await renewed.Content.ReadAsStringAsync(cancellationToken));
        (await mailpit.MessagesToAsync(address, 2, cancellationToken)).Count.ShouldBe(2, "the refused renewal sent a mail besides the invite's and the renewal's");
    }

    [Fact]
    public async Task A_pharmacy_at_fifty_invitation_mails_today_is_429_with_retry_after_until_midnight_utc_and_the_next_day_may_invite_again()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        await database.SetInvitationMailsAsync(pharmacy, await database.TodayUtcAsync(cancellationToken), 49, cancellationToken);
        await InviteAsync(owner, pharmacy, Invitee(), cancellationToken);
        var refusedAddress = Invitee();

        using (var tooMany = await owner.PostAsJsonAsync(InvitationsUri(pharmacy), Invitation(refusedAddress), cancellationToken))
        {
            await ShouldBeRefusedLaterAsync(tooMany, "organisation.too-many-invitations-today", TimeSpan.FromDays(1), cancellationToken);
        }

        await database.MoveInvitationMailDayBackAsync(pharmacy, cancellationToken);

        using var nextDay = await owner.PostAsJsonAsync(InvitationsUri(pharmacy), Invitation(refusedAddress), cancellationToken);

        nextDay.StatusCode.ShouldBe(HttpStatusCode.Created, await nextDay.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task A_refused_invitation_mails_nothing_and_stores_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        await database.SetInvitationMailsAsync(pharmacy, await database.TodayUtcAsync(cancellationToken), 50, cancellationToken);
        var memberships = await database.MembershipsOfAsync(pharmacy, cancellationToken);
        var refusedAddress = Invitee();

        using (var tooMany = await owner.PostAsJsonAsync(InvitationsUri(pharmacy), Invitation(refusedAddress), cancellationToken))
        {
            await ShouldBeRefusedLaterAsync(tooMany, "organisation.too-many-invitations-today", TimeSpan.FromDays(1), cancellationToken);
        }

        (await database.MembershipsOfAsync(pharmacy, cancellationToken)).ShouldBe(memberships, "the refused invitation was stored");
        (await database.StatusesOfAsync(pharmacy, refusedAddress, cancellationToken)).ShouldBeEmpty();

        // A mail requested by the refusal would have been sent before the one an invite requests after it.
        await database.MoveInvitationMailDayBackAsync(pharmacy, cancellationToken);
        var laterAddress = Invitee();
        await InviteAsync(owner, pharmacy, laterAddress, cancellationToken);
        (await mailpit.MessagesToAsync(laterAddress, 1, cancellationToken)).ShouldHaveSingleItem();
        (await mailpit.CountToAsync(refusedAddress, cancellationToken)).ShouldBe(0, "the refused invitation sent a mail");
    }

    /// <summary>
    /// The cap needs no lock (docs/adr/0004): two invites at 49 mails both load the tally below the cap, and the
    /// pharmacy's row version lets only the first commit count the fiftieth; the other is told the pharmacy changed.
    /// </summary>
    [Fact]
    public async Task Of_two_invitations_at_once_at_forty_nine_mails_today_one_is_stored_and_the_other_is_told_the_pharmacy_changed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        await database.SetInvitationMailsAsync(pharmacy, await database.TodayUtcAsync(cancellationToken), 49, cancellationToken);

        HttpResponseMessage[] responses;
        await using (var gate = await database.LockPharmacyAsync(pharmacy, cancellationToken))
        {
            var first = owner.PostAsJsonAsync(InvitationsUri(pharmacy), Invitation(Invitee()), cancellationToken);
            var second = owner.PostAsJsonAsync(InvitationsUri(pharmacy), Invitation(Invitee()), cancellationToken);
            await database.WaitUntilBlockedAsync(gate, 2, cancellationToken);
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

            var described = string.Join(" | ", answers);
            responses.Select(response => response.StatusCode).ShouldBe([HttpStatusCode.Created, HttpStatusCode.Conflict], ignoreOrder: true, described);
            var lost = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
            using var problem = JsonDocument.Parse(await lost.Content.ReadAsStringAsync(cancellationToken));
            // On the sender's row the two wait for each other (#369) and on their recipients' rows they share nothing, so
            // the loser loses on the pharmacy alone.
            problem.RootElement.GetProperty("code").GetString().ShouldBe("organisation.pharmacy-changed", described);
            (await database.InvitationMailsThatDayAsync(pharmacy, cancellationToken)).ShouldBe(50, described);
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
    /// A request time a cooldown or more ahead of the database's now is not trusted (#294): a row edited by hand, or a
    /// database clock that moved back, cannot block renewing for longer than two cooldowns.
    /// </summary>
    [Fact]
    public async Task Renewing_an_invitation_whose_request_time_lies_a_year_ahead_is_ok_and_mails_again()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var address = Invitee();
        var membership = await InviteAsync(owner, pharmacy, address, cancellationToken);
        (await mailpit.MessagesToAsync(address, 1, cancellationToken)).ShouldHaveSingleItem();
        await database.SetInvitationRequestedAheadAsync(membership, TimeSpan.FromDays(365), cancellationToken);
        await database.MoveRecipientMailBackAsync(address, TimeSpan.FromMinutes(10), cancellationToken);

        using var renewed = await owner.PostAsync(RenewUri(pharmacy, membership), content: null, cancellationToken);

        renewed.StatusCode.ShouldBe(HttpStatusCode.OK, await renewed.Content.ReadAsStringAsync(cancellationToken));
        (await mailpit.MessagesToAsync(address, 2, cancellationToken)).Count.ShouldBe(2, "the renewal sent no second mail");
    }

    /// <summary>
    /// A stored mail day beyond tomorrow is discarded (#158, #294): even at the end of the calendar and at the cap, the
    /// pharmacy invites, and today's tally starts afresh. Characterizes behaviour that shipped with #158.
    /// </summary>
    [Fact]
    public async Task A_pharmacy_whose_stored_mail_day_is_9999_12_31_at_the_cap_invites_and_today_starts_the_tally_afresh()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var today = await database.TodayUtcAsync(cancellationToken);
        await database.SetInvitationMailsAsync(pharmacy, new DateOnly(9999, 12, 31), 50, cancellationToken);
        var address = Invitee();

        await InviteAsync(owner, pharmacy, address, cancellationToken);

        (await mailpit.MessagesToAsync(address, 1, cancellationToken)).ShouldHaveSingleItem();
        (await database.InvitationMailsThatDayAsync(pharmacy, cancellationToken)).ShouldBe(1, "today's tally did not start afresh");
        (await database.InvitationMailDayAsync(pharmacy, cancellationToken)).ShouldBeOneOf([today, today.AddDays(1)], "the stored day is not today");
    }

    /// <summary>
    /// A recipient's stored instant within a cooldown of the end of the calendar is distrusted (#326), never added to:
    /// the invite is created and the address mailed once, and the stored instant is replaced by the database's now.
    /// </summary>
    [Fact]
    public async Task An_address_whose_stored_mail_instant_is_9999_12_31_is_invited_and_mailed_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var address = Invitee();
        await database.SetRecipientLastMailedAtAsync(address, new DateTimeOffset(9999, 12, 31, 23, 55, 0, TimeSpan.Zero), cancellationToken);

        using var invited = await owner.PostAsJsonAsync(InvitationsUri(pharmacy), Invitation(address), cancellationToken);

        invited.StatusCode.ShouldBe(HttpStatusCode.Created, await invited.Content.ReadAsStringAsync(cancellationToken));
        (await mailpit.MessagesToAsync(address, 1, cancellationToken)).ShouldHaveSingleItem();
        (await database.RecipientLastMailedAtAsync(address, cancellationToken)).ShouldNotBeNull().Year.ShouldBeLessThan(9999, "the stored instant at the end of the calendar was kept");
    }

    /// <summary>
    /// <c>Retry-After</c> counts down from the instant the refusal was made on the database's clock (#294, #326): it is
    /// exactly the whole seconds, rounded up, between <c>refusedAt</c> and <c>retryAt</c> of the problem.
    /// </summary>
    [Fact]
    public async Task Retry_after_of_a_refused_renewal_is_the_whole_seconds_rounded_up_from_refused_at_to_retry_at()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var membership = await InviteAsync(owner, pharmacy, Invitee(), cancellationToken);

        using var tooSoon = await owner.PostAsync(RenewUri(pharmacy, membership), content: null, cancellationToken);

        var body = await tooSoon.Content.ReadAsStringAsync(cancellationToken);
        tooSoon.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests, body);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().ShouldBe("organisation.invitation-renewed-too-soon", body);
        problem.RootElement.TryGetProperty("retryAt", out var retryAt).ShouldBeTrue($"the problem carries no retryAt: {body}");
        problem.RootElement.TryGetProperty("refusedAt", out var refusedAt).ShouldBeTrue($"the problem carries no refusedAt: {body}");
        var expected = (long)Math.Ceiling((retryAt.GetDateTimeOffset() - refusedAt.GetDateTimeOffset()).TotalSeconds);
        var retryAfter = tooSoon.Headers.RetryAfter?.Delta;
        retryAfter.ShouldNotBeNull($"the 429 carries no Retry-After in seconds: {body}");
        ((long)retryAfter.Value.TotalSeconds).ShouldBe(expected, $"Retry-After is not ceil(retryAt - refusedAt): {body}");
    }

    private static object Invitation(string address) => new { email = address, jobFunction = "Pta", role = "Employee" };

    private static string Invitee() => $"invitee-{Guid.CreateVersion7()}@example.com";

    private static Uri InvitationsUri(Guid pharmacy) => new($"/organisation/pharmacies/{pharmacy}/invitations", UriKind.Relative);

    private static Uri RenewUri(Guid pharmacy, Guid membership) => new($"/organisation/pharmacies/{pharmacy}/invitations/{membership}/renew", UriKind.Relative);

    private static async Task<Guid> RegisterAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var created = await client.PostAsJsonAsync(Pharmacies, new { name = $"Apotheke {Guid.CreateVersion7()}", jobFunction = "Pharmacist" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
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
