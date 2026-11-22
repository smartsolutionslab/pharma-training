using System.Buffers.Text;
using System.Security.Cryptography;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>A caller's token is checked with <see cref="InvitationToken.TryFrom"/> before anything else reads it, and is never trimmed.</summary>
public class InvitationTokenTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_callers_blank_token_is_no_token_and_no_exception(string? value)
    {
        InvitationToken.TryFrom(value, out var token).ShouldBeFalse();
        token.ShouldBeNull();
    }

    [Theory]
    [InlineData(31)]
    [InlineData(129)]
    public void A_callers_token_shorter_than_32_or_longer_than_128_characters_is_no_token(int length)
    {
        InvitationToken.TryFrom(new string('a', length), out var token).ShouldBeFalse();
        token.ShouldBeNull();
    }

    [Fact]
    public void A_callers_token_of_43_url_safe_characters_is_a_token()
    {
        var value = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        value.Length.ShouldBe(43);

        InvitationToken.TryFrom(value, out var token).ShouldBeTrue();

        token.ShouldBe(InvitationToken.From(value));
        token.Value.ShouldBe(value);
    }

    [Fact]
    public void A_callers_token_is_kept_exactly_and_not_trimmed()
    {
        var value = " " + new string('a', 40) + " ";

        InvitationToken.TryFrom(value, out var token).ShouldBeTrue();

        token.Value.ShouldBe(value);
    }

    [Fact]
    public void A_token_has_at_most_128_characters()
    {
        InvitationToken.MaxLength.ShouldBe(128);
        InvitationToken.From(new string('a', 128)).Value.Length.ShouldBe(128);
        InvitationToken.TryFrom(new string('a', 128), out _).ShouldBeTrue();
        Should.Throw<GuardException>(() => InvitationToken.From(new string('a', 129)));
    }
}
