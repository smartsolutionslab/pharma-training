using System.Buffers.Binary;
using System.Security.Cryptography;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

/// <summary>
/// A learning session's identity and the id of its event stream. A session started for a learner, a course version
/// and a run has the id <see cref="For"/> derives from them, so two concurrent first starts collide on one stream
/// (docs/adr/0010). An empty Guid is never one.
/// </summary>
public sealed record LearningSessionId : IValueObject<Guid>, IAggregateId
{
    // Learning's namespace for derived session ids (RFC 9562, section 5.8). It must never change: every stored
    // session's id was derived with it.
    private static readonly Guid Namespace = new("5c1e8a4b-2f0d-4e7a-9b3c-6d1f0a2e8c47");

    private LearningSessionId(Guid value) => Value = value;

    public Guid Value { get; }

    public static LearningSessionId New() => new(Guid.CreateVersion7());

    public static LearningSessionId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new LearningSessionId(value);
    }

    /// <summary>
    /// The id of the learner's run of the course version: a name-based UUID version 8 (RFC 9562), the first 16 bytes
    /// of the SHA-256 of Learning's namespace, the learner, the version and the run, with the version and variant set.
    /// </summary>
    public static LearningSessionId For(PersonId learner, CourseVersionId version, SessionRun run)
    {
        Ensure.That(learner).IsNotNull();
        Ensure.That(version).IsNotNull();
        Ensure.That(run).IsNotNull();

        Span<byte> name = stackalloc byte[52];
        Namespace.TryWriteBytes(name[..16], bigEndian: true, out _);
        learner.Value.TryWriteBytes(name[16..32], bigEndian: true, out _);
        version.Value.TryWriteBytes(name[32..48], bigEndian: true, out _);
        BinaryPrimitives.WriteInt32BigEndian(name[48..], run.Value);

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(name, hash);
        var uuid = hash[..16];
        uuid[6] = (byte)((uuid[6] & 0x0F) | 0x80);
        uuid[8] = (byte)((uuid[8] & 0x3F) | 0x80);
        return new LearningSessionId(new Guid(uuid, bigEndian: true));
    }

    public override string ToString() => Value.ToString();
}
