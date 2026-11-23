using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Memberships;

/// <summary>Certification's copy of a started membership: who it is, where, under which name and since when; stored once.</summary>
public class MembershipStartedV1ConsumerTests
{
    [Fact]
    public async Task A_started_membership_is_stored_with_every_field_once_the_unit_of_work_commits()
    {
        var store = new InMemoryMembershipStore();
        var membershipId = Guid.CreateVersion7();
        var personId = Guid.CreateVersion7();
        var pharmacyId = Guid.CreateVersion7();
        var startedAt = new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.FromHours(1));

        await new MembershipStartedV1Consumer(store, store).HandleAsync(MembershipEvents.Started(membershipId, personId, pharmacyId, "Ada Lovelace", startedAt), TestContext.Current.CancellationToken);

        var membership = store.Memberships.ShouldHaveSingleItem();
        membership.Id.ShouldBe(MembershipId.From(membershipId));
        membership.Person.ShouldBe(PersonId.From(personId));
        membership.Pharmacy.ShouldBe(PharmacyId.From(pharmacyId));
        membership.HolderName.ShouldBe(HolderName.From("Ada Lovelace"));
        membership.StartedAt.ShouldBe(Timestamp.From(startedAt));
        store.Endings.ShouldBeEmpty();
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_membership_delivered_twice_is_stored_once_as_it_first_arrived_and_nothing_more_is_committed()
    {
        var store = new InMemoryMembershipStore();
        var consumer = new MembershipStartedV1Consumer(store, store);
        var first = MembershipEvents.Started(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Ada Lovelace");

        await consumer.HandleAsync(first, TestContext.Current.CancellationToken);
        await consumer.HandleAsync(first, TestContext.Current.CancellationToken);

        store.Memberships.ShouldHaveSingleItem().HolderName.ShouldBe(HolderName.From("Ada Lovelace"));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_membership_published_again_under_a_new_identifier_and_another_name_changes_nothing()
    {
        var store = new InMemoryMembershipStore();
        var consumer = new MembershipStartedV1Consumer(store, store);
        var first = MembershipEvents.Started(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Ada Lovelace");
        var again = MembershipEvents.Started(first.MembershipId, first.PersonId, first.PharmacyId, "Ada Byron", first.StartedAt.AddDays(1));
        again.Metadata.EventIdentifier.ShouldNotBe(first.Metadata.EventIdentifier);

        await consumer.HandleAsync(first, TestContext.Current.CancellationToken);
        await consumer.HandleAsync(again, TestContext.Current.CancellationToken);

        var membership = store.Memberships.ShouldHaveSingleItem();
        membership.HolderName.ShouldBe(HolderName.From("Ada Lovelace"));
        membership.StartedAt.ShouldBe(Timestamp.From(first.StartedAt));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task One_person_in_two_pharmacies_is_stored_as_two_memberships()
    {
        var store = new InMemoryMembershipStore();
        var consumer = new MembershipStartedV1Consumer(store, store);
        var personId = Guid.CreateVersion7();

        await consumer.HandleAsync(MembershipEvents.Started(Guid.CreateVersion7(), personId, Guid.CreateVersion7(), "Ada"), TestContext.Current.CancellationToken);
        await consumer.HandleAsync(MembershipEvents.Started(Guid.CreateVersion7(), personId, Guid.CreateVersion7(), "Ada"), TestContext.Current.CancellationToken);

        store.Memberships.Count.ShouldBe(2);
        store.Memberships.ShouldAllBe(membership => membership.Person == PersonId.From(personId));
    }

    [Theory]
    [InlineData(nameof(MembershipStartedV1.MembershipId))]
    [InlineData(nameof(MembershipStartedV1.PersonId))]
    [InlineData(nameof(MembershipStartedV1.PharmacyId))]
    public async Task An_empty_id_is_refused_by_its_name_and_nothing_stored(string field)
    {
        var store = new InMemoryMembershipStore();
        var started = MembershipEvents.Started(
            field == nameof(MembershipStartedV1.MembershipId) ? Guid.Empty : Guid.CreateVersion7(),
            field == nameof(MembershipStartedV1.PersonId) ? Guid.Empty : Guid.CreateVersion7(),
            field == nameof(MembershipStartedV1.PharmacyId) ? Guid.Empty : Guid.CreateVersion7(),
            "Ada");

        await RefusedAsync(store, started, field);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ada\0Lovelace")]
    public async Task A_missing_or_blank_display_name_or_one_holding_a_nul_character_is_refused_by_its_name_and_nothing_stored(string? displayName)
    {
        var store = new InMemoryMembershipStore();
        var started = MembershipEvents.Started(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), displayName!);

        await RefusedAsync(store, started, nameof(MembershipStartedV1.DisplayName));
    }

    [Fact]
    public async Task A_display_name_as_long_as_organisation_allows_is_stored()
    {
        var store = new InMemoryMembershipStore();
        var displayName = new string('a', 100);

        await new MembershipStartedV1Consumer(store, store).HandleAsync(MembershipEvents.Started(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), displayName), TestContext.Current.CancellationToken);

        store.Memberships.ShouldHaveSingleItem().HolderName.ShouldBe(HolderName.From(displayName));
    }

    [Fact]
    public async Task A_display_name_longer_than_organisation_allows_is_refused_by_its_name_and_nothing_stored()
    {
        var store = new InMemoryMembershipStore();
        var started = MembershipEvents.Started(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), new string('a', 101));

        await RefusedAsync(store, started, nameof(MembershipStartedV1.DisplayName));
    }

    [Fact]
    public async Task A_job_function_or_role_certification_does_not_keep_is_not_checked()
    {
        var store = new InMemoryMembershipStore();
        var started = MembershipEvents.Started(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Ada") with
        {
            JobFunction = (JobFunctionV1)99,
            Role = (MembershipRoleV1)99,
        };

        await new MembershipStartedV1Consumer(store, store).HandleAsync(started, TestContext.Current.CancellationToken);

        store.Memberships.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task No_message_is_refused_and_nothing_stored()
    {
        var store = new InMemoryMembershipStore();

        await Should.ThrowAsync<GuardException>(() => new MembershipStartedV1Consumer(store, store).HandleAsync(null!, TestContext.Current.CancellationToken));

        store.Memberships.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    private static async Task RefusedAsync(InMemoryMembershipStore store, MembershipStartedV1 started, string field)
    {
        var refused = await Should.ThrowAsync<GuardException>(() => new MembershipStartedV1Consumer(store, store).HandleAsync(started, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(field);
        store.Memberships.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }
}
