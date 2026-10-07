using System.CommandLine;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using MavLinkSharp.Cli.Cli;

namespace MavLinkSharp.Cli.Commands;

/// <summary>
/// Implements <c>mavlinkx info</c>: reports the tool, library and dialect being used.
/// </summary>
internal static class InfoCommand
{
    private static class Opt
    {
        internal static readonly Option<bool> Json = SharedOptions.Json;
    }

    internal static Command Create()
    {
        var command = new Command("info", "Show version, runtime and dialect information.") { Opt.Json };

        command.SetAction((parseResult, _) =>
        {
            string? dialect = parseResult.GetValue(SharedOptions.Dialect);
            var context = DialectResolver.Resolve(dialect);
            Metadata metadata = context.Metadata;

            var messages = metadata.MessagesDictionary.Values.ToArray();
            var enums = metadata.EnumsDictionary.Values.ToArray();

            Assembly toolAssembly = typeof(InfoCommand).Assembly;
            Assembly libraryAssembly = typeof(Frame).Assembly;

            if (parseResult.GetValue(Opt.Json))
            {
                using var json = new JsonLineWriter();
                json.Write(writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteString("tool", toolAssembly.GetName().Name ?? "mavlinkx");
                    writer.WriteString("toolVersion", Version(toolAssembly));
                    writer.WriteString("libraryVersion", Version(libraryAssembly));
                    writer.WriteString("dialect", DialectResolver.Normalize(dialect));
                    writer.WriteString("runtime", RuntimeInformation.FrameworkDescription);
                    writer.WriteString("os", RuntimeInformation.OSDescription);
                    writer.WriteString("architecture", RuntimeInformation.ProcessArchitecture.ToString());
                    writer.WriteNumber("messageCount", messages.Length);
                    writer.WriteNumber("enumCount", enums.Length);
                    writer.WriteNumber("commandCount", metadata.CommandsDictionary.Count);
                    writer.WriteEndObject();
                });

                return Task.FromResult(Program.ExitSuccess);
            }

            Console.Out.WriteLine($"mavlinkx         {Version(toolAssembly)}");
            Console.Out.WriteLine($"MavLinkSharp     {Version(libraryAssembly)}");
            Console.Out.WriteLine($"dialect          {DialectResolver.Normalize(dialect)}");
            Console.Out.WriteLine($"runtime          {RuntimeInformation.FrameworkDescription}");
            Console.Out.WriteLine($"os               {RuntimeInformation.OSDescription}");
            Console.Out.WriteLine($"architecture     {RuntimeInformation.ProcessArchitecture}");
            Console.Out.WriteLine($"messages         {messages.Length}");
            Console.Out.WriteLine($"enums            {enums.Length}");
            Console.Out.WriteLine($"commands         {metadata.CommandsDictionary.Count}");

            Message[] heartbeats = messages
                .Where(message => message.Name is "HEARTBEAT" or "SYS_STATUS" or "ATTITUDE")
                .OrderBy(message => message.Name, StringComparer.Ordinal)
                .ToArray();

            if (heartbeats.Length > 0)
            {
                Console.Out.WriteLine();
                Console.Out.WriteLine("common messages available:");

                foreach (Message message in heartbeats)
                {
                    Console.Out.WriteLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "  {0,-16} id {1,5}  payload {2,3} bytes",
                        message.Name,
                        message.Id,
                        message.PayloadLength));
                }
            }

            return Task.FromResult(Program.ExitSuccess);
        });

        return command;
    }

    private static string Version(Assembly assembly)
    {
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "unknown";
    }
}