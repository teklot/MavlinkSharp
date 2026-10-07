using System.Text.Json;

namespace MavLinkSharp.Cli.Cli;

/// <summary>
/// Writes JSON documents to standard output, one per line.
/// </summary>
/// <remarks>
/// Used instead of <see cref="Console.Out"/> so machine readable output is never mixed with progress messages,
/// which all go to standard error.
/// </remarks>
internal sealed class JsonLineWriter : IDisposable
{
    private static readonly JsonWriterOptions Options = new()
    {
        Indented = false,
        SkipValidation = true
    };

    private readonly Stream _stream;
    private readonly Utf8JsonWriter _writer;
    private bool _disposed;

    internal JsonLineWriter()
    {
        _stream = Console.OpenStandardOutput();
        _writer = new Utf8JsonWriter(_stream, Options);
    }

    /// <summary>
    /// Writes a single JSON document followed by a newline.
    /// </summary>
    internal void Write(Action<Utf8JsonWriter> write)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        write(_writer);
        _writer.Flush();
        _stream.WriteByte((byte)'\n');
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writer.Dispose();
        _stream.Dispose();
    }
}