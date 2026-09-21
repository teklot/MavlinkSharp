# Getting Started with MavLinkSharp

## Installation

Install the MavLinkSharp package via NuGet:

```bash
dotnet add package MavLinkSharp
```

## Initialize

At application startup, load a MAVLink dialect. MavLinkSharp parses dialect XML **at runtime**, so there's no code generation step.

```csharp
using MavLinkSharp;

MavLink.Initialize(DialectType.Common);               // a bundled standard dialect
MavLink.Initialize("vehicle.xml");                    // or any custom dialect file
```

> **Important:** Initialization is mandatory — calling `Frame.TryParse()` before `MavLink.Initialize()` throws an `InvalidOperationException`.

## Parse Telemetry

Create a `Frame` once and reuse it. Pass each raw packet to `TryParse`:

```csharp
using var frame = new Frame();       // reuse for zero-allocation parsing
if (frame.TryParse(udpBytes))
{
    string name = Metadata.Messages[frame.MessageId].Name; // e.g. "ATTITUDE"
    Console.WriteLine($"Message: {name}");

    // Typed field access
    float roll = frame.GetFloat("roll");
}
```

`Frame` implements `IDisposable` — it hands buffers back to `ArrayPool<byte>.Shared`. Use `using` or call `Dispose()`.

## Send Messages

Construct a frame from message metadata and set its fields:

```csharp
var frame = new Frame
{
    StartMarker = Protocol.V2.StartMarker,
    SystemId = 1,
    ComponentId = 1,
    MessageId = 0,                         // HEARTBEAT
    Message = MavLinkContext.Default.Metadata.MessagesDictionary[0]
};
frame.SetFields(new Dictionary<string, object>
{
    ["type"] = (byte)2,
    ["autopilot"] = (byte)3,
    ["base_mode"] = (byte)81,
    ["custom_mode"] = (uint)0,
    ["system_status"] = (byte)4,
    ["mavlink_version"] = (byte)3
});

var raw = frame.ToBytes();               // ready to send over any transport
```

## Connect a Stream

Use the connection manager to talk over UDP, TCP, or Serial with events, auto-reconnect, and auto-sequence:

```csharp
using MavLinkSharp.Connection;

var transport = new SerialTransport("COM3", 57600);
var connection = new MavLinkConnection(transport, new ConnectionOptions
{
    SystemId = 1,
    ComponentId = 1,
    HeartbeatIntervalMs = 1000   // auto-heartbeat
});

connection.PacketReceived += frame =>
{
    Console.WriteLine($"Rx: {Metadata.Messages[frame.MessageId].Name}");
};

await connection.ConnectAsync();
```

## Next Steps

- Use the high-level [Command, Mission, and Parameter protocols](architecture.md#protocols)
- Enable [MAVLink 2 signing](architecture.md#mavlink-2-signing)
- Browse the [API Reference](~/api/index.md)