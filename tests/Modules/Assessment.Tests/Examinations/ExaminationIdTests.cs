using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Examinations;

public class ExaminationIdTests
{
    private static readonly LearningSessionId Session = LearningSessionId.From(Guid.CreateVersion7());

    [Fact]
    public void An_examination_id_is_derived_from_its_session_as_a_version_8_uuid()
    {
        var id = ExaminationId.For(Session);

        id.Value.Version.ShouldBe(8);
        (id.Value.Variant & 0b1100).ShouldBe(0b1000, "the variant bits of an RFC 9562 UUID are 10xx");
        id.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void The_same_session_always_derives_the_same_examination_id() =>
        ExaminationId.For(Session).ShouldBe(ExaminationId.For(LearningSessionId.From(Session.Value)));

    [Fact]
    public void Two_sessions_derive_two_examination_ids() =>
        ExaminationId.For(Session).ShouldNotBe(ExaminationId.For(LearningSessionId.From(Guid.CreateVersion7())));

    [Fact]
    public void The_derived_id_is_not_the_sessions_own_guid() =>
        ExaminationId.For(Session).Value.ShouldNotBe(Session.Value);

    /// <summary>
    /// Fixes Assessment's namespace (03b091d0-68d3-44ab-8dd0-8d6ac1bff3aa) and the byte layout forever: the first 16
    /// bytes of SHA-256(namespace ‖ session, both big-endian), version nibble 8, variant 10. Every stored examination's
    /// stream id was derived this way, so this vector never changes.
    /// </summary>
    [Fact]
    public void A_known_session_derives_a_known_examination_id() =>
        ExaminationId.For(LearningSessionId.From(new Guid("0192f5d4-7c3a-7b2e-9a41-5e6f7d8c9b0a")))
            .ShouldBe(ExaminationId.From(new Guid("a4743f21-f877-839d-befa-d536a329cd5a")));

    [Fact]
    public void A_new_examination_id_is_a_version_7_uuid() =>
        ExaminationId.New().Value.Version.ShouldBe(7);

    [Fact]
    public void An_empty_guid_is_no_examination_id() =>
        Should.Throw<GuardException>(() => ExaminationId.From(Guid.Empty));
}
