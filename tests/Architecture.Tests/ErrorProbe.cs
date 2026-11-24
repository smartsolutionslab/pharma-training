namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests;

/// <summary>
/// What probing one error type found: its code, its message made from filled and from empty constructor arguments,
/// why a probe failed if one did, and every status a caller can receive it with (none when no use case maps it).
/// </summary>
public sealed record ErrorProbe(Type Type, string? Code, string? Message, string? EmptyMessage, string? Failure, IReadOnlyList<int> Statuses)
{
    /// <summary>The type, its code and its message, as a violation names them.</summary>
    public string Describe() => $"{Type.FullName} ({Code ?? "no code"}): \"{Message ?? EmptyMessage}\"";
}
