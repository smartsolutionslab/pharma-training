using SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;
using SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests;

/// <summary>
/// A permanent message failure skips the retry ladder, so the marker belongs only on a failure certain to fail the same
/// way on every retry: a sealed exception that wraps no other, which might be transient (#198's review).
/// </summary>
public class PermanentMessageFailuresAreSealedExceptionsTests
{
    [Fact]
    public void A_sealed_exception_without_an_inner_exception_passes() =>
        PermanentMessageFailuresAreSealedExceptions.Violations([typeof(ProbePermanentFailure)]).ShouldBeEmpty();

    [Fact]
    public void A_permanent_failure_that_is_not_sealed_is_named()
    {
        var violation = PermanentMessageFailuresAreSealedExceptions.Violations([typeof(ProbeOpenPermanentFailure)]).ShouldHaveSingleItem();

        violation.ShouldContain(typeof(ProbeOpenPermanentFailure).FullName!);
        violation.ShouldContain("not sealed");
    }

    [Fact]
    public void A_permanent_failure_that_can_wrap_an_inner_exception_is_named()
    {
        var violation = PermanentMessageFailuresAreSealedExceptions.Violations([typeof(ProbeWrappingPermanentFailure)]).ShouldHaveSingleItem();

        violation.ShouldContain(typeof(ProbeWrappingPermanentFailure).FullName!);
        violation.ShouldContain("inner exception");
    }

    [Fact]
    public void A_marker_on_something_that_is_no_exception_is_named()
    {
        var violation = PermanentMessageFailuresAreSealedExceptions.Violations([typeof(ProbePermanentFailureMarker)]).ShouldHaveSingleItem();

        violation.ShouldContain(typeof(ProbePermanentFailureMarker).FullName!);
        violation.ShouldContain("no exception");
    }

    [Fact]
    public void Types_without_the_marker_are_not_looked_at() =>
        PermanentMessageFailuresAreSealedExceptions.Violations([typeof(InvalidOperationException), typeof(ProbePaperOption)]).ShouldBeEmpty();
}
