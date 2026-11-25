using System.Globalization;
using System.Text;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>
/// Whether text is plain: no control character, no format character (bidi overrides, zero-width characters), line or
/// paragraph separator, private-use or unassigned code point, and no unpaired surrogate. A caller may allow line breaks
/// and tabs, as a free-text reason does; a one-line value such as a name does not. A name (<see cref="IsPlainName"/>) also
/// holds at least one letter and no Default_Ignorable code point (Hangul fillers, the combining grapheme joiner), so it
/// never shows as nothing, with two exceptions real names are written with: the zero-width non-joiner and joiner (U+200C,
/// U+200D), which Persian and Indic names hold, only after a letter or mark, and at the end of a word only after a virama;
/// and a variation selector (<see cref="VariationSelectors"/>), which picks how the letter before it is drawn (辻 with
/// U+E0100 in a Japanese name, a Mongolian free variation selector), only right after a letter or a letter and its marks.
/// </summary>
internal static class PlainText
{
    private const int ZeroWidthNonJoiner = 0x200C;
    private const int ZeroWidthJoiner = 0x200D;

    /// <summary>The code points of Unicode's Default_Ignorable_Code_Point property (DerivedCoreProperties.txt).</summary>
    private static readonly (int First, int Last)[] DefaultIgnorable =
    [
        (0x00AD, 0x00AD), (0x034F, 0x034F), (0x061C, 0x061C), (0x115F, 0x1160), (0x17B4, 0x17B5), (0x180B, 0x180F),
        (0x200B, 0x200F), (0x202A, 0x202E), (0x2060, 0x206F), (0x3164, 0x3164), (0xFE00, 0xFE0F), (0xFEFF, 0xFEFF),
        (0xFFA0, 0xFFA0), (0xFFF0, 0xFFF8), (0x1BCA0, 0x1BCA3), (0x1D173, 0x1D17A), (0xE0000, 0xE0FFF),
    ];

    /// <summary>
    /// The variation selectors, all Default_Ignorable: the Mongolian free variation selectors FVS1 to FVS3 and FVS4
    /// (U+180B to U+180D, U+180F; U+180E, the vowel separator, is a format character), the standardized variation
    /// selectors (U+FE00 to U+FE0F) and the ideographic ones (U+E0100 to U+E01EF).
    /// </summary>
    private static readonly (int First, int Last)[] VariationSelectors =
    [
        (0x180B, 0x180D), (0x180F, 0x180F), (0xFE00, 0xFE0F), (0xE0100, 0xE01EF),
    ];

    /// <summary>
    /// The viramas (and the Khmer coeng) after which a joiner may end a word: a virama followed by a joiner is how
    /// Malayalam chillu letters and Bengali khanda-ta are typed, at the end of a word as often as inside one.
    /// </summary>
    private static readonly HashSet<int> Viramas =
    [
        0x094D, 0x09CD, 0x0A4D, 0x0ACD, 0x0B4D, 0x0BCD, 0x0C4D, 0x0CCD, 0x0D3B, 0x0D3C, 0x0D4D, 0x0DCA, 0x1039, 0x103A,
        0x17D2, 0x1B44, 0xA8C4,
    ];

    /// <summary>
    /// A one-line name a reader can see: plain, at least one letter, no Default_Ignorable code point but the joiners and
    /// the variation selectors, a joiner only after a letter or mark, ending a word only after a virama, and a variation
    /// selector only right after a letter or a letter and its marks.
    /// </summary>
    public static bool IsPlainName(string text)
    {
        if (!IsPlain(text, allowLineBreaksAndTabs: false, allowJoiners: true))
        {
            return false;
        }

        var characters = text.EnumerateRunes().ToList();
        return characters.Any(Rune.IsLetter)
            && Enumerable.Range(0, characters.Count).All(index =>
                IsJoiner(characters[index]) || !IsDefaultIgnorable(characters[index]) || SelectsALetter(characters, index));
    }

    public static bool IsPlain(string text, bool allowLineBreaksAndTabs) => IsPlain(text, allowLineBreaksAndTabs, allowJoiners: false);

    private static bool IsPlain(string text, bool allowLineBreaksAndTabs, bool allowJoiners)
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

    /// <summary>
    /// A joiner follows a letter or mark, so it never starts a name or a word and never follows a space or another joiner,
    /// where it would show nothing. It may end a word only after a virama (<see cref="Viramas"/>), not after any other
    /// mark, which may itself show nothing.
    /// </summary>
    private static bool JoinsTwoLetters(List<Rune> characters, int index)
    {
        if (index == 0 || !IsLetterOrMark(characters[index - 1]))
        {
            return false;
        }

        var endsWord = index == characters.Count - 1 || !IsLetterOrMark(characters[index + 1]);
        return !endsWord || Viramas.Contains(characters[index - 1].Value);
    }

    /// <summary>
    /// A variation selector right after a letter, or after a letter and the marks on it, so it never stands alone, starts a
    /// name or a word, or follows a space, a digit, a joiner or another selector, where it would select nothing.
    /// </summary>
    private static bool SelectsALetter(List<Rune> characters, int index)
    {
        if (!IsIn(VariationSelectors, characters[index]))
        {
            return false;
        }

        var before = index - 1;
        while (before >= 0 && IsMark(characters[before]) && !IsDefaultIgnorable(characters[before]))
        {
            before--;
        }

        return before >= 0 && Rune.IsLetter(characters[before]) && !IsDefaultIgnorable(characters[before]);
    }

    private static bool IsMark(Rune character) =>
        Rune.GetUnicodeCategory(character) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    private static bool IsLetterOrMark(Rune character) =>
        Rune.IsLetter(character) || IsMark(character);

    private static bool IsDefaultIgnorable(Rune character) => IsIn(DefaultIgnorable, character);

    private static bool IsIn((int First, int Last)[] ranges, Rune character) =>
        ranges.Any(range => character.Value >= range.First && character.Value <= range.Last);

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
