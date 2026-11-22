using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Courses;

public class CourseValueObjectTests
{
    [Fact]
    public void A_course_id_comes_back_from_its_value()
    {
        var value = Guid.CreateVersion7();

        var id = CourseId.From(value);

        id.Value.ShouldBe(value);
        id.ToString().ShouldBe(value.ToString());
        id.ShouldBe(CourseId.From(value));
    }

    [Fact]
    public void A_course_version_id_comes_back_from_its_value()
    {
        var value = Guid.CreateVersion7();

        var id = CourseVersionId.From(value);

        id.Value.ShouldBe(value);
        id.ToString().ShouldBe(value.ToString());
        id.ShouldBe(CourseVersionId.From(value));
    }

    [Fact]
    public void An_empty_guid_is_no_course_id() =>
        Should.Throw<GuardException>(() => CourseId.From(Guid.Empty));

    [Fact]
    public void An_empty_guid_is_no_course_version_id() =>
        Should.Throw<GuardException>(() => CourseVersionId.From(Guid.Empty));

    [Fact]
    public void A_title_holds_its_value_as_editorial_sent_it()
    {
        var title = CourseTitle.From(" Kühlkette in der Apotheke ");

        title.Value.ShouldBe(" Kühlkette in der Apotheke ");
        title.ToString().ShouldBe(" Kühlkette in der Apotheke ");
        title.ShouldBe(CourseTitle.From(" Kühlkette in der Apotheke "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Kühl\0kette")]
    public void A_missing_or_blank_title_or_one_holding_a_nul_character_is_refused(string? value) =>
        Should.Throw<GuardException>(() => CourseTitle.From(value!));

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    public void A_version_number_from_one_up_holds_its_value(int value)
    {
        var number = VersionNumber.From(value);

        number.Value.ShouldBe(value);
        number.ShouldBe(VersionNumber.From(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_version_number_below_one_is_refused(int value) =>
        Should.Throw<GuardException>(() => VersionNumber.From(value));

    [Fact]
    public void Version_numbers_compare_by_their_value()
    {
        VersionNumber.From(1).CompareTo(VersionNumber.From(2)).ShouldBeLessThan(0);
        VersionNumber.From(2).CompareTo(VersionNumber.From(1)).ShouldBeGreaterThan(0);
        VersionNumber.From(2).CompareTo(VersionNumber.From(2)).ShouldBe(0);
        VersionNumber.From(1).CompareTo(null).ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(24)]
    public void A_validity_of_one_month_or_more_holds_its_value(int value)
    {
        var validity = ValidityMonths.From(value);

        validity.Value.ShouldBe(value);
        validity.ShouldBe(ValidityMonths.From(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-12)]
    public void A_validity_below_one_month_is_refused(int value) =>
        Should.Throw<GuardException>(() => ValidityMonths.From(value));
}
