using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using NSubstitute.ExceptionExtensions;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Erasures;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Erasures;

/// <summary>
/// #115 slice 6: Organisation deletes the erased person's Keycloak account and records the step <c>keycloak</c> at its
/// own clock. An account that is gone counts as deleted; once the step is recorded, Keycloak is not asked again; a
/// client's service account is never deleted; a failure of Keycloak records nothing and rethrows, so Wolverine retries.
/// </summary>
public class AccountDeletionRequestedV1ConsumerTests
{
    private static readonly PersonId Support = PersonId.From(Guid.CreateVersion7());
    private static readonly DateTimeOffset Start = new(2027, 5, 3, 10, 0, 0, TimeSpan.Zero);

    private readonly InMemoryPharmacyStore store = new();
    private readonly IAccounts accounts = Substitute.For<IAccounts>();
    private readonly FakeTimeProvider time = new(Start);
    private readonly FakeLogger<AccountDeletionRequestedV1Consumer> logger = new();
    private readonly PersonId person = PersonId.From(Guid.CreateVersion7());

    [Fact]
    public async Task The_account_is_deleted_and_the_keycloak_step_recorded_at_now()
    {
        var erasure = Forgotten();
        accounts.FindAsync(person, Arg.Any<CancellationToken>()).Returns(Option<Account>.Some(AnAccount()));
        time.Advance(TimeSpan.FromMinutes(4));

        await Consumer().HandleAsync(new AccountDeletionRequestedV1(erasure.Id.Value, person.Value), TestContext.Current.CancellationToken);

        await accounts.Received(1).DeleteAsync(person, Arg.Any<CancellationToken>());
        erasure.Steps.Select(step => step.Module).ShouldBe([ErasureModule.Organisation, ErasureModule.Keycloak]);
        erasure.Steps.Single(step => step.Module == ErasureModule.Keycloak).CompletedAt.ShouldBe(Timestamp.From(Start.AddMinutes(4)));
        store.Commits.ShouldBe(1);
        store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_missing_account_records_the_step_without_deleting()
    {
        var erasure = Forgotten();
        accounts.FindAsync(person, Arg.Any<CancellationToken>()).Returns(Option<Account>.None);

        await Consumer().HandleAsync(new AccountDeletionRequestedV1(erasure.Id.Value, person.Value), TestContext.Current.CancellationToken);

        await accounts.DidNotReceiveWithAnyArgs().DeleteAsync(default!, TestContext.Current.CancellationToken);
        erasure.HasCompleted(ErasureModule.Keycloak).ShouldBeTrue("an account Keycloak does not have is deleted already");
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_recorded_step_asks_keycloak_nothing()
    {
        var erasure = Forgotten();
        erasure.CompleteStep(ErasureModule.Keycloak, Timestamp.From(Start));
        time.Advance(TimeSpan.FromHours(1));

        await Consumer().HandleAsync(new AccountDeletionRequestedV1(erasure.Id.Value, person.Value), TestContext.Current.CancellationToken);

        accounts.ReceivedCalls().ShouldBeEmpty("a redelivery after the step must not call Keycloak");
        erasure.Steps.Single(step => step.Module == ErasureModule.Keycloak).CompletedAt.ShouldBe(Timestamp.From(Start));
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_service_account_is_never_deleted_and_no_step_is_recorded()
    {
        var erasure = Forgotten();
        accounts.FindAsync(person, Arg.Any<CancellationToken>()).Returns(Option<Account>.Some(AnAccount() with { IsServiceAccount = true }));

        await Consumer().HandleAsync(new AccountDeletionRequestedV1(erasure.Id.Value, person.Value), TestContext.Current.CancellationToken);

        await accounts.DidNotReceiveWithAnyArgs().DeleteAsync(default!, TestContext.Current.CancellationToken);
        erasure.HasCompleted(ErasureModule.Keycloak).ShouldBeFalse("support must see the keycloak step missing");
        store.Commits.ShouldBe(0);
        var logged = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        logged.Level.ShouldBe(LogLevel.Error);
        logged.Message.ShouldContain(erasure.Id.Value.ToString());
        logged.Message.ShouldNotContain(person.Value.ToString());
    }

    [Theory]
    [InlineData("read")]
    [InlineData("delete")]
    public async Task A_failure_records_nothing_and_rethrows(string failing)
    {
        var erasure = Forgotten();
        var unavailable = new AccountsUnavailableException($"Keycloak answered the {failing} with 503.");
        if (failing == "read")
        {
            accounts.FindAsync(person, Arg.Any<CancellationToken>()).ThrowsAsync(unavailable);
        }
        else
        {
            accounts.FindAsync(person, Arg.Any<CancellationToken>()).Returns(Option<Account>.Some(AnAccount()));
            accounts.DeleteAsync(person, Arg.Any<CancellationToken>()).ThrowsAsync(unavailable);
        }

        var thrown = await Should.ThrowAsync<AccountsUnavailableException>(
            () => Consumer().HandleAsync(new AccountDeletionRequestedV1(erasure.Id.Value, person.Value), TestContext.Current.CancellationToken));

        thrown.ShouldBeSameAs(unavailable);
        erasure.HasCompleted(ErasureModule.Keycloak).ShouldBeFalse();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task An_unknown_erasure_is_logged_and_dropped()
    {
        var unknown = Guid.CreateVersion7();

        await Consumer().HandleAsync(new AccountDeletionRequestedV1(unknown, person.Value), TestContext.Current.CancellationToken);

        accounts.ReceivedCalls().ShouldBeEmpty("an erasure Organisation does not know deletes no account");
        store.Commits.ShouldBe(0);
        var logged = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        logged.Level.ShouldBe(LogLevel.Warning);
        logged.Message.ShouldContain(unknown.ToString());
        logged.Message.ShouldNotContain(person.Value.ToString());
    }

    [Theory]
    [InlineData(nameof(AccountDeletionRequestedV1.ErasureId))]
    [InlineData(nameof(AccountDeletionRequestedV1.PersonId))]
    public async Task An_empty_id_is_refused_by_its_name(string empty)
    {
        var erasure = Forgotten();
        var message = empty == nameof(AccountDeletionRequestedV1.ErasureId)
            ? new AccountDeletionRequestedV1(Guid.Empty, person.Value)
            : new AccountDeletionRequestedV1(erasure.Id.Value, Guid.Empty);

        var refused = await Should.ThrowAsync<GuardException>(() => Consumer().HandleAsync(message, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(empty);
        accounts.ReceivedCalls().ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    private static Account AnAccount() => new(Option<EmailAddress>.Some(EmailAddress.From("account@example.org")), IsServiceAccount: false);

    private AccountDeletionRequestedV1Consumer Consumer() => new(store, accounts, store, time, logger);

    /// <summary>The erasure as the mail consumer leaves it: logged, with Organisation's step done.</summary>
    private Erasure Forgotten()
    {
        var erasure = Erasure.Request(ErasureId.New(), Support, Timestamp.From(Start));
        erasure.CompleteStep(ErasureModule.Organisation, Timestamp.From(Start));
        store.StoredErasures.Add(erasure);
        return erasure;
    }
}
