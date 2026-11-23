using System.Security.Cryptography;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

/// <summary>
/// An examination's identity and the id of its event stream. The examination of a learning session has the id
/// <see cref="For"/> derives from the session, so two concurrent first openings collide on one stream (docs/adr/0012).
/// An empty Guid is never one.
/// </summary>
public sealed record ExaminationId : IValueObject<Guid>, IAggregateId
{
    // Assessment's namespace for derived examination ids (RFC 9562, section 5.8). It must never change: every stored
    // examination's id was derived with it.
    private static readonly Guid Namespace = new("03b091d0-68d3-44ab-8dd0-8d6ac1bff3aa");

    private ExaminationId(Guid value) => Value = value;

    public Guid Value { get; }

    public static ExaminationId New() => new(Guid.CreateVersion7());

    public static ExaminationId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new ExaminationId(value);
    }

    /// <summary>
    /// The id of the session's one examination: a name-based UUID version 8 (RFC 9562), the first 16 bytes of the
    /// SHA-256 of Assessment's namespace and the session, both big-endian, with the version and variant set.
    /// </summary>
    public static ExaminationId For(LearningSessionId session)
    {
        Ensure.That(session).IsNotNull();

        Span<byte> name = stackalloc byte[32];
        Namespace.TryWriteBytes(name[..16], bigEndian: true, out _);
        session.Value.TryWriteBytes(name[16..], bigEndian: true, out _);

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(name, hash);
        var uuid = hash[..16];
        uuid[6] = (byte)((uuid[6] & 0x0F) | 0x80);
        uuid[8] = (byte)((uuid[8] & 0x3F) | 0x80);
        return new ExaminationId(new Guid(uuid, bigEndian: true));
    }

    public override string ToString() => Value.ToString();
}
