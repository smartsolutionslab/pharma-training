using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Api;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.InviteMember;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RenewInvitation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationSenders;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.InvitationRecipients;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>
/// Invite and renew refuse beyond the invitation mail limits with 429, the limit's code, and a <c>Retry-After</c> in
/// whole seconds until the refusal ends. The endpoints are mapped through <see cref="OrganisationModule.MapEndpoints"/>,
/// so the module's route group adds the header as it does in the host; their request delegates are invoked directly,
/// and the dispatcher forwards to the real handlers over the in-memory store. The database's clock is a second
/// <see cref="FakeTimeProvider"/>, the host's unless a test gives one of its own.
/// </summary>
public class InvitationLimitEndpointTests
{
    private static readonly DateTimeOffset Start = new(2027, 4, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly PersonId Owner = PersonId.From(Guid.CreateVersion7());

    [Fact]
    public async Task Renewing_within_the_cooldown_answers_429_with_retry_after_until_it_ends()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var invitation = pharmacy.Invite(MembershipId.New(), EmailAddress.From("anna@example.org"), JobFunction.Pta, MembershipRole.Employee, Timestamp.From(Start), InvitationMailLimits.Default).Value;
        pharmacy.Stored(store);
        var time = new FakeTimeProvider(Start);
        time.Advance(TimeSpan.FromMinutes(4));

        var answer = await SendAsync(
            store,
            time,
            InvitationMailLimits.Default,
            "/organisation/pharmacies/{id:guid}/invitations/{membershipId:guid}/renew",
            new RouteValueDictionary { ["id"] = pharmacy.Id.Value.ToString(), ["membershipId"] = invitation.Value.ToString() },
            body: null);

        answer.Status.ShouldBe((int)HttpStatusCode.TooManyRequests, "renewing 4 minutes after the invite");
        answer.RetryAfter.ShouldBe("360");
        CodeOf(answer.Body).ShouldBe("organisation.invitation-renewed-too-soon");
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task Inviting_at_the_daily_cap_answers_429_with_retry_after_until_midnight_utc()
    {
        var store = new InMemoryPharmacyStore();
        var capOfOne = new InvitationMailLimits(RenewCooldown.From(TimeSpan.FromMinutes(10)), DailyMailCap.From(1));
        var pharmacy = TestPharmacies.Register(Owner);
        pharmacy.Invite(MembershipId.New(), EmailAddress.From("anna@example.org"), JobFunction.Pta, MembershipRole.Employee, Timestamp.From(Start), capOfOne)
            .IsSuccess.ShouldBeTrue();
        pharmacy.Stored(store);
        var time = new FakeTimeProvider(new DateTimeOffset(2027, 4, 1, 22, 0, 0, TimeSpan.Zero));

        var answer = await SendAsync(
            store,
            time,
            capOfOne,
            "/organisation/pharmacies/{id:guid}/invitations",
            new RouteValueDictionary { ["id"] = pharmacy.Id.Value.ToString() },
            body: """{"email":"ben@example.org","jobFunction":"Pta","role":"Employee"}""");

        answer.Status.ShouldBe((int)HttpStatusCode.TooManyRequests, "inviting at 22:00 UTC on a day capped at one mail that has sent it");
        answer.RetryAfter.ShouldBe("7200");
        CodeOf(answer.Body).ShouldBe("organisation.too-many-invitations-today");
        store.Commits.ShouldBe(0);
    }

    // #265
    [Fact]
    public async Task Inviting_an_address_another_pharmacy_mailed_four_minutes_ago_answers_429_with_retry_after_until_its_cooldown_ends()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner).Stored(store);
        store.StoredRecipients.Add(StoredRecipients.MailedAt(EmailAddress.From("ben@example.org"), Timestamp.From(Start)));
        var time = new FakeTimeProvider(Start);
        time.Advance(TimeSpan.FromMinutes(4));

        var answer = await SendAsync(
            store,
            time,
            InvitationMailLimits.Default,
            "/organisation/pharmacies/{id:guid}/invitations",
            new RouteValueDictionary { ["id"] = pharmacy.Id.Value.ToString() },
            body: """{"email":"ben@example.org","jobFunction":"Pta","role":"Employee"}""");

