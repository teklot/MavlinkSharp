namespace MavLinkConsole;

/// <summary>
/// Parsed command-line options for the MavLinkConsole demos.
/// </summary>
internal sealed class CliOptions
{
    /// <summary>True when all demos were requested (--all).</summary>
    public bool RunAll { get; private set; }

    /// <summary>True when the in-memory Mission Protocol demo was requested.</summary>
    public bool RunMission { get; private set; }

    /// <summary>True when the in-memory Parameter Protocol demo was requested.</summary>
    public bool RunParam { get; private set; }

    /// <summary>True when the UDP Tx/Rx demo was requested.</summary>
    public bool RunTxRx { get; private set; }

    /// <summary>True when the UDP Rx-only demo was requested.</summary>
    public bool RunRxOnly { get; private set; }

    /// <summary>True when usage help was requested.</summary>
    public bool ShowHelp { get; private set; }

    private CliOptions() { }

    /// <summary>
    /// Parses command-line arguments into a <see cref="CliOptions"/>.
    /// Unknown arguments are ignored.
    /// </summary>
    /// <param name="args">Raw command-line arguments.</param>
    /// <returns>The parsed options.</returns>
    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help":
                case "-h":
                    options.ShowHelp = true;
                    break;
                case "--all":
                    options.RunAll = true;
                    break;
                case "--mission":
                    options.RunMission = true;
                    break;
                case "--param":
                    options.RunParam = true;
                    break;
                case "--tx":
                    options.RunTxRx = true;
                    break;
                case "--rx":
                    options.RunRxOnly = true;
                    break;
            }
        }

        return options;
    }

    /// <summary>Prints usage help to the console.</summary>
    public static void PrintHelp()
    {
        System.Console.WriteLine("MavLinkConsole demos");
        System.Console.WriteLine();
        System.Console.WriteLine("Usage:");
        System.Console.WriteLine("  MavLinkConsole [options]");
        System.Console.WriteLine();
        System.Console.WriteLine("Options:");
        System.Console.WriteLine("  (no arguments)                 Show the interactive options menu.");
        System.Console.WriteLine("  --all                          Run all demos (Mission, Param, then UDP Tx/Rx).");
        System.Console.WriteLine("  --tx                           Run only the UDP Tx/Rx demo (continues until Ctrl+C).");
        System.Console.WriteLine("  --rx                           Run only the UDP Rx demo, listening on the default port 14550.");
        System.Console.WriteLine("  --mission                      Run only the in-memory Mission Protocol demo.");
        System.Console.WriteLine("  --param                        Run only the in-memory Parameter Protocol demo.");
        System.Console.WriteLine("  --help, -h                     Show this help.");
    }
}