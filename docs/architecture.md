# Architecture

MavLinkSharp is structured around three layers:

1. **Dialect Metadata** — MAVLink XML definitions loaded at runtime into a `Metadata` model.
2. **Frame** — the parsing/encoding engine and the MAVLink packet lifecycle.
3. **Connection & Protocols** — high-level transport, Command/Mission/Parameter flows.

## Runtime Dialect Parsing

MavLinkSharp has **no code generation**. At startup, `MavLink.Initialize()` reads standard or custom MAVLink XML dialect files and builds an in-memory model:

- `MavLinkContext` holds the loaded state (messages, enums, commands) and drives checksums, signing, and message filtering.
- `Metadata` exposes `Messages`, `Enums`, and `Commands` dictionaries for lookup.
- `Message` holds the field definitions of one message: `PayloadLength`, `CrcExtra`, and the wire-ordered `OrderedFields`.
- `Field` resolves each XML type (`uint8_t`, `float`, `char[16]`, ...) to a CLR type, length, and wire ordering key.

Wire ordering follows the MAVLink serialization rules — fields are sorted by native size (largest first), equal-size fields keep XML order, MAVLink 2 extension fields are appended in declaration order, and `CRC_EXTRA` is computed over the reordered base fields.

## Frame Lifecycle

`Frame` is both the parse target and the encode/preview builder:

- **Parse:** `Frame.TryParse(ReadOnlySpan<byte>)` or the streaming `ReadOnlySequence<byte>` overload finds the start marker, decodes header/payload, verifies CRC (and, when enabled, MAVLink 2 signature), and stores the decoded `Message` plus a buffer-backed payload.
- **Access:** `Fields` lazily decodes the payload into typed values; typed accessors such as `GetFloat`, `GetByte`, `GetUInt16` read field spans directly.
- **Encode:** `SetFields` writes values into the payload at their computed offsets; `ToBytes` assembles the wire packet.
- **Lifecycle:** `Frame` is `IDisposable` and returns its buffer to `ArrayPool<byte>.Shared`; reuse one `Frame` instance for zero-allocation parsing.
- **Buffer ownership:** The parsed payload is referenced, not copied — keep the source buffer alive until you are done with the frame, or copy fields you retain.

## Connection Manager

`MavLinkConnection` wraps an `ITransport` and adds stream intelligence:

| Transport | Purpose |
|-----------|---------|
| `UdpTransport` | Client- or listener-mode UDP (default MAVLink port 14550) |
| `TcpTransport` | TCP client/server with auto-reconnect and hostname support |
| `SerialTransport` | Serial ports (`System.IO.Ports`) |

Features: automatic packet framing via `Frame`, `PacketReceived` events, auto-heartbeat, auto-sequence numbering, reconnection, and `OnCommandAck` handling. `ConnectionOptions` controls system/component IDs, heartbeat interval, and reconnect behavior.

## Protocols

High-level, asynchronous flows built on the connection layer with built-in timeout and retry:

- **Command Protocol** — `COMMAND_LONG` / `COMMAND_INT` with `COMMAND_ACK` handling and progress support.
- **Mission Protocol** — upload, download, and clear flight plans (`MISSION_COUNT`, `MISSION_ITEM_INT`, `MISSION_ACK`, ...).
- **Parameter Protocol** — request/read/set onboard parameters with an in-memory typed `ParameterCache`.

## MAVLink 2 Signing

`MavLinkSigning` implements MAVLink 2 packet signing (HMAC-SHA256) with random or passphrase keys and timestamp validation. Enable it per-frame (`Frame.EnableSigning`) or configure it at the connection level for signed links.

## Streaming & Performance

- `ReadOnlySequence<byte>` and `System.IO.Pipelines` (`PipeReader`) overloads handle fragmented stream input without copying.
- Precompiled layout metadata gives O(1) offset lookups; `ArrayPool` and `Span<T>` keep allocation near zero.
- .NET 10 target uses an `XmlReader`-based parser and hardware intrinsics for AOT compatibility.

## See Also

- [Getting Started](getting-started.md)
- [API Reference](~/api/index.md)