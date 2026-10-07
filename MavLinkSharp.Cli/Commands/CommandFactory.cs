using System.CommandLine;
using MavLinkSharp.Cli.Commands;

namespace MavLinkSharp.Cli;

/// <summary>
/// Builds the <c>mavlinkx</c> command tree.
/// </summary>
internal static class CommandFactory
{
    /// <summary>
    /// Creates the root command with every subcommand attached.
    /// </summary>
    internal static RootCommand Create()
    {
        var rootCommand = new RootCommand(
            "mavlinkx - MAVLink diagnostics: capture, inspect and replay traffic, and introspect dialects.");

        rootCommand.Options.Add(SharedOptions.Dialect);

        rootCommand.Subcommands.Add(CaptureCommand.Create());
        rootCommand.Subcommands.Add(ReplayCommand.Create());
        rootCommand.Subcommands.Add(InspectCommand.Create());
        rootCommand.Subcommands.Add(SchemaCommand.Create());
        rootCommand.Subcommands.Add(InfoCommand.Create());

        return rootCommand;
    }
}