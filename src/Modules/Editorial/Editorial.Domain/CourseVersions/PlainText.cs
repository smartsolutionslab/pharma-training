using System.Globalization;
using System.Text;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>
/// Whether text is plain: no control character, no format character (bidi overrides, zero-width characters), line or
/// paragraph separator, private-use or unassigned code point, and no unpaired surrogate. A caller may allow line breaks
/// and tabs, as a free-text reason does; a one-line value such as a name does not. A name may allow the zero-width
/// non-joiner and joiner (U+200C, U+200D), which Persian and Indic names are written with; the bidi controls and every
/// other format character stay refused.
/// </summary>
internal static class PlainText
{
    private const int ZeroWidthNonJoiner = 0x200C;
    private const int ZeroWidthJoiner = 0x200D;

    public static bool IsPlain(string text, bool allowLineBreaksAndTabs, bool allowJoiners = false) =>
        !HasUnpairedSurrogate(text)
        && text.EnumerateRunes().All(character =>
            (allowJoiners && character.Value is ZeroWidthNonJoiner or ZeroWidthJoiner) || IsShowable(character, allowLineBreaksAndTabs));

    /// <summary>A lone high or low surrogate, which <see cref="string.EnumerateRunes"/> would read as U+FFFD and let through.</summary>
    private static bool HasUnpairedSurrogate(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsSurrogatePair(text, index))
            {
                index++;
            }
            else if (char.IsSurrogate(text[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsShowable(Rune character, bool allowLineBreaksAndTabs) =>
        (allowLineBreaksAndTabs && character.Value is '\n' or '\r' or '\t')
        || (!Rune.IsControl(character) && Rune.GetUnicodeCategory(character) is not (UnicodeCategory.Format
            or UnicodeCategory.LineSeparator
            or UnicodeCategory.ParagraphSeparator
            or UnicodeCategory.PrivateUse
            or UnicodeCategory.OtherNotAssigned));
}
