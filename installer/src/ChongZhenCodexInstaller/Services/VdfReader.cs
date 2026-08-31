using System.Text.RegularExpressions;

namespace ChongZhenCodexInstaller.Services;

public static partial class VdfReader
{
    [GeneratedRegex("\"(?<key>[^\"]+)\"\\s*\"(?<value>(?:\\\\.|[^\"])*)\"", RegexOptions.CultureInvariant)]
    private static partial Regex PairPattern();

    public static IReadOnlyList<string> ReadValues(string text, string key) =>
        PairPattern().Matches(text)
            .Where(match => string.Equals(match.Groups["key"].Value, key, StringComparison.OrdinalIgnoreCase))
            .Select(match => Unescape(match.Groups["value"].Value))
            .ToArray();

    private static string Unescape(string value) => value
        .Replace("\\\"", "\"", StringComparison.Ordinal)
        .Replace("\\\\", "\\", StringComparison.Ordinal);
}
