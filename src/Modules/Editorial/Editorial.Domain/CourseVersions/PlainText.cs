using System.Globalization;
using System.Text;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>
/// Whether text is plain: no control character, no format character (bidi overrides, zero-width characters), line or
/// paragraph separator, private-use or unassigned code point, and no unpaired surrogate. A caller may allow line breaks
/// and tabs, as a free-text reason does; a one-line value such as a name does not. A name may allow the zero-width
/// non-joiner and joiner (U+200C, U+200D), which Persian and Indic names are written with, but only between two letters or
/// marks, so they never make a name or its ends invisible; the bidi controls and every other format character stay refused.
/// </summary>
internal static class PlainText
{
    private const int ZeroWidthNonJoiner = 0x200C;
    private const int ZeroWidthJoiner = 0x200D;

    public static bool IsPlain(string text, bool allowLineBreaksAndTabs, bool allowJoiners = false)
    {
        if (HasUnpairedSurrogate(text))
        {
            return false;
        }

        var characters = text.EnumerateRunes().ToList();
        for (var index = 0; index < characters.Count; index++)
        {
            var character = characters[index];
            if (allowJoiners && IsJoiner(character))
            {
                if (!JoinsTwoLetters(characters, index))
                {
                    return false;
                }

                continue;
            }

            if (!IsShowable(character, allowLineBreaksAndTabs))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsJoiner(Rune character) => character.Value is ZeroWidthNonJoiner or ZeroWidthJoiner;

    /// <summary>A joiner stands between two letters or marks, never at an end, beside a space or beside another joiner, where it would show nothing.</summary>
    private static bool JoinsTwoLetters(List<Rune> characters, int index) =>
        index > 0 && index < characters.Count - 1 && IsLetterOrMark(characters[index - 1]) && IsLetterOrMark(characters[index + 1]);

    private static bool IsLetterOrMark(Rune character) =>
        Rune.IsLetter(character) || Rune.GetUnicodeCategory(character) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

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
