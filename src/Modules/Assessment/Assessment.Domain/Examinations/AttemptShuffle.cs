using System.Security.Cryptography;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

/// <summary>
/// The order of an attempt's questions and of each question's options, derived from the attempt's id (ADR 0022). Each
/// item's key is the SHA-256 of the attempt's Guid followed by the item's Guid, both written big-endian; the items are
/// ordered by their keys' bytes, unsigned and lexicographic, and a tie by the item's own big-endian bytes. The order
/// does not depend on the order the items are given in. The algorithm and its byte layout must never change: every
/// stored attempt's options would show in another order.
/// </summary>
public static class AttemptShuffle
{
    public static IReadOnlyList<QuestionVersionId> Questions(AttemptId attempt, IEnumerable<QuestionVersionId> questions) =>
        Order(attempt, questions, question => question.Value);

    public static IReadOnlyList<AnswerOptionId> Options(AttemptId attempt, IEnumerable<AnswerOptionId> options) =>
        Order(attempt, options, option => option.Value);

    private static List<T> Order<T>(AttemptId attempt, IEnumerable<T> items, Func<T, Guid> valueOf)
        where T : notnull
    {
        Ensure.That(attempt).IsNotNull();
        Ensure.That(items).IsNotNull();

        List<T> given = [.. items];
        Ensure.That(given.Select(valueOf).Distinct().Count(), nameof(items)).Satisfies(
            distinct => distinct == given.Count,
            "an attempt orders each item once; the items hold a duplicate.");

        return
        [
            .. given
                .Select(item => (Item: item, Key: KeyOf(attempt.Value, valueOf(item)), Bytes: BytesOf(valueOf(item))))
                .OrderBy(keyed => keyed.Key, ByteOrder.Instance)
                .ThenBy(keyed => keyed.Bytes, ByteOrder.Instance)
                .Select(keyed => keyed.Item),
        ];
    }

    private static byte[] KeyOf(Guid attempt, Guid item)
    {
        Span<byte> name = stackalloc byte[32];
        attempt.TryWriteBytes(name[..16], bigEndian: true, out _);
        item.TryWriteBytes(name[16..], bigEndian: true, out _);
        return SHA256.HashData(name);
    }

    private static byte[] BytesOf(Guid item)
    {
        var bytes = new byte[16];
        item.TryWriteBytes(bytes, bigEndian: true, out _);
        return bytes;
    }

    /// <summary>Unsigned, lexicographic order of byte strings of one length.</summary>
    private sealed class ByteOrder : IComparer<byte[]>
    {
        public static readonly ByteOrder Instance = new();

        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
    }
}
