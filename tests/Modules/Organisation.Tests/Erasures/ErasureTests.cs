using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Erasures;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Erasures;

/// <summary>The erasure log's entry: requested by support, its confirmation handled once, each module's step recorded once.</summary>
public class ErasureTests
{
    private static readonly PersonId Support = PersonId.From(Guid.CreateVersion7());
    private static readonly Timestamp RequestedAt = Timestamp.From(new DateTimeOffset(2027, 5, 3, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_requested_erasure_is_pending_with_no_steps()
    {
        var id = ErasureId.New();

        var erasure = Erasure.Request(id, Support, RequestedAt);

        erasure.Id.ShouldBe(id);
        erasure.RequestedBy.ShouldBe(Support);
        erasure.RequestedAt.ShouldBe(RequestedAt);
        erasure.Confirmation.ShouldBe(ErasureConfirmation.Pending);
        erasure.ConfirmationHandledAt.ShouldBeNull();
        erasure.Steps.ShouldBeEmpty();
    }

    [Fact]
    public void A_step_completed_twice_is_recorded_once()
    {
        var erasure = Erasure.Request(ErasureId.New(), Support, RequestedAt);

        erasure.CompleteStep(ErasureModule.Organisation, RequestedAt.PlusDays(1));
        erasure.CompleteStep(ErasureModule.Organisation, RequestedAt.PlusDays(2));

        var step = erasure.Steps.ShouldHaveSingleItem();
        step.Module.ShouldBe(ErasureModule.Organisation);
        step.CompletedAt.ShouldBe(RequestedAt.PlusDays(1));
    }

    [Fact]
    public void Steps_of_different_modules_are_each_recorded()
    {
        var erasure = Erasure.Request(ErasureId.New(), Support, RequestedAt);

        erasure.CompleteStep(ErasureModule.Organisation, RequestedAt.PlusDays(1));
        erasure.CompleteStep(ErasureModule.From("compliance"), RequestedAt.PlusDays(2));

        erasure.Steps.Select(step => step.Module.Value).ShouldBe(["organisation", "compliance"], ignoreOrder: true);
    }

    [Fact]
    public void The_confirmation_is_handled_once()
    {
        var erasure = Erasure.Request(ErasureId.New(), Support, RequestedAt);

        erasure.ConfirmationHandled(ErasureConfirmation.Mailed, RequestedAt.PlusDays(1));
        erasure.ConfirmationHandled(ErasureConfirmation.NoAddress, RequestedAt.PlusDays(2));

        erasure.Confirmation.ShouldBe(ErasureConfirmation.Mailed);
        erasure.ConfirmationHandledAt.ShouldBe(RequestedAt.PlusDays(1));
    }

    [Theory]
    [InlineData("organisation")]
    [InlineData("compliance")]
    [InlineData("certification")]
    [InlineData("a")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmn")]
    public void An_erasure_module_is_lower_case_letters(string name)
    {
        ErasureModule.From(name).Value.ShouldBe(name);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Organisation")]
    [InlineData("compliance1")]
    [InlineData("key-cloak")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmno")]
    public void Anything_else_is_no_erasure_module(string name)
    {
        Should.Throw<GuardException>(() => ErasureModule.From(name));
    }

    [Fact]
    public void The_organisation_module_is_named_organisation()
    {
        ErasureModule.Organisation.Value.ShouldBe("organisation");
        ErasureModule.Organisation.ShouldBe(ErasureModule.From("organisation"));
    }

    /// <summary>#115 slice 6: the Keycloak account's consumer asks whether its step is recorded before it calls Keycloak.</summary>
    [Fact]
    public void HasCompleted_is_true_once_the_step_is_recorded_and_false_before()
    {
        var erasure = Erasure.Request(ErasureId.New(), Support, RequestedAt);
        erasure.CompleteStep(ErasureModule.Organisation, RequestedAt.PlusDays(1));

        erasure.HasCompleted(ErasureModule.Keycloak).ShouldBeFalse("the keycloak step is not recorded yet");
        erasure.HasCompleted(ErasureModule.Organisation).ShouldBeTrue("the organisation step is recorded");

        erasure.CompleteStep(ErasureModule.Keycloak, RequestedAt.PlusDays(2));

        erasure.HasCompleted(ErasureModule.Keycloak).ShouldBeTrue("the keycloak step is recorded");
        erasure.HasCompleted(ErasureModule.From("keycloak")).ShouldBeTrue("a module is the same module by its name");
    }

    [Fact]
    public void An_erasure_id_is_never_empty()
    {
        Should.Throw<GuardException>(() => ErasureId.From(Guid.Empty));
        var id = Guid.CreateVersion7();
        ErasureId.From(id).Value.ShouldBe(id);
        ErasureId.New().ShouldNotBe(ErasureId.New());
    }
}
