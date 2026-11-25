using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Retention;

/// <summary>What the caller setting a retention period is told when it is refused (#391): the code and status are the contract, the message English and constant (docs/adr/0027).</summary>
public class RetentionFailuresTests
{
    [Fact]
    public void A_period_outside_the_range_is_a_bad_request()
    {
        var error = RetentionFailures.InvalidPeriod();

        error.Code.ShouldBe("certification.invalid-retention-period");
        error.Status.ShouldBe(HttpStatusCode.BadRequest);
        error.Message.ShouldBe("A retention period is a whole number of years from 2 to 30.");
    }
}
