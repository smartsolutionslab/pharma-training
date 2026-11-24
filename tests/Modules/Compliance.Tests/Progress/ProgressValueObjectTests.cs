using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Progress;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Progress;

/// <summary>A learning session's identity as Learning minted it: never the empty Guid (ADR 0028).</summary>
public class ProgressValueObjectTests
{
    [Fact]
    public void A_session_id_is_never_the_empty_guid()
    {
        Should.Throw<GuardException>(() => LearningSessionId.From(Guid.Empty));
    }

    [Fact]
    public void A_session_id_keeps_its_guid()
    {
        var value = Guid.CreateVersion7();

        var id = LearningSessionId.From(value);

        id.Value.ShouldBe(value);
        id.ToString().ShouldBe(value.ToString());
        id.ShouldBe(LearningSessionId.From(value));
    }
}
