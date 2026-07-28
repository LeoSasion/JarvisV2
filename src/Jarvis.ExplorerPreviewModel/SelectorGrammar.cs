using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Jarvis.ExplorerPreviewModel;

internal static partial class SelectorGrammar
{
    public static bool TryNormalize(
        string selector,
        out string normalized,
        out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(selector) ||
            selector.Length > 512 ||
            selector.Contains("//", StringComparison.Ordinal) ||
            selector.IndexOfAny(['[', ']', '@', ':', '=', '\r', '\n']) >= 0)
        {
            error = "selector-contains-forbidden-syntax";
            return false;
        }

        string[] parts = selector.Split(
            '>',
            StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Length > 9 ||
            parts.Any(string.IsNullOrWhiteSpace))
        {
            error = "selector-part-count-invalid";
            return false;
        }

        if (parts[0] == "*" || parts[^1] == "*")
        {
            error = "selector-wildcard-edge-forbidden";
            return false;
        }

        bool previousWildcard = false;
        foreach (string part in parts)
        {
            if (part == "*")
            {
                if (previousWildcard)
                {
                    error = "selector-consecutive-wildcard-forbidden";
                    return false;
                }

                previousWildcard = true;
                continue;
            }

            if (!SelectorPartPattern().IsMatch(part))
            {
                error = "selector-part-invalid";
                return false;
            }

            previousWildcard = false;
        }

        normalized = string.Join(" > ", parts);
        return true;
    }

    public static string Fingerprint(string normalizedSelector)
    {
        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(normalizedSelector)));
    }

    [GeneratedRegex(
        "^[A-Za-z_][A-Za-z0-9_.]*(?:#[A-Za-z_][A-Za-z0-9_]*)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SelectorPartPattern();
}
