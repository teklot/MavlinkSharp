namespace MavLinkSharp.Cli.Cli;

/// <summary>
/// Selects packets by message id, id range, or (optionally wildcarded) message name.
/// </summary>
/// <remarks>
/// Filtering happens in the <c>PacketReceived</c> handler rather than through
/// <see cref="MavLinkContext.ExcludeMessages(uint[])"/>: excluded messages never reach handlers because parsing
/// fails with <see cref="Enums.ErrorReason.MessageExcluded"/>, which would make them uncapturable.
/// A leading <c>!</c> (or an <c>exclude:</c> prefix) negates a term. When at least one positive term exists a
/// packet must match one of them; negated terms always win.
/// </remarks>
internal sealed class PacketFilter
{
    private readonly Term[] _included;
    private readonly Term[] _excluded;

    private PacketFilter(Term[] included, Term[] excluded)
    {
        _included = included;
        _excluded = excluded;
    }

    /// <summary>
    /// Builds a filter from raw CLI expressions.
    /// </summary>
    internal static PacketFilter Parse(string[]? expressions)
    {
        var included = new List<Term>();
        var excluded = new List<Term>();

        foreach (string? expression in expressions ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                continue;
            }

            string term = expression.Trim();
            bool negated = term.StartsWith('!');

            if (negated)
            {
                term = term[1..].Trim();
            }
            else if (term.StartsWith("exclude:", StringComparison.OrdinalIgnoreCase))
            {
                negated = true;
                term = term["exclude:".Length..].Trim();
            }
            else if (term.StartsWith("include:", StringComparison.OrdinalIgnoreCase))
            {
                term = term["include:".Length..].Trim();
            }

            if (term.Length == 0)
            {
                throw new ArgumentException($"Invalid filter expression '{expression}'.");
            }

            (negated ? excluded : included).Add(Term.Parse(term));
        }

        return new PacketFilter(included.ToArray(), excluded.ToArray());
    }

    /// <summary>
    /// Gets a value indicating whether the filter lets every packet through.
    /// </summary>
    internal bool IsEmpty => _included.Length == 0 && _excluded.Length == 0;

    /// <summary>
    /// Determines whether a packet passes the filter.
    /// </summary>
    internal bool IsMatch(uint messageId, string? messageName)
    {
        foreach (Term term in _excluded)
        {
            if (term.IsMatch(messageId, messageName))
            {
                return false;
            }
        }

        if (_included.Length == 0)
        {
            return true;
        }

        foreach (Term term in _included)
        {
            if (term.IsMatch(messageId, messageName))
            {
                return true;
            }
        }

        return false;
    }

    private readonly struct Term
    {
        private readonly uint _id;
        private readonly uint _idEnd;
        private readonly string _pattern;

        private Term(uint id, uint idEnd, string pattern)
        {
            _id = id;
            _idEnd = idEnd;
            _pattern = pattern;
        }

        internal static Term Parse(string term)
        {
            if (term.All(char.IsDigit))
            {
                uint id = uint.Parse(term);
                return new Term(id, id, string.Empty);
            }

            int separator = term.IndexOf('-');
            if (separator > 0 && term[..separator].All(char.IsDigit) && term[(separator + 1)..].All(char.IsDigit))
            {
                uint start = uint.Parse(term[..separator]);
                uint end = uint.Parse(term[(separator + 1)..]);
                return new Term(start, end < start ? start : end, string.Empty);
            }

            return new Term(0, 0, term);
        }

        internal bool IsMatch(uint messageId, string? messageName)
        {
            if (_pattern.Length == 0)
            {
                return messageId >= _id && messageId <= _idEnd;
            }

            return WildcardMatcher.IsMatch(_pattern, messageName);
        }
    }
}