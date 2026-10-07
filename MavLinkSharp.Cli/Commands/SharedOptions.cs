using System.CommandLine;

namespace MavLinkSharp.Cli.Commands;

/// <summary>
/// Option instances shared by several commands. Keeping one instance per option is what lets a recursive option
/// declared on the root command still be read from a subcommand's <see cref="ParseResult"/>.
/// </summary>
internal static class SharedOptions
{
    /// <summary>
    /// The dialect selector, declared on the root command and inherited by every subcommand.
    /// </summary>
    internal static Option<string?> Dialect { get; } = new("--dialect")
    {
        Description = "Dialect to use: a built-in name such as 'common' or 'ardupilotmega', " +
                      "or a path to a dialect XML file.",
        Recursive = true
    };

    /// <summary>
    /// Emits machine readable JSON instead of formatted text.
    /// </summary>
    internal static Option<bool> Json { get; } = new("--json")
    {
        Description = "Write JSON instead of formatted text."
    };

    /// <summary>
    /// Includes per field detail in text output.
    /// </summary>
    internal static Option<bool> Verbose { get; } = new("--verbose", "-v")
    {
        Description = "Include per field detail."
    };

    /// <summary>
    /// Caps how many records a command prints.
    /// </summary>
    internal static Option<int?> Limit { get; } = new("--limit", "-n")
    {
        Description = "Stop after this many records."
    };

    /// <summary>
    /// Restricts output to matching message or enum names, with optional wildcards.
    /// </summary>
    internal static Option<string[]> Filter { get; } = new("--filter", "-f")
    {
        Description = "Only include names matching these patterns, for example 'ATTITUDE' or 'MSG_*'. Repeatable.",
        AllowMultipleArgumentsPerToken = true
    };
}