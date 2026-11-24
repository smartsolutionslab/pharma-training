using System.Globalization;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

/// <summary>
/// A completion is late when its day in Germany lies after the due date, the last day on which the course counts as
/// done in time. The due date 31 March 2027 falls after the change to summer time (28 March), so Germany is UTC+2.
/// </summary>
public class DueDateLatenessTests
{
    private static readonly DueDate Due = DueDate.From(new DateOnly(2027, 3, 31));

    [Theory]
    [InlineData("2027-03-31T12:00:00Z", false)]
    [InlineData("2027-03-31T21:59:59Z", false)]
    [InlineData("2027-04-01T09:00:00Z", true)]
    [InlineData("2027-03-31T22:30:00Z", true)]
    [InlineData("2027-03-15T09:00:00Z", false)]
    public void A_completion_misses_the_due_date_only_on_a_later_day_in_Germany(string completedAt, bool missed)
    {
        var at = Timestamp.From(DateTimeOffset.Parse(completedAt, CultureInfo.InvariantCulture));

        Due.IsMissedBy(at).ShouldBe(missed);
    }

    [Fact]
    public void A_completion_a_minute_before_midnight_in_Germany_on_the_due_day_is_in_time()
    {
        var at = Timestamp.From(new DateTimeOffset(2027, 3, 31, 23, 59, 0, TimeSpan.FromHours(2)));

        Due.IsMissedBy(at).ShouldBeFalse();
    }

    [Fact]
    public void No_completion_instant_is_refused()
    {
        Should.Throw<GuardException>(() => Due.IsMissedBy(null!));
    }
}
