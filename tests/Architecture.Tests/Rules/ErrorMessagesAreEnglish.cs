using System.Text.RegularExpressions;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// Error codes are the contract and the server's message is only the English fallback a page shows for a code it does
/// not translate (#145, #147): every ApiError and DomainError of the modules and of BuildingBlocks has an English
/// message. English is checked as printable ASCII without the common German words.
/// </summary>
public sealed partial class ErrorMessagesAreEnglish : IArchitectureRule
{
    /// <summary>Whole words, ignoring case, that give a German message away.</summary>
    private static readonly HashSet<string> GermanWords = new(
        ["der", "die", "das", "den", "dem", "des", "ein", "eine", "einer", "eines", "einem", "einen", "nicht", "kein", "keine", "keinen", "ist", "sind", "und", "oder", "muss", "wurde", "wurden", "gibt", "sie", "ihre", "ihr", "bitte", "mit", "zu", "von", "bei", "schon", "bereits"],
        StringComparer.OrdinalIgnoreCase);

    public string Description =>
        "Every ApiError and DomainError of the modules and of BuildingBlocks has an English message: printable ASCII (0x20 to 0x7E) and none of the German words "
        + string.Join(", ", GermanWords.Order(StringComparer.Ordinal)) + ".";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        return Violations(ErrorCatalog.ErrorTypes(model));
    }

    /// <summary>The violations among <paramref name="types"/>, each naming the type, its code and its message.</summary>
    public static IEnumerable<string> Violations(IEnumerable<Type> types)
    {
        foreach (var probe in ErrorCatalog.Probe(types))
        {
            var message = probe.Message ?? probe.EmptyMessage;
            if (message is null)
            {
                continue;
            }

            var foreign = message.Where(character => character is < ' ' or > '~').Distinct().ToList();
            var german = Words().Matches(message).Select(word => word.Value).Where(GermanWords.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (foreign.Count == 0 && german.Count == 0)
            {
                continue;
            }

            List<string> reasons = [];
            if (foreign.Count > 0)
            {
                reasons.Add($"it holds characters outside printable ASCII ({string.Join(", ", foreign.Select(character => $"U+{(int)character:X4}"))})");
            }

            if (german.Count > 0)
            {
                reasons.Add($"it holds the German words {string.Join(", ", german)}");
            }

            yield return $"{probe.Describe()} is not English: {string.Join("; ", reasons)}.";
        }
    }

    [GeneratedRegex("[A-Za-z]+")]
    private static partial Regex Words();
}
