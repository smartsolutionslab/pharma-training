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
        var startSent = false;
        try
        {
            await ComplianceDatabase.ReturnToInboxAsync(aspire, messageId, cancellationToken);
            await SendStartAsync(cancellationToken);
            startSent = true;
            await WaitUntilComplianceIsHealthyAsync(cancellationToken);

            await ComplianceDatabase.WaitUntilHandledUnlessForgedAsync(aspire, messageId, HandledTimeout, cancellationToken);
            (await ComplianceDatabase.CountAsync(aspire, MembersById, started.MembershipId, cancellationToken))
                .ShouldBe(1, $"Compliance holds the membership {started.MembershipId} other than once after recovering it.");
        }
        finally
        {
            // Every other test needs Compliance, so it is started again even when this run was cancelled; the wait is
            // bounded by two minutes. Once the test sent the start itself, a second start would fail on a resource that is
            // already starting and hide the failure the test met, so it is sent only when the test never sent one.
            if (!startSent)
            {
                await SendStartAsync(CancellationToken.None);
                await WaitUntilComplianceIsHealthyAsync(CancellationToken.None);
            }
        }
    }

    private async Task StopComplianceAsync(CancellationToken cancellationToken)
    {
        var stopped = await aspire.App.ResourceCommands.ExecuteCommandAsync(ComplianceResource, KnownResourceCommands.StopCommand, cancellationToken);
        stopped.Success.ShouldBeTrue($"Compliance did not stop: {stopped.Message}");
    }

    /// <summary>Sends Compliance the start command, failing at once with the command's message when it is refused.</summary>
    private async Task SendStartAsync(CancellationToken cancellationToken)
    {
        var started = await aspire.App.ResourceCommands.ExecuteCommandAsync(ComplianceResource, KnownResourceCommands.StartCommand, cancellationToken);
        started.Success.ShouldBeTrue($"Compliance did not start: {started.Message}");
    }

    /// <summary>Waits at most two minutes until Compliance is healthy.</summary>
    private async Task WaitUntilComplianceIsHealthyAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        await aspire.App.ResourceNotifications.WaitForResourceHealthyAsync(ComplianceResource, timeout.Token);
    }

    private sealed record EventMetadata(Guid EventIdentifier, DateTimeOffset OccurredAt);

    private sealed record StartedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, DateTimeOffset StartedAt, EventMetadata Metadata);
}
