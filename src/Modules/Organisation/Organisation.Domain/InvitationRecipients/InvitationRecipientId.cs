using System.Security.Cryptography;
using System.Text;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;

/// <summary>
/// An invitation recipient's identity, derived from the address so the address itself is never stored (docs/adr/0010
/// style, docs/adr/0004): a name-based UUID version 8 (RFC 9562, section 5.8) over SHA-256 of Organisation's fixed
/// namespace (16 bytes, big-endian) followed by the UTF-8 bytes of the address as stored (trimmed, lower-case). The
/// first 16 bytes of the hash, with the version nibble set to 8 and the variant bits to 10, read big-endian. The
/// namespace and the layout never change: every stored recipient's id was derived with them.
/// </summary>
public sealed record InvitationRecipientId : IValueObject<Guid>
{
    private static readonly Guid Namespace = new("3f9c2a71-8d4e-4b06-a5e2-7c1d9b0f6e38");

    private InvitationRecipientId(Guid value) => Value = value;

    public Guid Value { get; }

    /// <summary>The recipient of the address: the same for every spelling of it.</summary>
    public static InvitationRecipientId For(EmailAddress address)
    {
        Ensure.That(address).IsNotNull();

        var name = Encoding.UTF8.GetBytes(address.Value);
        var input = new byte[16 + name.Length];
        Namespace.TryWriteBytes(input.AsSpan(0, 16), bigEndian: true, out _);
        name.CopyTo(input, 16);
        var uuid = SHA256.HashData(input).AsSpan(0, 16).ToArray();
        uuid[6] = (byte)((uuid[6] & 0x0F) | 0x80);
        uuid[8] = (byte)((uuid[8] & 0x3F) | 0x80);
        return new InvitationRecipientId(new Guid(uuid, bigEndian: true));
    }

    /// <summary>A stored id read back; an empty Guid is never one. There is no <c>New()</c>: an id is always derived.</summary>
    public static InvitationRecipientId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new InvitationRecipientId(value);
    }

    public override string ToString() => Value.ToString();
}
