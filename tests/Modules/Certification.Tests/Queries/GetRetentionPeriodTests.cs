using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Queries.GetRetentionPeriod;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Retention;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Queries;

/// <summary>
/// #391: the pharmacy's manager reads its retention period: the years it chose, or five and that it is the default
/// while it chose none. The caller is checked as for setting it (docs/adr/0003).
/// </summary>
public class GetRetentionPeriodTests
{
    private static readonly DateTimeOffset LongAgo = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly InMemoryMembershipStore memberships = new();
    private readonly InMemoryPharmacyRetentionStore retention = new();
    private readonly PharmacyId pharmacy = PharmacyId.From(Guid.CreateVersion7());
    private readonly PersonId manager = PersonId.From(Guid.CreateVersion7());

    [Fact]
    public async Task A_pharmacy_that_chose_nothing_keeps_the_default_of_five_years()
    {
        Start(MembershipRole.Manager);

        var result = await HandleAsync(pharmacy.Value, manager.Value);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : string.Empty);
        result.Value.ShouldBe(new CertificationRetentionPeriodDto(5, true));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(5)]
    public async Task A_pharmacy_that_chose_a_period_keeps_it_and_it_is_no_default(int years)
    {
        Start(MembershipRole.Manager);
        retention.Keep(pharmacy, years);

        var result = await HandleAsync(pharmacy.Value, manager.Value);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : string.Empty);
        result.Value.ShouldBe(new CertificationRetentionPeriodDto(years, false));
    }

    [Fact]
    public async Task A_caller_who_is_no_person_is_forbidden()
    {
        Start(MembershipRole.Manager);

        var result = await HandleAsync(pharmacy.Value, Guid.Empty);

        ShouldBeRefused(result, HttpStatusCode.Forbidden, "certification.identity-incomplete");
    }

    [Fact]
    public async Task The_empty_pharmacy_id_is_not_found()
    {
        var result = await HandleAsync(Guid.Empty, manager.Value);

        ShouldBeRefused(result, HttpStatusCode.NotFound, "certification.pharmacy-not-found");
    }

    [Fact]
    public async Task An_employee_is_forbidden()
    {
        Start(MembershipRole.Employee);

        var result = await HandleAsync(pharmacy.Value, manager.Value);

        ShouldBeRefused(result, HttpStatusCode.Forbidden, "certification.capability-missing");
    }

    [Fact]
    public async Task Someone_who_is_no_member_is_told_the_pharmacy_is_not_found()
    {
        Start(MembershipRole.Manager, PharmacyId.From(Guid.CreateVersion7()));

        var result = await HandleAsync(pharmacy.Value, manager.Value);

        ShouldBeRefused(result, HttpStatusCode.NotFound, "certification.pharmacy-not-found");
    }

    private Task<Result<CertificationRetentionPeriodDto, ApiError>> HandleAsync(Guid pharmacyId, Guid caller) =>
        new GetRetentionPeriodHandler(memberships, retention)
            .HandleAsync(new GetRetentionPeriod(pharmacyId, caller), TestContext.Current.CancellationToken);

    private void Start(MembershipRole role, PharmacyId? where = null) =>
        memberships.Memberships.Add(Membership.Started(MembershipId.From(Guid.CreateVersion7()), manager, where ?? pharmacy, HolderName.From("Anna Berger"), role, Timestamp.From(LongAgo)));

    private static void ShouldBeRefused(Result<CertificationRetentionPeriodDto, ApiError> result, HttpStatusCode status, string code)
    {
        result.IsFailure.ShouldBeTrue(result.IsSuccess ? $"answered {result.Value}" : string.Empty);
        result.Error.Code.ShouldBe(code);
        result.Error.Status.ShouldBe(status);
    }
}
