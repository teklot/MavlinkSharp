namespace MavLinkConsole;

public static class TerminalLayout
{
    private static readonly object _lock = new();
    private static int _width;
    private static int _height;
    private static int _splitRow;
    private static bool _split;
    private static bool _plainConsole;
    private static readonly List<string> _txBuffer = new();
    private static readonly List<string> _rxBuffer = new();

    /// <summary>
    /// Initializes the console layout.
    /// </summary>
    /// <param name="split">
    /// When <c>true</c>, renders a two-pane Tx/Rx layout split by a divider (used by the live UDP stream).
    /// When <c>false</c>, renders a full-width single pane with no divider (used by the in-memory protocol demos).
    /// </param>
    public static void Initialize(bool split = true)
    {
        _plainConsole = !TryClear();
        try
        {
            Console.CursorVisible = !_plainConsole;
        }
        catch (IOException)
        {
            _plainConsole = true;
        }
        _split = split;
        _txBuffer.Clear();
        _rxBuffer.Clear();
        UpdateDimensions();
        RedrawAll();
    }

    private static bool TryClear()
    {
        try
        {
            Console.Clear();
            return true;
        }
        catch (IOException)
        {
            // No attached console (e.g. piped input); fall back to plain line output.
            return false;
        }
    }

    private static bool UpdateDimensions()
    {
        if (_plainConsole)
            return false;

        if (Console.WindowWidth != _width || Console.WindowHeight != _height)
        {
            _width = Console.WindowWidth;
            _height = Console.WindowHeight;
            _splitRow = _split ? _height / 2 : _height;
            if (_splitRow < 1) _splitRow = 1;
            return true;
        }
        return false;
    }

    private static void DrawSeparator()
    {
        if (_split && _splitRow < _height)
        {
            Console.SetCursorPosition(0, _splitRow);
            Console.Write(new string('-', _width));
        }
    }

    public static void WriteTx(string message)
    {
        lock (_lock)
        {
            if (_plainConsole)
            {
                Console.WriteLine(message);
                return;
            }

            _txBuffer.Add(message);
            bool resized = UpdateDimensions();
            TrimBuffers();
            
            if (resized)
            {
                RedrawAll();
            }
            else
            {
                RedrawTx();
            }
        }
    }

    public static void WriteRx(string message)
    {
        lock (_lock)
        {
            // In single-pane mode, route Rx messages into the same full-width stream as Tx.
            if (!_split)
            {
                WriteTx(message);
                return;
            }

            if (_plainConsole)
            {
                Console.WriteLine(message);
                return;
            }

            _rxBuffer.Add(message);
            bool resized = UpdateDimensions();
            TrimBuffers();

            if (resized)
            {
                RedrawAll();
            }
            else
            {
                RedrawRx();
            }
        }
    }

    private static void TrimBuffers()
    {
        int maxTx = _split ? _splitRow : _height;
        if (maxTx < 1) maxTx = 1;
        while (_txBuffer.Count > maxTx && _txBuffer.Count > 0)
        {
            _txBuffer.RemoveAt(0);
        }

        int maxRx = _split ? _height - _splitRow - 1 : 0;
        if (maxRx < 0) maxRx = 0;
        while (_rxBuffer.Count > maxRx && _rxBuffer.Count > 0)
        {
            _rxBuffer.RemoveAt(0);
        }
    }

    private static void RedrawAll()
    {
        if (_plainConsole)
            return;
        try
        {
            Console.Clear();
            DrawSeparator();
            RedrawTx();
            RedrawRx();
        }
        catch (IOException)
        {
            // No attached console; output was already routed through plain WriteLine calls.
        }
    }

    private static void RedrawTx()
    {
        for (int i = 0; i < _txBuffer.Count; i++)
        {
            if (i < _splitRow)
            {
                Console.SetCursorPosition(0, i);
                Console.Write(FormatLine(_txBuffer[i], _width));
            }
        }
    }

    private static void RedrawRx()
    {
        for (int i = 0; i < _rxBuffer.Count; i++)
        {
            int row = _splitRow + 1 + i;
            if (row < _height)
            {
                Console.SetCursorPosition(0, row);
                Console.Write(FormatLine(_rxBuffer[i], _width));
            }
        }
    }

    private static string FormatLine(string msg, int width)
    {
        if (msg.Length >= width)
            return msg.Substring(0, width - 1);
        return msg.PadRight(width);
    }
}
