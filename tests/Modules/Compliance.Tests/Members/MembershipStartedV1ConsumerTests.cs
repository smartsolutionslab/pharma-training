using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Members;

public class MembershipStartedV1ConsumerTests
{
    [Fact]
    public async Task A_started_membership_is_stored_with_every_field_once_the_unit_of_work_commits()
    {
        var store = new InMemoryMemberStore();
        var membershipId = Guid.CreateVersion7();
        var personId = Guid.CreateVersion7();
        var pharmacyId = Guid.CreateVersion7();
        var started = MembershipEvents.Started(membershipId, personId, pharmacyId, "Ada Lovelace", JobFunctionV1.Pharmacist, MembershipRoleV1.Manager);

        await new MembershipStartedV1Consumer(store, store).HandleAsync(started, TestContext.Current.CancellationToken);

        var member = store.Members.ShouldHaveSingleItem();
        member.Id.ShouldBe(MembershipId.From(membershipId));
        member.Person.ShouldBe(PersonId.From(personId));
        member.Pharmacy.ShouldBe(PharmacyId.From(pharmacyId));
        member.Name.ShouldBe(MemberName.From("Ada Lovelace"));
        member.JobFunction.ShouldBe(JobFunction.Pharmacist);
        member.Role.ShouldBe(MembershipRole.Manager);
        store.Endings.ShouldBeEmpty();
        store.Commits.ShouldBe(1);
    }

