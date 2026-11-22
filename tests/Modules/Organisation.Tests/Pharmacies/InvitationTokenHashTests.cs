using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>An invitation token is stored only as its SHA-256, in lower-case hex: 64 characters, nothing else.</summary>
public class InvitationTokenHashTests
{
    private const string Vector = "3997fb18327685ae1af7d75d3be692f98a12d699b5f4a00b0d17c4d425d694b5";

    [Fact]
    public void The_hash_of_a_token_is_its_sha256_in_lower_case_hex() =>
        InvitationTokenHash.Of(InvitationToken.From("invitation-token-0123456789abcdef")).Value.ShouldBe(Vector);

    [Fact]
    public void Two_different_tokens_have_different_hashes() =>
        InvitationTokenHash.Of(InvitationToken.From("invitation-token-0123456789abcdef"))
            .ShouldNotBe(InvitationTokenHash.Of(InvitationToken.From("invitation-token-fedcba9876543210")));

    [Theory]
    [InlineData(63)]
    [InlineData(65)]
    public void A_hash_of_any_other_length_is_refused(int length) =>
        Should.Throw<GuardException>(() => InvitationTokenHash.From(new string('a', length)));

    [Theory]
    [InlineData("3997FB18327685AE1AF7D75D3BE692F98A12D699B5F4A00B0D17C4D425D694B5")]
    [InlineData("3997fb18327685ae1af7d75d3be692f98a12d699b5f4a00b0d17c4d425d694bZ")]
    [InlineData("3997fb18327685ae1af7d75d3be692f98a12d699b5f4a00b0d17c4d425d694b ")]
    public void A_hash_that_is_not_lower_case_hex_is_refused(string value) =>
        Should.Throw<GuardException>(() => InvitationTokenHash.From(value));

    [Fact]
    public void A_stored_hash_read_back_is_the_hash_of_its_token()
    {
        var hash = InvitationTokenHash.Of(InvitationToken.From("invitation-token-0123456789abcdef"));

        InvitationTokenHash.From(hash.Value).ShouldBe(hash);
        InvitationTokenHash.From(Vector).ShouldBe(hash);
    }
}
