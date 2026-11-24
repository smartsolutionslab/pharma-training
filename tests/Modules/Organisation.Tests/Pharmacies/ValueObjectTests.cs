using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

public class ValueObjectTests
{
    [Fact]
    public void A_text_with_a_nul_character_is_no_name_and_no_address_because_the_database_cannot_hold_it()
    {
        PharmacyName.TryFrom("Stadt\0Apotheke", out _).ShouldBeFalse();
        DisplayName.TryFrom("Olaf\0Owner", out _).ShouldBeFalse();
        EmailAddress.TryFrom("owner\0@example.com", out _).ShouldBeFalse();
        Should.Throw<GuardException>(() => PharmacyName.From("Stadt\0Apotheke"));
        Should.Throw<GuardException>(() => DisplayName.From("Olaf\0Owner"));
        Should.Throw<GuardException>(() => EmailAddress.From("owner\0@example.com"));
    }

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

    [Theory]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\t")]
    [InlineData("\u0007")]
    [InlineData("\u001B")]
    [InlineData("\u007F")]
    [InlineData("\u0085")]
    [InlineData("\u202A")]
    [InlineData("\u202B")]
    [InlineData("\u202C")]
    [InlineData("\u202D")]
    [InlineData("\u202E")]
    [InlineData("\u2066")]
    [InlineData("\u2067")]
    [InlineData("\u2068")]
    [InlineData("\u2069")]
    public void A_pharmacy_name_with_a_control_or_bidi_character_is_refused_because_a_mail_or_screen_would_show_another_name(string character)
    {
        var value = $"Stadt{character}Apotheke";

        Should.Throw<GuardException>(() => PharmacyName.From(value));
        PharmacyName.TryFrom(value, out var name).ShouldBeFalse();
        name.ShouldBeNull();
    }

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

    [Theory]
    [InlineData("a<b@example.com")]
    [InlineData("a>b@example.com")]
    [InlineData("\"evil\"<a@b.c>")]
    [InlineData("a\"b@example.com")]
    [InlineData("a,b@example.com")]
    [InlineData("x@example.com,")]
    [InlineData("a;b@example.com")]
    [InlineData("a@example.com;b")]
    [InlineData("a(b)@example.com")]
    [InlineData("a@exam(ple.com")]
    [InlineData("a)b@example.com")]
    [InlineData("a[b]@example.com")]
    [InlineData("a@[example].com")]
    [InlineData("a\\b@example.com")]
    [InlineData("a:b@example.com")]
    [InlineData("a@example.com:25")]
    public void An_address_with_a_character_mail_reads_as_syntax_is_refused_because_it_would_reach_another_mailbox_or_none(string value)
    {
        Should.Throw<GuardException>(() => EmailAddress.From(value));
        EmailAddress.TryFrom(value, out var address).ShouldBeFalse();
        address.ShouldBeNull();
    }

    [Theory]
    [InlineData("anna+kurse@example.org")]
    [InlineData("anna.maria.schmidt@example.org")]
    [InlineData("anna@mail.apotheke.example.co.uk")]
    [InlineData("a.b-c_d+e@sub-domain.example")]
    public void An_address_with_plus_addressing_dots_or_subdomains_is_an_address(string value)
    {
        EmailAddress.From(value).Value.ShouldBe(value);
        EmailAddress.TryFrom(value, out var address).ShouldBeTrue();
        address.ShouldBe(EmailAddress.From(value));
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

    [Fact]
    public void A_membership_version_comes_back_from_its_value()
    {
        MembershipVersion.From(3).Value.ShouldBe(3);
        MembershipVersion.From(1).ShouldBe(MembershipVersion.First);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_membership_version_below_one_is_refused(int value) =>
        Should.Throw<GuardException>(() => MembershipVersion.From(value));

    [Fact]
    public void The_next_membership_version_is_one_higher()
    {
        MembershipVersion.First.Next().ShouldBe(MembershipVersion.From(2));
        MembershipVersion.From(2).Next().Value.ShouldBe(3);
    }

    [Fact]
    public void A_utc_day_is_the_utc_calendar_day_of_an_instant_and_starts_at_midnight_utc()
    {
        var lateInBerlin = Timestamp.From(new DateTimeOffset(2027, 4, 2, 0, 30, 0, TimeSpan.FromHours(2)));

        var day = UtcDay.Of(lateInBerlin);

        day.Value.ShouldBe(new DateOnly(2027, 4, 1));
        day.Start.ShouldBe(Timestamp.From(new DateTimeOffset(2027, 4, 1, 0, 0, 0, TimeSpan.Zero)));
        day.Next().ShouldBe(UtcDay.From(new DateOnly(2027, 4, 2)));
        day.Next().Start.ShouldBe(Timestamp.From(new DateTimeOffset(2027, 4, 2, 0, 0, 0, TimeSpan.Zero)));
        UtcDay.Of(Timestamp.From(new DateTimeOffset(2027, 4, 1, 23, 59, 59, TimeSpan.Zero))).ShouldBe(day);
    }

    [Fact]
    public void An_invitation_mail_count_starts_at_zero_counts_up_and_is_never_negative()
    {
        InvitationMailCount.Zero.Value.ShouldBe(0);
        InvitationMailCount.Zero.Next().ShouldBe(InvitationMailCount.From(1));
        InvitationMailCount.From(49).Next().Value.ShouldBe(50);
        Should.Throw<GuardException>(() => InvitationMailCount.From(-1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(1440)]
    public void A_renew_cooldown_of_a_minute_up_to_a_day_is_a_cooldown(int minutes) =>
        RenewCooldown.From(TimeSpan.FromMinutes(minutes)).Value.ShouldBe(TimeSpan.FromMinutes(minutes));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1441)]
    public void A_renew_cooldown_of_nothing_less_or_more_than_a_day_is_refused(int minutes) =>
        Should.Throw<GuardException>(() => RenewCooldown.From(TimeSpan.FromMinutes(minutes)));

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(10000)]
    public void A_daily_mail_cap_from_1_to_10000_is_a_cap(int mails) =>
        DailyMailCap.From(mails).Value.ShouldBe(mails);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10001)]
    public void A_daily_mail_cap_outside_1_to_10000_is_refused(int mails) =>
        Should.Throw<GuardException>(() => DailyMailCap.From(mails));
}
