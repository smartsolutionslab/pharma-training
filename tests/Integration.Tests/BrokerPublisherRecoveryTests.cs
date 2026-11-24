using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// An envelope a host left in its durable inbox is recovered when the host starts again, and checked like any other
/// (docs/adr/0011, D5): its publisher header and where it came from must survive Wolverine's storage, or the recovered
/// legitimate event is refused as forged. The envelope is handed back as a node that died while handling it leaves it,
/// while Compliance is stopped.
/// </summary>
[Collection(nameof(ComplianceRestart))]
public class BrokerPublisherRecoveryTests(AspireFixture aspire)
{
    private const string ComplianceResource = "compliance";

    private const string MembershipStarted = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipStartedV1";

    private const string MembersById = "select count(*) from compliance.members where id = @id";

    private static readonly TimeSpan HandledTimeout = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task A_legitimate_membership_left_in_the_inbox_is_handled_after_compliance_restarts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var started = new StartedEvent(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            $"Ada Lovelace {Guid.CreateVersion7()}",
            "Pta",
            "Employee",
            TimeProvider.System.GetUtcNow(),
            new EventMetadata(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow()));
        var messageId = Guid.CreateVersion7();
        await ComplianceDatabase.SendAsync(aspire, MembershipStarted, started, messageId, cancellationToken);
        await ComplianceDatabase.WaitUntilHandledAsync(aspire, messageId, cancellationToken);

        await StopComplianceAsync(cancellationToken);
        try
        {
            await ComplianceDatabase.ReturnToInboxAsync(aspire, messageId, cancellationToken);
            await StartComplianceAsync(cancellationToken);

            await ComplianceDatabase.WaitUntilHandledUnlessForgedAsync(aspire, messageId, HandledTimeout, cancellationToken);
            (await ComplianceDatabase.CountAsync(aspire, MembersById, started.MembershipId, cancellationToken))
                .ShouldBe(1, $"Compliance holds the membership {started.MembershipId} other than once after recovering it.");
        }
        finally
        {
            await StartComplianceAsync(CancellationToken.None);
        }
    }

    private async Task StopComplianceAsync(CancellationToken cancellationToken)
    {
        var stopped = await aspire.App.ResourceCommands.ExecuteCommandAsync(ComplianceResource, KnownResourceCommands.StopCommand, cancellationToken);
        stopped.Success.ShouldBeTrue($"Compliance did not stop: {stopped.Message}");
    }

    /// <summary>Starts Compliance, unless it runs already, and waits until it is healthy.</summary>
    private async Task StartComplianceAsync(CancellationToken cancellationToken)
    {
        await aspire.App.ResourceCommands.ExecuteCommandAsync(ComplianceResource, KnownResourceCommands.StartCommand, cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        await aspire.App.ResourceNotifications.WaitForResourceHealthyAsync(ComplianceResource, timeout.Token);
    }

    private sealed record EventMetadata(Guid EventIdentifier, DateTimeOffset OccurredAt);

    private sealed record StartedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, DateTimeOffset StartedAt, EventMetadata Metadata);
}
