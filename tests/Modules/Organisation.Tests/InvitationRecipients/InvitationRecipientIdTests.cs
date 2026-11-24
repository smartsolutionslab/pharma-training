using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.InvitationRecipients;

/// <summary>
/// A recipient's id is derived from the address (docs/adr/0010): a name-based UUID version 8 (RFC 9562) over SHA-256
/// of Organisation's fixed namespace and the address, so the address itself is never stored. The derivation must never
/// change: every stored recipient's id was derived with it.
/// </summary>
public class InvitationRecipientIdTests
{
    [Theory]
    [InlineData("anna@example.org", "fc1749a6-95e2-8b8c-b3ab-6896dff8d819")]
    [InlineData("sender@example.com", "7a04494b-ab30-8f69-a797-7813345d57f6")]
    public void The_id_of_an_address_is_derived_and_never_changes(string address, string expected) =>
        InvitationRecipientId.For(EmailAddress.From(address)).Value.ShouldBe(Guid.Parse(expected));

    [Fact]
    public void Two_spellings_of_one_address_are_one_recipient() =>
        InvitationRecipientId.For(EmailAddress.From(" Anna@Example.org ")).ShouldBe(InvitationRecipientId.For(EmailAddress.From("anna@example.org")));

    [Fact]
    public void Two_addresses_are_two_recipients() =>
        InvitationRecipientId.For(EmailAddress.From("anna@example.org")).ShouldNotBe(InvitationRecipientId.For(EmailAddress.From("ben@example.org")));

    [Fact]
    public void The_id_is_a_version_8_uuid_with_the_rfc_variant()
    {
        var id = InvitationRecipientId.For(EmailAddress.From("anna@example.org")).Value;

        id.Version.ShouldBe(8);
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes, bigEndian: true, out _).ShouldBeTrue();
        (bytes[8] & 0xC0).ShouldBe(0x80, "the variant bits are not 10");
    }

    [Fact]
    public void An_id_read_back_is_the_id_derived()
    {
        var derived = InvitationRecipientId.For(EmailAddress.From("anna@example.org"));

        InvitationRecipientId.From(derived.Value).ShouldBe(derived);
    }

    [Fact]
    public void The_empty_guid_is_no_recipient() =>
        Should.Throw<GuardException>(() => InvitationRecipientId.From(Guid.Empty));
}
