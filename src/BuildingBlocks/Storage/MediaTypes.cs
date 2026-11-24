using System.Text.RegularExpressions;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// The one shape a content type takes wherever the module names one (a read URL's signed headers, a blob it writes): one
/// bare <c>type/subtype</c> in lower case, with no parameters, whitespace or line break.
/// </summary>
internal static partial class MediaTypes
{
    public static EnsuredString Guard(string contentType, string parameterName = "value") =>
        Ensure.That(contentType, parameterName)
            .IsNotNullOrWhiteSpace()
            .Matches(MediaType(), "must be one bare type/subtype in lower case, without parameters, whitespace or a line break.");

    [GeneratedRegex("^[a-z0-9][a-z0-9!#$&^_.+-]*/[a-z0-9][a-z0-9!#$&^_.+-]*$")]
    private static partial Regex MediaType();
}
