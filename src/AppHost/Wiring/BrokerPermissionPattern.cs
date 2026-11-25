using System.Text;
using System.Text.RegularExpressions;

namespace Aspire.Hosting;

/// <summary>
/// The regular expression a broker user's permission is written as (docs/adr/0011): exactly the given queue and exchange
/// names. RabbitMQ matches a permission's regular expression anywhere in a name, so it is anchored, and each name is
/// escaped. An exchange is named after its event's full type name, so every name a user consumes shares the contracts'
/// namespace: the names are written as a tree of their dot-separated segments, each shared prefix once
/// (<c>^(compliance|SmartSolutionsLab\.PharmaTraining\.Contracts\.(Assessment\.AttemptsExhaustedV1|Compliance\.(A|B)))$</c>),
/// which keeps the pattern of a user who consumes many events under the length RabbitMQ accepts. Spelled out one by one,
/// every consumed event added about 60 bytes, and the broker refused a user's pattern at boot and stopped.
/// </summary>
public static class BrokerPermissionPattern
{
    /// <summary>
    /// The longest pattern, in bytes, RabbitMQ 4.3.8 accepts (<c>rabbit_re</c>; 4.3.7 accepted 1024). A longer one stops
    /// the broker at boot, so the AppHost refuses it first.
    /// </summary>
    public const int MaximumLength = 4096;

    /// <summary>
    /// The pattern of exactly <paramref name="names"/>, for the user <paramref name="owner"/>. A pattern longer than
    /// <see cref="MaximumLength"/> stops the AppHost, naming the user, rather than the broker at its boot.
    /// </summary>
    public static string Of(string owner, IEnumerable<string> names)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(names);

        var segments = names.Distinct(StringComparer.Ordinal).Select(name => name.Split('.')).ToList();
        var pattern = $"^({string.Join('|', Alternatives(segments))})$";
        var length = Encoding.UTF8.GetByteCount(pattern);
        if (length > MaximumLength)
        {
            throw new InvalidOperationException(
                $"The broker user {owner}'s permission pattern is {length} bytes; RabbitMQ refuses one over {MaximumLength} and stops at boot (docs/adr/0011).");
        }

        return pattern;
    }

    /// <summary>One alternative per distinct first segment, in ordinal order, each followed by what may come after it.</summary>
    private static List<string> Alternatives(List<string[]> names) =>
        [.. names
            .GroupBy(name => name[0], StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => Regex.Escape(group.Key) + Rest([.. group.Select(name => name[1..])]))];

    /// <summary>
    /// What may follow a shared segment: nothing, for a name that ends there, and a dot followed by the alternatives of
    /// the names that go on.
    /// </summary>
    private static string Rest(List<string[]> tails)
    {
        List<string> rests = [];
        if (tails.Any(tail => tail.Length == 0))
        {
            rests.Add(string.Empty);
        }

        var going = tails.Where(tail => tail.Length > 0).ToList();
        if (going.Count > 0)
        {
            rests.Add(@"\." + Group(Alternatives(going)));
        }

        return Group(rests);
    }

    private static string Group(List<string> alternatives) =>
        alternatives.Count == 1 ? alternatives[0] : $"({string.Join('|', alternatives)})";
}
