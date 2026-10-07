using System.Buffers;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace MavLinkSharp.Cli.Cli;

/// <summary>
/// Writes MAVLink traces in either the line delimited JSON format (<c>.jsonl</c>) or as raw packet bytes
/// (<c>.raw</c>).
/// </summary>
internal sealed class TraceWriter : IDisposable
{
    /// <summary>
    /// The trace format version written into the JSON header.
    /// </summary>
    internal const int FormatVersion = 1;

    private const string HeaderRecord = "header";

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        SkipValidation = true
    };

    private readonly FileStream _stream;
    private readonly ArrayBufferWriter<byte> _buffer = new(512);
    private readonly Utf8JsonWriter _jsonWriter;
    private bool _disposed;

    /// <summary>
    /// Gets a value indicating whether packets are written as raw bytes instead of JSON lines.
    /// </summary>
    internal bool IsRaw { get; }

    /// <summary>
    /// Gets a value indicating whether packet bytes are embedded in the trace, which replay needs to stay byte exact.
    /// </summary>
    internal bool IncludeRaw { get; }

    /// <summary>
    /// Gets the trace file path.
    /// </summary>
    internal string TracePath { get; }

    private TraceWriter(FileStream stream, string path, bool isRaw, bool includeRaw)
    {
        _stream = stream;
        TracePath = path;
        IsRaw = isRaw;
        IncludeRaw = includeRaw || isRaw;
        _jsonWriter = new Utf8JsonWriter(_buffer, WriterOptions);
    }

    /// <summary>
    /// Determines whether a path denotes a raw byte trace.
    /// </summary>
    internal static bool IsRawPath(string path)
    {
        return Path.GetExtension(path).Equals(".raw", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates a writer, replacing any existing file and creating missing directories.
    /// </summary>
    internal static TraceWriter Create(string path, string? dialect, MavLinkContext context, bool includeRaw)
    {
        string fullPath = System.IO.Path.GetFullPath(path);
        string? directory = System.IO.Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        bool isRaw = IsRawPath(fullPath);
        var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read, 64 * 1024);

        var writer = new TraceWriter(stream, fullPath, isRaw, includeRaw);

        if (!isRaw)
        {
            writer.WriteHeader(DialectResolver.Normalize(dialect), context);
        }

        return writer;
    }

    /// <summary>
    /// Writes the single JSON header line that starts every <c>.jsonl</c> trace.
    /// </summary>
    internal void WriteHeader(string dialect, MavLinkContext context)
    {
        _buffer.Clear();
        _jsonWriter.Reset();
        _jsonWriter.WriteStartObject();
        _jsonWriter.WriteString("record", HeaderRecord);
        _jsonWriter.WriteNumber("formatVersion", FormatVersion);
        _jsonWriter.WriteString("createdUtc", DateTime.UtcNow.ToString("O"));
        _jsonWriter.WriteString("dialect", dialect);
        _jsonWriter.WriteString("tool", ToolVersion);
        _jsonWriter.WriteBoolean("raw", IncludeRaw);
        _jsonWriter.WriteNumber("messageCount", context.Metadata.MessagesDictionary.Count);
        _jsonWriter.WriteNumber("enumCount", context.Metadata.EnumsDictionary.Count);
        _jsonWriter.WriteEndObject();
        _jsonWriter.Flush();
        WriteLine(_buffer.WrittenSpan);
    }

    /// <summary>
    /// Appends a packet to the trace. Must be called from the receive path, so it is synchronous by design.
    /// </summary>
    internal void Write(PacketRecord record)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsRaw)
        {
            byte[] raw = record.Raw
                         ?? throw new InvalidOperationException(
                             "Raw traces require raw packet bytes; capture with --include-raw.");

            _stream.Write(raw, 0, raw.Length);
            return;
        }

        _buffer.Clear();
        _jsonWriter.Reset();
        PacketFormatter.WriteJson(_jsonWriter, record);
        _jsonWriter.Flush();
        WriteLine(_buffer.WrittenSpan);
    }

    private void WriteLine(ReadOnlySpan<byte> payload)
    {
        _stream.Write(payload);
        _stream.WriteByte((byte)'\n');
    }

    /// <summary>
    /// Gets the tool version reported in trace headers.
    /// </summary>
    internal static string ToolVersion
    {
        get
        {
            Assembly assembly = typeof(TraceWriter).Assembly;
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? assembly.GetName().Version?.ToString()
                   ?? "unknown";
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _jsonWriter.Dispose();
        _stream.Flush();
        _stream.Dispose();
    }
}