    [Theory]
    [InlineData(JobFunctionV1.Pharmacist, JobFunction.Pharmacist)]
    [InlineData(JobFunctionV1.Pta, JobFunction.Pta)]
    [InlineData(JobFunctionV1.Pka, JobFunction.Pka)]
    [InlineData(JobFunctionV1.Trainee, JobFunction.Trainee)]
    [InlineData(JobFunctionV1.Other, JobFunction.Other)]
    public async Task Each_job_function_is_stored_as_its_own(JobFunctionV1 sent, JobFunction stored)
    {
        var store = new InMemoryMemberStore();
        var started = MembershipEvents.Started(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Ada", sent, MembershipRoleV1.Employee);

        await new MembershipStartedV1Consumer(store, store).HandleAsync(started, TestContext.Current.CancellationToken);

        store.Members.ShouldHaveSingleItem().JobFunction.ShouldBe(stored);
    }

    [Theory]
    [InlineData(MembershipRoleV1.Manager, MembershipRole.Manager)]
    [InlineData(MembershipRoleV1.Employee, MembershipRole.Employee)]
    public async Task Each_role_is_stored_as_its_own(MembershipRoleV1 sent, MembershipRole stored)
    {
        var store = new InMemoryMemberStore();
        var started = MembershipEvents.Started(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Ada", JobFunctionV1.Pta, sent);

        await new MembershipStartedV1Consumer(store, store).HandleAsync(started, TestContext.Current.CancellationToken);

        store.Members.ShouldHaveSingleItem().Role.ShouldBe(stored);
    }

    [Fact]
    public async Task A_membership_delivered_twice_is_stored_once_as_it_first_arrived_and_nothing_more_is_committed()
    {
        var store = new InMemoryMemberStore();
        var consumer = new MembershipStartedV1Consumer(store, store);
        var first = MembershipEvents.Started(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Ada Lovelace");

        await consumer.HandleAsync(first, TestContext.Current.CancellationToken);
        await consumer.HandleAsync(MembershipEvents.Started(first.MembershipId, first.PersonId, first.PharmacyId, "Ada Byron", JobFunctionV1.Other, MembershipRoleV1.Manager), TestContext.Current.CancellationToken);

        var member = store.Members.ShouldHaveSingleItem();
        member.Name.ShouldBe(MemberName.From("Ada Lovelace"));
        member.JobFunction.ShouldBe(JobFunction.Pta);
        member.Role.ShouldBe(MembershipRole.Employee);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task One_person_in_two_pharmacies_is_stored_as_two_members()
    {
        var store = new InMemoryMemberStore();
        var consumer = new MembershipStartedV1Consumer(store, store);
        var personId = Guid.CreateVersion7();

        await consumer.HandleAsync(MembershipEvents.Started(Guid.CreateVersion7(), personId, Guid.CreateVersion7(), "Ada"), TestContext.Current.CancellationToken);
        await consumer.HandleAsync(MembershipEvents.Started(Guid.CreateVersion7(), personId, Guid.CreateVersion7(), "Ada"), TestContext.Current.CancellationToken);

        store.Members.Count.ShouldBe(2);
        store.Members.ShouldAllBe(member => member.Person == PersonId.From(personId));
        store.Members.Select(member => member.Pharmacy).Distinct().Count().ShouldBe(2);
    }

    [Theory]
    [InlineData(nameof(MembershipStartedV1.MembershipId))]
    [InlineData(nameof(MembershipStartedV1.PersonId))]
    [InlineData(nameof(MembershipStartedV1.PharmacyId))]
    public async Task An_empty_id_is_refused_by_its_name_and_nothing_stored(string field)
    {
        var store = new InMemoryMemberStore();
        var started = MembershipEvents.Started(
            field == nameof(MembershipStartedV1.MembershipId) ? Guid.Empty : Guid.CreateVersion7(),
            field == nameof(MembershipStartedV1.PersonId) ? Guid.Empty : Guid.CreateVersion7(),
            field == nameof(MembershipStartedV1.PharmacyId) ? Guid.Empty : Guid.CreateVersion7(),
            "Ada");

        var refused = await Should.ThrowAsync<GuardException>(() => new MembershipStartedV1Consumer(store, store).HandleAsync(started, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(field);
        store.Members.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ada\0Lovelace")]
    public async Task A_missing_or_blank_display_name_or_one_holding_a_nul_character_is_refused_by_its_name_and_nothing_stored(string? displayName)
    {
        var store = new InMemoryMemberStore();
        var started = MembershipEvents.Started(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), displayName!);

        var refused = await Should.ThrowAsync<GuardException>(() => new MembershipStartedV1Consumer(store, store).HandleAsync(started, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(nameof(MembershipStartedV1.DisplayName));
        store.Members.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_display_name_longer_than_organisation_allows_today_is_still_stored()
    {
        var store = new InMemoryMemberStore();
        var displayName = new string('a', 500);

        await new MembershipStartedV1Consumer(store, store).HandleAsync(MembershipEvents.Started(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), displayName), TestContext.Current.CancellationToken);

        store.Members.ShouldHaveSingleItem().Name.ShouldBe(MemberName.From(displayName));
    }

    [Fact]
    public async Task An_undefined_job_function_is_refused_by_its_name_and_nothing_stored()
    {
        var store = new InMemoryMemberStore();
        var started = MembershipEvents.Started(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Ada", (JobFunctionV1)99, MembershipRoleV1.Employee);

        var refused = await Should.ThrowAsync<GuardException>(() => new MembershipStartedV1Consumer(store, store).HandleAsync(started, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(nameof(MembershipStartedV1.JobFunction));
        store.Members.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task An_undefined_role_is_refused_by_its_name_and_nothing_stored()
    {
        var store = new InMemoryMemberStore();
        var started = MembershipEvents.Started(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Ada", JobFunctionV1.Pta, (MembershipRoleV1)99);

        var refused = await Should.ThrowAsync<GuardException>(() => new MembershipStartedV1Consumer(store, store).HandleAsync(started, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(nameof(MembershipStartedV1.Role));
        store.Members.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task No_message_is_refused_and_nothing_stored()
    {
        var store = new InMemoryMemberStore();

        await Should.ThrowAsync<GuardException>(() => new MembershipStartedV1Consumer(store, store).HandleAsync(null!, TestContext.Current.CancellationToken));

        store.Members.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }
}
