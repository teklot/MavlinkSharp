using MavLinkSharp.Enums;

namespace MavLinkSharp.Cli.Cli;

/// <summary>
/// Resolves a dialect selector into a freshly initialized <see cref="MavLinkContext"/>.
/// </summary>
/// <remarks>
/// A fresh context is always created: <see cref="MavLinkContext.Initialize(string, uint[])"/> returns early
/// when the context is already initialized, so sharing a context would silently ignore the requested dialect.
/// </remarks>
internal static class DialectResolver
{
    internal const string DefaultDialect = "common";

    /// <summary>
    /// Gets the normalized dialect selector, substituting the default when none was supplied.
    /// </summary>
    internal static string Normalize(string? dialect)
    {
        return string.IsNullOrWhiteSpace(dialect) ? DefaultDialect : dialect.Trim();
    }

    /// <summary>
    /// Builds and initializes a new context for the given dialect selector.
    /// </summary>
    /// <param name="dialect">A built-in dialect name (for example <c>common</c>) or a path to a dialect XML file.</param>
    internal static MavLinkContext Resolve(string? dialect)
    {
        string name = Normalize(dialect);

        var context = new MavLinkContext();
        if (TryResolveDialectType(name, out var dialectType))
        {
            context.Initialize(dialectType);
        }
        else
        {
            context.Initialize(name);
        }

        return context;
    }

    /// <summary>
    /// Attempts to map a selector onto <see cref="DialectType"/>. Purely numeric selectors are rejected so that
    /// they are not silently interpreted as enum ordinals.
    /// </summary>
    internal static bool TryResolveDialectType(string name, out DialectType dialectType)
    {
        dialectType = default;

        if (string.IsNullOrWhiteSpace(name) || name.All(char.IsDigit))
        {
            return false;
        }

        return System.Enum.TryParse(name, ignoreCase: true, out dialectType)
               && System.Enum.IsDefined(dialectType);
    }

    /// <summary>
    /// Gets the sorted names of every built-in dialect.
    /// </summary>
    internal static string[] GetBuiltInDialects()
    {
        return System.Enum.GetNames<DialectType>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }
}