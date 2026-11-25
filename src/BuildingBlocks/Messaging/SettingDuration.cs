using System.Globalization;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// Reads a duration from a setting in exactly <c>hh:mm:ss</c> or <c>d.hh:mm:ss</c>, two digits for each of hours, minutes
/// and seconds (#462). <see cref="TimeSpan.TryParse(string, IFormatProvider, out TimeSpan)"/>, and even the <c>c</c>
/// format, read <c>5</c> as five days, <c>1:30</c> as an hour and a half and <c>00:01</c> as a minute, and take fractions
/// and surrounding blanks, so a mistyped interval ran at a period nobody meant; this refuses all of them, and a sign.
/// </summary>
public static class SettingDuration
{
    private static readonly string[] Formats = [@"hh\:mm\:ss", @"d\.hh\:mm\:ss"];

    /// <summary>Whether <paramref name="value"/> is a duration in one of the two formats; it is then in <paramref name="duration"/>.</summary>
    public static bool TryParse(string value, out TimeSpan duration) =>
        TimeSpan.TryParseExact(value, Formats, CultureInfo.InvariantCulture, TimeSpanStyles.None, out duration);
}
