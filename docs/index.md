# MavLinkSharp

**Parse and send MAVLink telemetry & messages from drones, robots, and autonomous systems in .NET.**

MavLinkSharp is a lightweight, high-performance .NET library for MAVLink v1/v2. It parses [MAVLink XML dialects](https://mavlink.io/en/guide/xml_schema.html) **at runtime** — no code generation — and encodes/sends MAVLink packets over serial, UDP, TCP, or any other transport. It ships high-level Command, Mission, and Parameter Protocol APIs with built-in timeout and retry.

## Quick Links

- [Getting Started](getting-started.md) - Install, initialize, and parse your first packet
- [Architecture](architecture.md) - Understand the runtime dialect pipeline and connection model
- [API Reference](~/api/index.md) - Complete API documentation

## Features

| Area | Description |
|------|-------------|
| Runtime Dialect Parsing | Load standard or custom MAVLink XML dialects at startup — drop in an XML file, restart, done |
| Parsing (`Frame.TryParse`) | Vectorized MAVLink 1/2 packet parsing with MAVLink 2 truncation handling |
| Connection Manager | Event-driven `MavLinkConnection` over UDP, TCP, or Serial with auto-reconnect and heartbeat |
| Protocols | Command, Mission, and Parameter Protocols with timeout/retry |
| MAVLink 2 Signing | HMAC-SHA256 packet signing with timestamp validation |
| Streaming | `System.IO.Pipelines` and `ReadOnlySequence<byte>` support for fragmented streams |
| .NET | Multi-targeted: .NET 10+ (Native AOT compatible) and .NET Standard 2.0 |

## Example

```csharp
using MavLinkSharp;

// Load the common MAVLink dialect at runtime
MavLink.Initialize(DialectType.Common);

using var frame = new Frame();               // reuse one Frame for zero-allocation parsing
if (frame.TryParse(udpBytes))
{
    string name = Metadata.Messages[frame.MessageId].Name; // e.g. "ATTITUDE"
    Console.WriteLine($"Message: {name}");
}
```

See [Getting Started](getting-started.md) for parsing, sending, and connection examples.