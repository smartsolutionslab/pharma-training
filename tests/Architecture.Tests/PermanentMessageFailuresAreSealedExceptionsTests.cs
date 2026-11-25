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

    /// <summary>A struct can never be an exception, so the marker on one is named like on any other non-exception (#401's review).</summary>
    [Fact]
    public void A_marker_on_a_struct_is_named()
    {
        var violation = PermanentMessageFailuresAreSealedExceptions.Violations([typeof(ProbePermanentFailureStruct)]).ShouldHaveSingleItem();

        violation.ShouldContain(typeof(ProbePermanentFailureStruct).FullName!);
        violation.ShouldContain("no exception");
    }

    /// <summary>
    /// The rule looks at every assembly of the project's code, not only the modules' (#401's review): a marker in
    /// BuildingBlocks, Contracts, the Kernel, ServiceDefaults or src/Host (compiled into each service host) is checked too.
    /// The probes live in this test assembly, which no module is, so a model that holds it only as a project assembly
    /// still has them named.
    /// </summary>
    [Fact]
    public void A_marker_outside_the_modules_is_named()
    {
        var model = ArchitectureModel.Of(moduleAssemblies: [], projectAssemblies: [typeof(ProbeOpenPermanentFailure).Assembly]);

        var violations = new PermanentMessageFailuresAreSealedExceptions().Violations(model).ToList();

        violations.ShouldContain(violation => violation.Contains(typeof(ProbeOpenPermanentFailure).FullName!));
        violations.ShouldContain(violation => violation.Contains(typeof(ProbeWrappingPermanentFailure).FullName!));
        violations.ShouldContain(violation => violation.Contains(typeof(ProbePermanentFailureMarker).FullName!));
        violations.ShouldContain(violation => violation.Contains(typeof(ProbePermanentFailureStruct).FullName!));
    }

    [Fact]
    public void The_project_assemblies_hold_every_layer_the_architecture_tests_load()
    {
        var names = ArchitectureModel.Load().ProjectAssemblies.Select(assembly => assembly.GetName().Name).ToList();

        names.ShouldContain("BuildingBlocks");
        names.ShouldContain("Contracts");
        names.ShouldContain("ServiceDefaults");
        names.ShouldContain("SmartSolutionsLab.PharmaTraining.Kernel");
        names.ShouldContain("SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain");
        names.ShouldContain("SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application");
        names.ShouldContain("SmartSolutionsLab.PharmaTraining.Services.Organisation.Host");
    }

    [Fact]
    public void Types_without_the_marker_are_not_looked_at() =>
        PermanentMessageFailuresAreSealedExceptions.Violations([typeof(InvalidOperationException), typeof(ProbePaperOption)]).ShouldBeEmpty();
}
