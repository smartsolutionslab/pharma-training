using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Memberships;

/// <summary>
/// The time a person worked in a pharmacy, by which an export picks their certificates (#52): from the start's instant on,
/// and up to but not including the instant the membership ended, since on that instant the person no longer works there.
/// </summary>
public class MembershipPeriodTests
{
    private static readonly PersonId Person = PersonId.From(Guid.CreateVersion7());
    private static readonly DateTimeOffset Start = new(2025, 3, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2025, 9, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void It_covers_the_instant_it_started()
    {
        Ended().Covers(Timestamp.From(Start)).ShouldBeTrue();
    }

    [Fact]
    public void It_does_not_cover_one_tick_before_it_started()
    {
        Ended().Covers(Timestamp.From(Start.AddTicks(-1))).ShouldBeFalse();
    }

    [Fact]
    public void It_covers_one_tick_before_it_ended()
    {
        Ended().Covers(Timestamp.From(End.AddTicks(-1))).ShouldBeTrue();
    }

    [Fact]
    public void It_does_not_cover_the_instant_it_ended()
    {
        Ended().Covers(Timestamp.From(End)).ShouldBeFalse();
    }

    [Fact]
    public void Without_an_end_it_covers_every_instant_from_its_start_on()
    {
        var period = MembershipPeriod.Of(Person, Timestamp.From(Start), null);

        period.Covers(Timestamp.From(Start)).ShouldBeTrue();
        period.Covers(Timestamp.From(new DateTimeOffset(2099, 12, 31, 23, 59, 59, TimeSpan.Zero))).ShouldBeTrue();
        period.Covers(Timestamp.From(Start.AddTicks(-1))).ShouldBeFalse();
    }

    [Fact]
    public void It_keeps_whose_it_is_and_when()
    {
        var period = Ended();

        period.Person.ShouldBe(Person);
        period.StartedAt.ShouldBe(Timestamp.From(Start));
        period.EndedAt.ShouldBe(Timestamp.From(End));
    }

    [Fact]
    public void A_period_without_a_person_or_a_start_is_refused()
    {
        Should.Throw<GuardException>(() => MembershipPeriod.Of(null!, Timestamp.From(Start), null));
        Should.Throw<GuardException>(() => MembershipPeriod.Of(Person, null!, null));
    }

    private static MembershipPeriod Ended() => MembershipPeriod.Of(Person, Timestamp.From(Start), Timestamp.From(End));
}
