namespace MavLinkSharp.Cli.Cli;

/// <summary>
/// Case insensitive wildcard matching supporting <c>*</c> (any run of characters) and <c>?</c> (one character).
/// </summary>
internal static class WildcardMatcher
{
    /// <summary>
    /// Determines whether the value matches the pattern.
    /// </summary>
    /// <param name="pattern">The pattern, for example <c>MSG_*</c>.</param>
    /// <param name="value">The value to test. A <c>null</c> value never matches a pattern that is not <c>*</c>.</param>
    internal static bool IsMatch(string pattern, string? value)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return true;
        }

        if (value is null)
        {
            return pattern == "*";
        }

        return IsMatch(pattern, 0, value, 0);
    }

    /// <summary>
    /// Determines whether the pattern contains wildcard characters.
    /// </summary>
    internal static bool ContainsWildcards(string pattern)
    {
        foreach (char c in pattern)
        {
            if (c is '*' or '?')
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMatch(string pattern, int patternIndex, string value, int valueIndex)
    {
        while (patternIndex < pattern.Length)
        {
            char token = pattern[patternIndex];

            if (token == '*')
            {
                // Collapse consecutive stars, then try every split point.
                while (patternIndex + 1 < pattern.Length && pattern[patternIndex + 1] == '*')
                {
                    patternIndex++;
                }

                if (patternIndex == pattern.Length - 1)
                {
                    return true;
                }

                for (int i = valueIndex; i <= value.Length; i++)
                {
                    if (IsMatch(pattern, patternIndex + 1, value, i))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (valueIndex >= value.Length)
            {
                return false;
            }

            if (token != '?' && char.ToUpperInvariant(token) != char.ToUpperInvariant(value[valueIndex]))
            {
                return false;
            }

            patternIndex++;
            valueIndex++;
        }

        return valueIndex == value.Length;
    }
}