using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

public class ValueObjectTests
{
    [Fact]
    public void A_pharmacy_name_is_trimmed()
    {
        var name = PharmacyName.From("  Stadt-Apotheke  ");

        name.Value.ShouldBe("Stadt-Apotheke");
        name.ShouldBe(PharmacyName.From("Stadt-Apotheke"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_pharmacy_name_is_refused(string value) =>
        Should.Throw<GuardException>(() => PharmacyName.From(value));

    [Fact]
    public void A_pharmacy_name_of_120_characters_is_a_name_and_one_of_121_is_refused()
    {
        PharmacyName.MaxLength.ShouldBe(120);
        PharmacyName.From(new string('a', 120)).Value.Length.ShouldBe(120);
        Should.Throw<GuardException>(() => PharmacyName.From(new string('a', 121)));
    }

    [Fact]
    public void A_callers_pharmacy_name_becomes_a_trimmed_name()
    {
        PharmacyName.TryFrom("  Stadt-Apotheke ", out var name).ShouldBeTrue();
        name.ShouldBe(PharmacyName.From("Stadt-Apotheke"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_callers_blank_pharmacy_name_is_no_name_and_no_exception(string? value)
    {
        PharmacyName.TryFrom(value, out var name).ShouldBeFalse();
        name.ShouldBeNull();
    }

    [Fact]
    public void A_callers_pharmacy_name_of_121_characters_is_no_name() =>
        PharmacyName.TryFrom(new string('a', 121), out _).ShouldBeFalse();

    [Fact]
    public void An_email_address_is_kept_lower_case_and_trimmed()
    {
        var address = EmailAddress.From(" Anna@Example.ORG ");

        address.Value.ShouldBe("anna@example.org");
        address.ShouldBe(EmailAddress.From("anna@example.org"));
    }

    [Theory]
    [InlineData("no-at")]
    [InlineData("")]
    [InlineData("anna@example")]
    public void Something_that_is_no_email_address_is_refused(string value) =>
        Should.Throw<GuardException>(() => EmailAddress.From(value));

    [Fact]
    public void A_callers_email_address_becomes_a_lower_case_address()
    {
        EmailAddress.TryFrom(" Anna@Example.ORG", out var address).ShouldBeTrue();
        address.ShouldBe(EmailAddress.From("anna@example.org"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-at")]
    public void A_callers_value_that_is_no_email_address_is_no_address_and_no_exception(string? value)
    {
        EmailAddress.TryFrom(value, out var address).ShouldBeFalse();
        address.ShouldBeNull();
    }

    [Fact]
    public void An_email_address_longer_than_254_characters_is_refused()
    {
        var tooLong = new string('a', 250) + "@x.de";

        Should.Throw<GuardException>(() => EmailAddress.From(tooLong));
        EmailAddress.TryFrom(tooLong, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_display_name_is_trimmed_and_at_most_100_characters()
    {
        DisplayName.From(" Olaf Owner ").Value.ShouldBe("Olaf Owner");
        DisplayName.From(new string('a', 100)).Value.Length.ShouldBe(100);
        Should.Throw<GuardException>(() => DisplayName.From(new string('a', 101)));
        Should.Throw<GuardException>(() => DisplayName.From("  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void A_callers_blank_display_name_is_no_name_and_no_exception(string? value)
    {
        DisplayName.TryFrom(value, out var name).ShouldBeFalse();
        name.ShouldBeNull();
    }

    [Fact]
    public void A_callers_display_name_becomes_a_trimmed_name_up_to_100_characters()
    {
        DisplayName.TryFrom(" Olaf Owner", out var name).ShouldBeTrue();
        name.ShouldBe(DisplayName.From("Olaf Owner"));
        DisplayName.TryFrom(new string('a', 101), out _).ShouldBeFalse();
    }

    [Fact]
    public void An_invitation_token_has_at_least_32_characters()
    {
        InvitationToken.From(new string('a', 32)).Value.ShouldBe(new string('a', 32));
        Should.Throw<GuardException>(() => InvitationToken.From(new string('a', 31)));
    }

    [Fact]
    public void New_identities_are_version_7_guids()
    {
        PharmacyId.New().Value.Version.ShouldBe(7);
        MembershipId.New().Value.Version.ShouldBe(7);
    }

    [Fact]
    public void Identities_come_back_from_their_values()
    {
        var value = Guid.CreateVersion7();

        PharmacyId.From(value).Value.ShouldBe(value);
        MembershipId.From(value).Value.ShouldBe(value);
        PersonId.From(value).Value.ShouldBe(value);
        PharmacyId.From(value).ToString().ShouldBe(value.ToString());
        MembershipId.From(value).ToString().ShouldBe(value.ToString());
        PersonId.From(value).ToString().ShouldBe(value.ToString());
    }

    [Fact]
    public void An_empty_guid_is_no_identity()
    {
        Should.Throw<GuardException>(() => PharmacyId.From(Guid.Empty));
        Should.Throw<GuardException>(() => MembershipId.From(Guid.Empty));
        Should.Throw<GuardException>(() => PersonId.From(Guid.Empty));
    }

    [Fact]
    public void A_timestamp_is_kept_in_utc()
    {
        var timestamp = Timestamp.From(new DateTimeOffset(2027, 3, 1, 10, 0, 0, TimeSpan.FromHours(1)));

        timestamp.Value.Offset.ShouldBe(TimeSpan.Zero);
        timestamp.Value.ShouldBe(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_timestamp_moves_on_by_days_and_compares_by_its_instant()
    {
        var now = Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));

        var later = now.PlusDays(14);

        later.Value.ShouldBe(new DateTimeOffset(2027, 3, 15, 9, 0, 0, TimeSpan.Zero));
        now.CompareTo(later).ShouldBeLessThan(0);
        later.CompareTo(now).ShouldBeGreaterThan(0);
        now.CompareTo(Timestamp.From(new DateTimeOffset(2027, 3, 1, 10, 0, 0, TimeSpan.FromHours(1)))).ShouldBe(0);
    }
}
