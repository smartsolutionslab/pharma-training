using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Commands.SetRetentionPeriod;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Retention;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Commands;

/// <summary>
/// #391: the pharmacy's manager sets its retention period. Who may is decided by Certification's copy of the caller's
/// membership and its current role (docs/adr/0003): a non-member is told the pharmacy is not found, a member whose role
/// lacks the capability is forbidden, and in neither case is anything stored.
/// </summary>
public class SetRetentionPeriodTests
{
    private static readonly DateTimeOffset LongAgo = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly InMemoryMembershipStore memberships = new();
    private readonly InMemoryPharmacyRetentionStore retention = new();
    private readonly PharmacyId pharmacy = PharmacyId.From(Guid.CreateVersion7());
    private readonly PersonId manager = PersonId.From(Guid.CreateVersion7());

    [Fact]
    public async Task A_manager_sets_the_pharmacys_period()
    {
        Start(MembershipRole.Manager);

        var result = await HandleAsync(7);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : string.Empty);
        retention.Stored[pharmacy].Period.ShouldBe(RetentionPeriod.From(7));
    }

    [Fact]
    public async Task The_last_period_set_is_kept()
    {
        Start(MembershipRole.Manager);

        await HandleAsync(7);
        var result = await HandleAsync(3);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : string.Empty);
        retention.Stored.ShouldHaveSingleItem().Value.Period.ShouldBe(RetentionPeriod.From(3));
    }

    [Fact]
    public async Task An_employee_is_forbidden_and_nothing_is_stored()
    {
        Start(MembershipRole.Employee);

        var result = await HandleAsync(7);

        ShouldBeRefused(result, HttpStatusCode.Forbidden, "certification.capability-missing");
    }

    [Fact]
    public async Task Someone_who_is_no_member_is_told_the_pharmacy_is_not_found_and_nothing_is_stored()
    {
        Start(MembershipRole.Manager, PharmacyId.From(Guid.CreateVersion7()));

        var result = await HandleAsync(7);

        ShouldBeRefused(result, HttpStatusCode.NotFound, "certification.pharmacy-not-found");
    }

    [Fact]
    public async Task A_manager_whose_membership_ended_is_told_the_pharmacy_is_not_found()
    {
        var membership = Start(MembershipRole.Manager);
        memberships.Endings.Add(MembershipEnding.Of(membership, manager, pharmacy, Timestamp.From(LongAgo.AddYears(1))));

        var result = await HandleAsync(7);

        ShouldBeRefused(result, HttpStatusCode.NotFound, "certification.pharmacy-not-found");
    }

    [Fact]
    public async Task A_manager_demoted_by_a_later_change_is_forbidden()
    {
        var membership = Start(MembershipRole.Manager);
        memberships.Changes.Add(MembershipChange.Of(membership, manager, pharmacy, MembershipVersion.From(2), MembershipRole.Employee));

        var result = await HandleAsync(7);

        ShouldBeRefused(result, HttpStatusCode.Forbidden, "certification.capability-missing");
    }

    [Fact]
    public async Task A_caller_who_is_no_person_is_forbidden_and_nothing_is_stored()
    {
        Start(MembershipRole.Manager);

        var result = await HandleAsync(Option<PharmacyId>.Some(pharmacy), 7, Option<PersonId>.None);

        ShouldBeRefused(result, HttpStatusCode.Forbidden, "certification.identity-incomplete");
    }

    [Fact]
    public async Task The_empty_pharmacy_id_is_not_found_and_nothing_is_stored()
    {
        var result = await HandleAsync(Option<PharmacyId>.None, 7, Option<PersonId>.Some(manager));

        ShouldBeRefused(result, HttpStatusCode.NotFound, "certification.pharmacy-not-found");
    }

    [Fact]
    public async Task A_caller_who_is_no_person_is_forbidden_before_the_pharmacy_is_looked_at()
    {
        var result = await HandleAsync(Option<PharmacyId>.None, 7, Option<PersonId>.None);

        ShouldBeRefused(result, HttpStatusCode.Forbidden, "certification.identity-incomplete");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(0)]
    [InlineData(null)]
    public async Task A_period_outside_two_to_thirty_years_is_refused_and_nothing_is_stored(int? years)
    {
        Start(MembershipRole.Manager);

        var result = await HandleAsync(years);

        ShouldBeRefused(result, HttpStatusCode.BadRequest, "certification.invalid-retention-period");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(30)]
    public async Task The_shortest_and_the_longest_period_are_stored(int years)
    {
        Start(MembershipRole.Manager);

        var result = await HandleAsync(years);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : string.Empty);
        retention.Stored[pharmacy].Period.ShouldBe(RetentionPeriod.From(years));
    }

    [Theory]
    [InlineData(31)]
    [InlineData(null)]
    public async Task A_caller_who_is_no_person_is_forbidden_whatever_the_period(int? years)
    {
        Start(MembershipRole.Manager);

        var result = await HandleAsync(Option<PharmacyId>.Some(pharmacy), years, Option<PersonId>.None);

        ShouldBeRefused(result, HttpStatusCode.Forbidden, "certification.identity-incomplete");
    }

    [Theory]
    [InlineData(31)]
    [InlineData(null)]
    public async Task The_empty_pharmacy_id_is_not_found_whatever_the_period(int? years)
    {
        var result = await HandleAsync(Option<PharmacyId>.None, years, Option<PersonId>.Some(manager));

        ShouldBeRefused(result, HttpStatusCode.NotFound, "certification.pharmacy-not-found");
    }

    [Theory]
    [InlineData(31)]
    [InlineData(null)]
    public async Task Someone_who_is_no_member_is_told_the_pharmacy_is_not_found_whatever_the_period(int? years)
    {
        Start(MembershipRole.Manager, PharmacyId.From(Guid.CreateVersion7()));

        var result = await HandleAsync(years);

        ShouldBeRefused(result, HttpStatusCode.NotFound, "certification.pharmacy-not-found");
    }

    [Theory]
    [InlineData(31)]
    [InlineData(null)]
    public async Task An_employee_is_forbidden_whatever_the_period(int? years)
    {
        Start(MembershipRole.Employee);

        var result = await HandleAsync(years);

        ShouldBeRefused(result, HttpStatusCode.Forbidden, "certification.capability-missing");
    }

    private Task<Result<Unit, ApiError>> HandleAsync(int? years) =>
        HandleAsync(Option<PharmacyId>.Some(pharmacy), years, Option<PersonId>.Some(manager));

    private Task<Result<Unit, ApiError>> HandleAsync(Option<PharmacyId> where, int? years, Option<PersonId> caller) =>
        new SetRetentionPeriodHandler(memberships, retention)
            .HandleAsync(new SetRetentionPeriod(where, RequestedRetentionYears.From(years), caller), TestContext.Current.CancellationToken);

    private MembershipId Start(MembershipRole role, PharmacyId? where = null)
    {
        var id = MembershipId.From(Guid.CreateVersion7());
        memberships.Memberships.Add(Membership.Started(id, manager, where ?? pharmacy, HolderName.From("Anna Berger"), role, Timestamp.From(LongAgo)));
        return id;
    }

    private void ShouldBeRefused(Result<Unit, ApiError> result, HttpStatusCode status, string code)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        result.Error.Status.ShouldBe(status);
        retention.Stored.ShouldBeEmpty();
    }
}
