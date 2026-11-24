using System.Globalization;
using System.Text.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The follow-up of a recurring assignment (#31), end to end: the certificate that completes a yearly assignment,
/// delivered through the broker, leads to one new open assignment of the same learner, pharmacy and course, due one
/// year after the completion's day in Germany, assigned by the original assigner, announced as <c>CourseAssignedV1</c>.
/// The same certificate again, under a new envelope or a new event identifier, makes no second one and leaves it open
/// (docs/adr/0026).
/// </summary>
public class ComplianceFollowUpTests(AspireFixture aspire)
{
    private readonly ComplianceFollowUps followUps = new(aspire);

    [Fact]
    public async Task A_certificate_completes_a_yearly_assignment_and_a_follow_up_appears_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var recurring = await followUps.AssignYearlyAsync(cancellationToken);

        // Assigning announced the assignment, so its exchange exists: a queue of the test's own hears the follow-up's.
        await using var connection = await Broker.ConnectAsync(aspire, cancellationToken);
        await Broker.WaitForExchangeAsync(connection, ComplianceFollowUps.AssignedExchange, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var queue = await Broker.BindQueueAsync(channel, ComplianceFollowUps.AssignedExchange, cancellationToken);

        var issued = await followUps.IssueAsync(recurring, cancellationToken);

        var followUp = await followUps.WaitForFollowUpAsync(recurring.Assignment, cancellationToken);
        followUp.DueDate.ShouldBe(ComplianceFollowUps.DayInGermany(issued.IssuedAt).AddMonths(12));
        followUp.Status.ShouldBe("Open");
        followUp.RecurrenceMonths.ShouldBe(12);
        followUp.AssignedBy.ShouldBe(recurring.AssignedBy);
        followUp.AssignedAt.ShouldBeGreaterThanOrEqualTo(issued.IssuedAt);
        (await followUps.AssignmentsAsync(recurring, cancellationToken))[recurring.Assignment].ShouldBe("Completed");

        var announced = await Broker.ReceiveAsync<JsonElement>(
            channel,
            queue,
            received => received.GetProperty("assignmentId").GetGuid() == followUp.Id,
            cancellationToken);
        announced.GetProperty("personId").GetGuid().ShouldBe(recurring.Learner);
        announced.GetProperty("pharmacyId").GetGuid().ShouldBe(recurring.Pharmacy);
        announced.GetProperty("courseId").GetGuid().ShouldBe(recurring.Course);
        DateOnly.Parse(announced.GetProperty("dueDate").GetString().ShouldNotBeNull(), CultureInfo.InvariantCulture).ShouldBe(followUp.DueDate);

        // The same certificate again under another envelope id, so the inbox lets it through to the consumer, then
        // published again by Certification under an event identifier of its own.
        await followUps.DeliverAsync(ComplianceFollowUps.IssuedType, issued, cancellationToken);
        await followUps.DeliverAsync(ComplianceFollowUps.IssuedType, issued with { Metadata = ComplianceFollowUps.NewMetadata() }, cancellationToken);
        await followUps.WaitUntilNoFollowUpRequestIsPendingAsync(cancellationToken);

        (await followUps.FollowUpCountAsync(recurring.Assignment, cancellationToken)).ShouldBe(1);
        var rows = await followUps.AssignmentsAsync(recurring, cancellationToken);
        rows.Count.ShouldBe(2);
        rows[recurring.Assignment].ShouldBe("Completed");
        rows[followUp.Id].ShouldBe("Open");
    }
}