        answer.Status.ShouldBe((int)HttpStatusCode.TooManyRequests, "inviting an address another pharmacy mailed 4 minutes ago");
        answer.RetryAfter.ShouldBe("360");
        CodeOf(answer.Body).ShouldBe("organisation.recipient-mailed-recently");
        RetryAtOf(answer.Body).ShouldBe(Start.AddMinutes(10));
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task Inviting_as_a_sender_at_the_daily_cap_answers_429_with_retry_after_until_midnight_utc()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner).Stored(store);
        store.StoredSenders.Add(InvitationSender.FirstMailed(Owner, Timestamp.From(Start)));
        var capOfOne = new RecipientAndSenderMailLimits(RecipientCooldown.From(TimeSpan.FromMinutes(10)), DailyMailCap.From(1));
        var time = new FakeTimeProvider(new DateTimeOffset(2027, 4, 1, 22, 0, 0, TimeSpan.Zero));

        var answer = await SendAsync(
            store,
            time,
            InvitationMailLimits.Default,
            "/organisation/pharmacies/{id:guid}/invitations",
            new RouteValueDictionary { ["id"] = pharmacy.Id.Value.ToString() },
            body: """{"email":"ben@example.org","jobFunction":"Pta","role":"Employee"}""",
            recipientAndSender: capOfOne);

        answer.Status.ShouldBe((int)HttpStatusCode.TooManyRequests, "inviting at 22:00 UTC as a sender capped at one mail a day that has sent it");
        answer.RetryAfter.ShouldBe("7200");
        CodeOf(answer.Body).ShouldBe("organisation.too-many-invitations-sent-today");
        RetryAtOf(answer.Body).ShouldBe(new DateTimeOffset(2027, 4, 2, 0, 0, 0, TimeSpan.Zero));
        store.Commits.ShouldBe(0);
    }

    // #294: Retry-After counts from the instant the database's clock refused at, not from the host's clock.
    [Fact]
    public async Task Renewing_within_the_cooldown_on_a_host_three_days_ahead_answers_retry_after_from_the_database_clock()
    {
        var store = new InMemoryPharmacyStore();
        var pharmacy = TestPharmacies.Register(Owner);
        var invitation = pharmacy.Invite(MembershipId.New(), EmailAddress.From("anna@example.org"), JobFunction.Pta, MembershipRole.Employee, Timestamp.From(Start), InvitationMailLimits.Default).Value;
        pharmacy.Stored(store);
        var host = new FakeTimeProvider(Start.AddDays(3));
        var database = new FakeTimeProvider(Start.AddMinutes(4));

        var answer = await SendAsync(
            store,
            host,
            InvitationMailLimits.Default,
            "/organisation/pharmacies/{id:guid}/invitations/{membershipId:guid}/renew",
            new RouteValueDictionary { ["id"] = pharmacy.Id.Value.ToString(), ["membershipId"] = invitation.Value.ToString() },
            body: null,
            database: database);

        answer.Status.ShouldBe((int)HttpStatusCode.TooManyRequests, "renewing 4 minutes after the invite on the database's clock");
        CodeOf(answer.Body).ShouldBe("organisation.invitation-renewed-too-soon");
        answer.RetryAfter.ShouldBe("360", "Retry-After counted on the host's clock, three days ahead of the database's");
        RetryAtOf(answer.Body).ShouldBe(Start.AddMinutes(10));
        RefusedAtOf(answer.Body).ShouldBe(Start.AddMinutes(4), "the problem does not carry the database's instant of the refusal");
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task Inviting_at_the_daily_cap_on_a_host_an_hour_behind_answers_retry_after_from_the_database_clock()
    {
        var store = new InMemoryPharmacyStore();
        var capOfOne = new InvitationMailLimits(RenewCooldown.From(TimeSpan.FromMinutes(10)), DailyMailCap.From(1));
        var pharmacy = TestPharmacies.Register(Owner);
        pharmacy.Invite(MembershipId.New(), EmailAddress.From("anna@example.org"), JobFunction.Pta, MembershipRole.Employee, Timestamp.From(Start), capOfOne)
            .IsSuccess.ShouldBeTrue();
        pharmacy.Stored(store);
        var refusedAt = new DateTimeOffset(2027, 4, 1, 22, 0, 0, TimeSpan.Zero);
        var host = new FakeTimeProvider(refusedAt.AddHours(-1));
        var database = new FakeTimeProvider(refusedAt);

        var answer = await SendAsync(
            store,
            host,
            capOfOne,
            "/organisation/pharmacies/{id:guid}/invitations",
            new RouteValueDictionary { ["id"] = pharmacy.Id.Value.ToString() },
            body: """{"email":"ben@example.org","jobFunction":"Pta","role":"Employee"}""",
            database: database);

        answer.Status.ShouldBe((int)HttpStatusCode.TooManyRequests, "inviting at 22:00 UTC on the database's clock on a day capped at one mail that has sent it");
        CodeOf(answer.Body).ShouldBe("organisation.too-many-invitations-today");
        answer.RetryAfter.ShouldBe("7200", "Retry-After counted on the host's clock, an hour behind the database's");
        RetryAtOf(answer.Body).ShouldBe(new DateTimeOffset(2027, 4, 2, 0, 0, 0, TimeSpan.Zero));
        RefusedAtOf(answer.Body).ShouldBe(refusedAt, "the problem does not carry the database's instant of the refusal");
        store.Commits.ShouldBe(0);
    }

    private static DateTimeOffset? RetryAtOf(byte[] body) => InstantOf(body, "retryAt");

    private static DateTimeOffset? RefusedAtOf(byte[] body) => InstantOf(body, "refusedAt");

    private static DateTimeOffset? InstantOf(byte[] body, string extension)
    {
        if (body.Length == 0)
        {
            return null;
        }

        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty(extension, out var instant) ? instant.GetDateTimeOffset() : null;
    }

    private static ClaimsPrincipal Caller() =>
        new(new ClaimsIdentity(
            [new Claim("sub", Owner.Value.ToString()), new Claim("name", "Olaf Owner"), new Claim("email", "owner@example.com")],
            authenticationType: "Bearer"));

    private static IDispatcher Dispatching(InMemoryPharmacyStore store, TimeProvider database, InvitationMailLimits limits, RecipientAndSenderMailLimits recipientAndSender)
    {
        var invite = new InviteMemberHandler(store, store, store, store, new FakeDatabaseClock(database), limits, recipientAndSender, NullLogger<InviteMemberHandler>.Instance);
        var renew = new RenewInvitationHandler(store, store, store, store, new FakeDatabaseClock(database), limits, recipientAndSender, NullLogger<RenewInvitationHandler>.Instance);
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.SendAsync(Arg.Any<ICommand<Result<Guid, ApiError>>>(), Arg.Any<CancellationToken>())
            .Returns(call => invite.HandleAsync((InviteMember)call.ArgAt<ICommand<Result<Guid, ApiError>>>(0), call.ArgAt<CancellationToken>(1)));
        dispatcher.SendAsync(Arg.Any<ICommand<Result<Unit, ApiError>>>(), Arg.Any<CancellationToken>())
            .Returns(call => renew.HandleAsync((RenewInvitation)call.ArgAt<ICommand<Result<Unit, ApiError>>>(0), call.ArgAt<CancellationToken>(1)));
        return dispatcher;
    }

    private static async Task<(int Status, string RetryAfter, byte[] Body)> SendAsync(
        InMemoryPharmacyStore store,
        FakeTimeProvider time,
        InvitationMailLimits limits,
        string route,
        RouteValueDictionary values,
        string? body,
        RecipientAndSenderMailLimits? recipientAndSender = null,
        FakeTimeProvider? database = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton(Dispatching(store, database ?? time, limits, recipientAndSender ?? RecipientAndSenderMailLimits.Default));
        builder.Services.AddSingleton<TimeProvider>(time);

        // The module maps every endpoint of its group, accept among them, which names the module's per-user limit.
        builder.Services.AddUserRateLimit(OrganisationLimits.AcceptFrom(new ConfigurationBuilder().Build()));
        await using var app = builder.Build();
        new OrganisationModule().MapEndpoints(app);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .SingleOrDefault(candidate => candidate.RoutePattern.RawText == route
                && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Post) == true);
        endpoint.ShouldNotBeNull($"OrganisationModule maps no POST {route}");

        using var response = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = app.Services, User = Caller() };
        context.Request.Method = HttpMethods.Post;
        context.Request.RouteValues = values;
        if (body is not null)
        {
            context.Request.ContentType = "application/json";
            var request = Encoding.UTF8.GetBytes(body);
            context.Request.Body = new MemoryStream(request);
            context.Request.ContentLength = request.Length;
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new WithBody());
        }

        context.Response.Body = response;

        await endpoint.RequestDelegate!(context);

        return (context.Response.StatusCode, context.Response.Headers.RetryAfter.ToString(), response.ToArray());
    }

    private static string? CodeOf(byte[] body)
    {
        if (body.Length == 0)
        {
            return null;
        }

        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>Tells the request delegate the request has a body, as a server does for a POST that sends one.</summary>
    private sealed class WithBody : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
