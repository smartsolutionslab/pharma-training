namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// A code is the contract (#145): its message does not depend on the instance, so it never reads its constructor's
/// arguments (a compile-time constant is fine); every type that carries the code has the same message, whichever layer
/// it is in; and every way a caller receives the code answers the same HTTP status.
/// </summary>
public sealed class EveryErrorCodeHasOneMessageAndStatus : IArchitectureRule
{
    public string Description =>
        "Every error code has one message, which reads none of its constructor's arguments and is the same for every type carrying the code, and one HTTP status, whichever way a caller receives it.";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        return Violations(ErrorCatalog.ErrorTypes(model));
    }

    /// <summary>The violations among <paramref name="types"/>, each naming the types, their code and their messages.</summary>
    public static IEnumerable<string> Violations(IEnumerable<Type> types)
    {
        var probes = ErrorCatalog.Probe(types);

        foreach (var probe in probes)
        {
            if (probe.Failure is not null)
            {
                yield return $"{probe.Describe()}: {probe.Failure}.";
            }
            else if (probe.Message != probe.EmptyMessage)
            {
                yield return $"{probe.Describe()}: the message reads its constructor's arguments; with empty ones it is \"{probe.EmptyMessage}\".";
            }
        }

        foreach (var code in probes.Where(probe => probe.Code is not null).GroupBy(probe => probe.Code!, StringComparer.Ordinal).OrderBy(code => code.Key, StringComparer.Ordinal))
        {
            var messages = code.Select(probe => probe.Message).Distinct(StringComparer.Ordinal).ToList();
            if (messages.Count > 1)
            {
                yield return $"{code.Key} has {messages.Count} messages: {string.Join("; ", code.Select(probe => $"{probe.Type.FullName}: \"{probe.Message}\""))}.";
            }

            var statuses = code.SelectMany(probe => probe.Statuses).Distinct().ToList();
            if (statuses.Count > 1)
            {
                yield return $"{code.Key} answers {statuses.Count} statuses: {string.Join("; ", code.Where(probe => probe.Statuses.Count > 0).Select(probe => $"{probe.Type.FullName}: {string.Join(", ", probe.Statuses.Distinct())}"))}.";
            }
        }
    }
}
