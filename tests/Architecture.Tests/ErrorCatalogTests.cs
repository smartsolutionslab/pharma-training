using SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes.Errors;
using SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests;

/// <summary>The error rules and the export's rows, on probe error types outside every module.</summary>
public class ErrorCatalogTests
{
    [Fact]
    public void A_german_message_is_named_as_not_english()
    {
        var violations = ErrorMessagesAreEnglish.Violations([typeof(ProbeGermanError)]).ToList();

        var violation = violations.ShouldHaveSingleItem();
        violation.ShouldContain(typeof(ProbeGermanError).FullName!);
        violation.ShouldContain("probe.german");
        violation.ShouldContain("Die Probe fehlt.");
        violation.ShouldContain("not English");
    }

    [Fact]
    public void A_message_naming_its_id_is_named_as_reading_its_arguments()
    {
        var violations = EveryErrorCodeHasOneMessageAndStatus.Violations([typeof(ProbeErrorNamingItsId)]).ToList();

        var violation = violations.ShouldHaveSingleItem();
        violation.ShouldContain(typeof(ProbeErrorNamingItsId).FullName!);
        violation.ShouldContain("probe.naming-its-id");
        violation.ShouldContain("reads its constructor's arguments");
    }

    [Fact]
    public void Two_types_sharing_a_code_with_two_messages_are_named_together()
    {
        var violations = EveryErrorCodeHasOneMessageAndStatus.Violations([typeof(ProbeTwinA), typeof(ProbeTwinB)]).ToList();

        var violation = violations.ShouldHaveSingleItem();
        violation.ShouldContain("probe.twin has 2 messages");
        violation.ShouldContain($"{typeof(ProbeTwinA).FullName}: \"The probe is missing.\"");
        violation.ShouldContain($"{typeof(ProbeTwinB).FullName}: \"There is no such probe.\"");
    }

    [Fact]
    public void A_message_interpolating_a_constant_passes_every_rule()
    {
        ErrorMessagesAreEnglish.Violations([typeof(ProbeConstantMaximum)]).ShouldBeEmpty();
        EveryErrorCodeHasOneMessageAndStatus.Violations([typeof(ProbeConstantMaximum)]).ShouldBeEmpty();
    }

    [Fact]
    public void An_api_error_is_exported_with_its_message_and_status()
    {
        var entries = ErrorCatalog.Entries([typeof(ProbeConstantMaximum)]);

        entries.ShouldHaveSingleItem().ShouldBe(new ErrorCatalogEntry("probe.constant-maximum", 400, "Ask for at most 3 probes."));
    }

    [Fact]
    public void A_domain_error_no_failures_method_maps_is_named_as_having_no_status()
    {
        ErrorCatalog.Entries([typeof(ProbeGermanError)]).ShouldBeEmpty();
        ErrorCatalog.WithoutStatus([typeof(ProbeGermanError), typeof(ProbeConstantMaximum)]).ShouldHaveSingleItem().Code.ShouldBe("probe.german");
    }

    [Fact]
    public void The_export_is_an_array_of_code_status_and_message_sorted_by_code_ending_in_one_line_feed()
    {
        var exported = ErrorCatalog.Export([new ErrorCatalogEntry("b.second", 409, "It's `b`."), new ErrorCatalogEntry("a.first", 400, "A <first>.")]);

        exported.ShouldBe(
            """
            [
              {
                "code": "a.first",
                "status": 400,
                "message": "A <first>."
              },
              {
                "code": "b.second",
                "status": 409,
                "message": "It's `b`."
              }
            ]

            """.ReplaceLineEndings("\n"));
    }
}
