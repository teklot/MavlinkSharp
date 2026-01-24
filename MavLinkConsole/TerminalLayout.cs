namespace MavLinkConsole;

public static class TerminalLayout
{
    private static readonly object _lock = new();
    private static int _width;
    private static int _height;
    private static int _splitRow;
    private static readonly List<string> _txBuffer = new();
    private static readonly List<string> _rxBuffer = new();

    public static void Initialize()
    {
        Console.Clear();
        Console.CursorVisible = false;
        UpdateDimensions();
        RedrawAll();
    }

    private static bool UpdateDimensions()
    {
        if (Console.WindowWidth != _width || Console.WindowHeight != _height)
        {
            _width = Console.WindowWidth;
            _height = Console.WindowHeight;
            _splitRow = _height / 2;
            return true;
        }
        return false;
    }

    private static void DrawSeparator()
    {
        if (_splitRow < _height)
        {
            Console.SetCursorPosition(0, _splitRow);
            Console.Write(new string('-', _width));
        }
    }

    public static void WriteTx(string message)
    {
        lock (_lock)
        {
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
        int maxTx = _splitRow;
        while (_txBuffer.Count > maxTx && _txBuffer.Count > 0)
        {
            _txBuffer.RemoveAt(0);
        }

        int maxRx = _height - _splitRow - 1;
        if (maxRx < 0) maxRx = 0;
        while (_rxBuffer.Count > maxRx && _rxBuffer.Count > 0)
        {
            _rxBuffer.RemoveAt(0);
        }
    }

    private static void RedrawAll()
    {
        Console.Clear();
        DrawSeparator();
        RedrawTx();
        RedrawRx();
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
