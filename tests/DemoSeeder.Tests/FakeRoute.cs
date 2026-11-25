using System.Text.RegularExpressions;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder.Tests;

/// <summary>Matches a path against a route template such as <c>/organisation/pharmacies/{id}/invitations</c>.</summary>
public static class FakeRoute
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    public static bool TryMatch(string template, string path, out Dictionary<string, string> values)
    {
        var pattern = "^" + Regex.Replace(Regex.Escape(template), @"\\\{(\w+)}", "(?<$1>[^/]+)", RegexOptions.None, MatchTimeout) + "/?$";
        var match = Regex.Match(path, pattern, RegexOptions.None, MatchTimeout);
        values = [with(StringComparer.Ordinal)];
        if (!match.Success)
        {
            return false;
        }

        foreach (var group in match.Groups.Keys.Where(key => !int.TryParse(key, out _)))
        {
            values[group] = Uri.UnescapeDataString(match.Groups[group].Value);
        }

        return true;
    }

    /// <summary>A query string or form body as name and value, the last value of a repeated name winning.</summary>
    public static Dictionary<string, string> Pairs(string encoded)
    {
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in encoded.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=', StringComparison.Ordinal);
            var name = separator < 0 ? part : part[..separator];
            var value = separator < 0 ? string.Empty : part[(separator + 1)..];
            pairs[Decode(name)] = Decode(value);
        }

        return pairs;
    }

    private static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));
}
