using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Exports;

/// <summary>What the caller of an export is told when it is refused (#52): codes and statuses are the contract, the messages English and constant (docs/adr/0027).</summary>
public class ExportFailuresTests
{
    [Fact]
    public void Someone_without_an_active_membership_is_told_the_pharmacy_is_not_found()
    {
        var error = ExportFailures.PharmacyNotFound(Guid.CreateVersion7());

        error.Code.ShouldBe("certification.pharmacy-not-found");
        error.Status.ShouldBe(HttpStatusCode.NotFound);
        error.Message.ShouldBe("You are not a member of this pharmacy, or it does not exist.");
    }

    [Fact]
    public void A_member_whose_role_does_not_allow_it_is_forbidden()
    {
        var error = ExportFailures.CapabilityMissing();

        error.Code.ShouldBe("certification.capability-missing");
        error.Status.ShouldBe(HttpStatusCode.Forbidden);
        error.Message.ShouldBe("Your role in this pharmacy does not allow this.");
    }

    [Fact]
    public void A_period_outside_the_rules_is_a_bad_request()
    {
        var error = ExportFailures.InvalidPeriod();

        error.Code.ShouldBe("certification.invalid-export-period");
        error.Status.ShouldBe(HttpStatusCode.BadRequest);
        error.Message.ShouldBe("An export needs from and to, with to not before from and at most five years after it.");
    }

    [Fact]
    public void An_export_of_too_many_certificates_is_a_bad_request()
    {
        var error = ExportFailures.TooLarge();

        error.Code.ShouldBe("certification.export-too-large");
        error.Status.ShouldBe(HttpStatusCode.BadRequest);
        error.Message.ShouldBe("An export holds at most 2000 certificates; choose a shorter period.");
    }
}
