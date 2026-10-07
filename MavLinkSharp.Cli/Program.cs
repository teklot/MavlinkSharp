using System.CommandLine;

namespace MavLinkSharp.Cli;

/// <summary>
/// Entry point for the <c>mavlinkx</c> diagnostic CLI.
/// </summary>
internal static class Program
{
    internal const int ExitSuccess = 0;
    internal const int ExitFailure = 1;
    internal const int ExitCancelled = 130;

    /// <summary>
    /// Grace period given to a running command after Ctrl+C before the process is terminated.
    /// </summary>
    private static readonly TimeSpan TerminationTimeout = TimeSpan.FromSeconds(3);

    internal static async Task<int> Main(string[] args)
    {
        RootCommand rootCommand = CommandFactory.Create();

        try
        {
            // ProcessTerminationTimeout is what turns Ctrl+C into cancellation of the running action.
            return await rootCommand.Parse(args).InvokeAsync(new InvocationConfiguration
            {
                ProcessTerminationTimeout = TerminationTimeout,

                // Errors are reported by the catch below with friendlier messages than the default handler.
                EnableDefaultExceptionHandler = false
            });
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("mavlinkx: cancelled.");
            return ExitCancelled;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"mavlinkx: {exception.Message}");

            if (!IsExpectedError(exception))
            {
                Console.Error.WriteLine(exception);
            }

            return ExitFailure;
        }
    }

    /// <summary>
    /// Errors caused by bad input rather than by a defect, where a stack trace only adds noise.
    /// </summary>
    private static bool IsExpectedError(Exception exception)
    {
        return exception is ArgumentException
            or InvalidOperationException
            or FileNotFoundException
            or DirectoryNotFoundException
            or IOException
            or UnauthorizedAccessException
            or System.Text.Json.JsonException;
    }
}