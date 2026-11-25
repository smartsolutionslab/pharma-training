using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Erasures;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Erasures;

/// <summary>
/// Editorial announced that it has done its part of an erasure (#115): Organisation records the step
/// <c>editorial</c> once, at its own clock. A redelivery adds nothing, and an erasure Organisation does not know is
/// logged and dropped.
/// </summary>
public class EditorialErasureCompletedV1ConsumerTests
{
    private static readonly PersonId Support = PersonId.From(Guid.CreateVersion7());
    private static readonly DateTimeOffset Start = new(2027, 5, 3, 10, 0, 0, TimeSpan.Zero);
    private static readonly ErasureModule Editorial = ErasureModule.From("editorial");

    private readonly InMemoryPharmacyStore store = new();
    private readonly FakeTimeProvider time = new(Start);
    private readonly FakeLogger<EditorialErasureCompletedV1Consumer> logger = new();

    [Fact]
    public async Task The_editorial_step_is_recorded_at_organisations_clock_once_committed()
    {
        var erasure = Logged();
        erasure.CompleteStep(ErasureModule.Organisation, Timestamp.From(Start));
        time.Advance(TimeSpan.FromMinutes(3));

        await Consumer().HandleAsync(new EditorialErasureCompletedV1(erasure.Id.Value), TestContext.Current.CancellationToken);

        erasure.Steps.Select(step => step.Module).ShouldBe([ErasureModule.Organisation, Editorial]);
        erasure.Steps.Single(step => step.Module == Editorial).CompletedAt.ShouldBe(Timestamp.From(Start.AddMinutes(3)));
        store.Commits.ShouldBe(1);
        store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_completion_delivered_twice_is_recorded_once_as_it_first_arrived()
    {
        var erasure = Logged();
        var completed = new EditorialErasureCompletedV1(erasure.Id.Value);
        await Consumer().HandleAsync(completed, TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMinutes(5));

        await Consumer().HandleAsync(completed, TestContext.Current.CancellationToken);

        erasure.Steps.ShouldHaveSingleItem().CompletedAt.ShouldBe(Timestamp.From(Start));
        erasure.Steps.ShouldHaveSingleItem().Module.ShouldBe(Editorial);
    }

    [Fact]
    public async Task An_unknown_erasure_is_logged_and_dropped()
    {
        var unknown = Guid.CreateVersion7();

        await Consumer().HandleAsync(new EditorialErasureCompletedV1(unknown), TestContext.Current.CancellationToken);

        store.Commits.ShouldBe(0);
        store.StoredErasures.ShouldBeEmpty();
        var logged = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        logged.Level.ShouldBe(LogLevel.Warning);
        logged.Message.ShouldContain(unknown.ToString());
    }

    [Fact]
    public async Task An_empty_erasure_id_is_refused_by_its_name_and_nothing_committed()
    {
        var refused = await Should.ThrowAsync<GuardException>(() => Consumer().HandleAsync(new EditorialErasureCompletedV1(Guid.Empty), TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(nameof(EditorialErasureCompletedV1.ErasureId));
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task No_message_is_refused_and_nothing_committed()
    {
        await Should.ThrowAsync<GuardException>(() => Consumer().HandleAsync(null!, TestContext.Current.CancellationToken));

        store.Commits.ShouldBe(0);
    }

    private EditorialErasureCompletedV1Consumer Consumer() => new(store, store, time, logger);

    private Erasure Logged()
    {
        var erasure = Erasure.Request(ErasureId.New(), Support, Timestamp.From(Start));
        store.StoredErasures.Add(erasure);
        return erasure;
    }
}
