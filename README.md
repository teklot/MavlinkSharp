# MavLinkSharp — MAVLink Library to Parse & Send Telemetry & Messages from Drones, Robots & Autonomous Systems in .NET

[![CI](https://github.com/teklot/MavLinkSharp/actions/workflows/ci.yml/badge.svg)](https://github.com/teklot/MavLinkSharp/actions/workflows/ci.yml)
[![NuGet Version](https://img.shields.io/nuget/v/MavLinkSharp)](https://www.nuget.org/packages/MavLinkSharp/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/MavLinkSharp)](https://www.nuget.org/packages/MavLinkSharp/)
[![.NET](https://img.shields.io/badge/.NET-net10.0%20%7C%20netstandard2.0-blue)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/github/license/teklot/MavLinkSharp)](LICENSE)
[![Docs](https://img.shields.io/badge/docs-MavLinkSharp-blue)](https://teklot.github.io/MavlinkSharp/)

MavLinkSharp is a lightweight, high-performance .NET library for parsing and sending [MAVLink](https://mavlink.io/) v1/v2 protocol messages from drones, UAVs, and robots — ArduPilot, PX4, and any MAVLink-compatible vehicle, from aircraft and ground rovers to marine vessels. It parses [MAVLink XML dialects](https://mavlink.io/en/guide/xml_schema.html) **at runtime**, so there's **no code generation** — drop in a dialect, call `MavLink.Initialize()`, and start parsing telemetry immediately. It also encodes and sends MAVLink packets over serial, UDP, TCP, or any other transport, and ships high-level Command and Mission Protocol APIs with built-in timeout and retry.

MAVLink powers far more than aerospace: it's the backbone of **robotics, precision agriculture, environmental monitoring, surveying & inspection, marine robotics, logistics, and autonomous systems** across air, ground, and sea. Wherever there's a remote vehicle or autonomous machine producing telemetry, MavLinkSharp gives .NET developers a fast, reliable way to talk to it.

## Table of Contents

- [Features](#features)
- [Supported Frameworks](#supported-frameworks)
- [Getting Started](#getting-started)
- [Dialect Handling](#dialect-handling)
- [Filtering Messages](#filtering-messages)
- [Command Protocol](#command-protocol)
- [Mission Protocol](#mission-protocol)
- [Parameter Protocol](#parameter-protocol)
- [Connection Manager](#connection-manager)
- [MAVLink 2 Signing](#mavlink-2-signing)
- [Code Example](#code-example)
- [Constructing and Sending Messages](#constructing-and-sending-messages)
- [Advanced: Multiple Dialects (MavLinkContext)](#advanced-multiple-dialects-mavlinkcontext)
- [Advanced: Asynchronous Streaming](#advanced-asynchronous-streaming)
- [Example Project: MavLinkConsole](#example-project-mavlinkconsole)
- [Benchmark Project](#benchmark-project-mavlinksharpbenchmark)
- [MavLinkSharp vs pymavlink](#mavlinksharp-vs-pymavlink)

## Features
 - **Runtime Dialect Parsing:** Consumes standard MAVLink XML dialect files at runtime. **No code generation required.**
 - **Extensible:** Supports custom dialects with no extra effort. Just provide the XML file.
 - **High Performance:** Designed for speed and low allocation to handle high-throughput MAVLink streams.
 - **Streaming Ready:** Built-in support for `System.IO.Pipelines` (`PipeReader`) and `ReadOnlySequence<byte>` for efficient fragmented data stream parsing.
 - **Cross-Platform:** Can be used on any platform that supports .NET Standard 2.0 (Windows, Linux, macOS, etc.).
 - **Minimal Dependencies:** Only requires `System.Memory` and `System.IO.Pipelines`.
 - **MAVLink 2 Signing:** Full support for MAVLink 2 packet signing using HMAC-SHA256 with timestamp validation.
 - **Command Protocol:** High-level API for sending commands (`COMMAND_LONG`/`COMMAND_INT`) and handling acknowledgements (`COMMAND_ACK`) with built-in timeout, retry, and progress support.
 - **Mission Protocol:** High-level API for uploading, downloading, and clearing flight plans (`MISSION_COUNT`, `MISSION_REQUEST_INT`, `MISSION_ITEM_INT`, `MISSION_ACK`) with built-in timeout and retry.
 - **Parameter Protocol:** High-level API for reading, streaming, and setting onboard parameters (`PARAM_REQUEST_READ`, `PARAM_REQUEST_LIST`, `PARAM_VALUE`, `PARAM_SET`) with an in-memory typed `ParameterCache` and built-in timeout and retry.
 - **Connection Manager:** Event-driven `MavLinkConnection` wrapping UDP, TCP, or Serial transports with auto-reconnect, auto-heartbeat, and automatic sequence numbering.
 - **IDisposable Frame:** `Frame` implements `IDisposable` and uses `ArrayPool<byte>.Shared` for zero-allocation buffer management. Call `Dispose()` or use `using` to return buffers to the pool.
 - **Frame.ToString():** Human-readable summary of parsed frames for debugging (e.g., `MAVLink2 Msg=HEARTBEAT Sys=1 Comp=1`).

## Supported Frameworks

`MavLinkSharp` is a **multi-targeted** library supporting:
- **.NET 10+**: Optimized for maximum performance and **Native AOT** compatibility.
- **.NET Standard 2.0**: Providing broad compatibility across legacy .NET platforms, including .NET Framework.

### Modern .NET Features (10+):
*   **Native AOT Ready**: Uses a high-performance `XmlReader` parser instead of `XmlSerializer` to ensure zero reflection during initialization.
*   **High Performance**: Leverages modern hardware intrinsics and `Span<T>` for bit-manipulation.
*   **IsAotCompatible**: Fully compatible with trimmed and AOT-compiled applications.

## Getting Started

Using the library involves four main steps:

1.  **Add the NuGet Package:** Install the `MavLinkSharp` package from NuGet into your .NET project.
	```
	Install-Package MavLinkSharp
	```
2.  **Initialize the Library:** At application startup, call the static `MavLink.Initialize()` method. You must specify which dialect file to use (e.g., `common.xml`).
    > **Important:** This step is mandatory. Calling `frame.TryParse()` before `MavLink.Initialize()` will result in an `InvalidOperationException`.
3.  **Parse Incoming Data:** Create a `Frame` object once and reuse it. As you receive data from a MAVLink stream (e.g., a UDP client or serial port), pass the raw `byte[]` packet to the `frame.TryParse()` method.
4.  **Use the Result:** If `TryParse()` returns `true`, the `frame` object will be populated with the decoded message and its fields.

In just a few lines you can be parsing drone telemetry:

```cs
using MavLinkSharp;

MavLink.Initialize(DialectType.Common);   // load the common MAVLink dialect at runtime

using var frame = new Frame();             // reuse one Frame for zero-allocation parsing
if (frame.TryParse(udpBytes))
{
    string name = Metadata.Messages[frame.MessageId].Name; // e.g. "ATTITUDE"
    Console.WriteLine($"Message: {name}");
}
```

## Dialect Handling

> **⚡ No Code Generation Required** — Unlike traditional MAVLink libraries that require pre-generating C# code from XML, MavLinkSharp parses dialect files **at runtime**. Add a new dialect XML file, restart your app, and you're done.

The `MavLinkSharp` library utilizes a **runtime parsing mechanism** for [MAVLink XML dialect files](https://mavlink.io/en/guide/xml_schema.html). This means message definitions are loaded and processed dynamically when your application starts, rather than requiring code generation.

### Using Standard Dialects

The NuGet package includes a set of widely used MAVLink dialects: `common.xml`, `ardupilotmega.xml`, and `minimal.xml`.

To initialize the library with one of these standard dialects, simply pass its filename to `MavLink.Initialize()`:

```cs
// Initialize with the common MAVLink dialect
MavLink.Initialize(DialectType.Common); 
```

### Using Custom Dialects

You can easily extend the library's capabilities by providing your own custom MAVLink dialect XML files.

To use a custom dialect (e.g., `my-custom-dialect.xml`) initialize the library using the filename of your custom dialect:
```cs
// Initialize with your custom MAVLink dialect
MavLink.Initialize("path-to/my-custom-dialect.xml");
```

If your custom dialect includes other dialects, `MavLinkSharp` will automatically load them recursively as specified in your custom XML file.

> **💡 Tip:** Need to support a new vehicle type? Just drop its `vehicle.xml` dialect file into your project and call `MavLink.Initialize("vehicle.xml")`. No code generation step, no build-time tools, no manual mapping.

## Filtering Messages

The `MavLinkSharp` library offers flexible control over which MAVLink messages are parsed, allowing you to optimize for performance by only processing messages relevant to your application.

### Initializing Message Parsing

Message filtering begins with the `MavLink.Initialize()` method. This method loads message definitions from your specified MAVLink dialect(s) and simultaneously defines the initial set of messages that will be processed.

The `MavLink.Initialize()` method has the following signatures:
```cs
public static void Initialize(DialectType dialectType, params uint[] messageIds)

public static void Initialize(string dialectPath, params uint[] messageIds)
```
*   **`dialectType/dialectPath`**: (Required) The type of or path to the main dialect file to load.
*   **`messageIds`**: (Optional) A list of specific MAVLink message IDs (`uint`) you wish to enable for parsing immediately upon initialization.
    *   If **`messageIds` are provided**, only those specified messages will be marked for parsing.
    *   If **`messageIds` are omitted (or an empty array is passed)**, then *all messages* defined in the loaded dialect(s) will be initially marked for parsing.

**Important Note:** Regardless of the `messageIds` provided to `MavLink.Initialize()`, the **HEARTBEAT (#0)** message is always included and processed by default.

### Fine-Tuning Message Parsing (Include/Exclude)

After initialization, you can further fine-tune which messages are parsed using `MavLink.IncludeMessages()` and `MavLink.ExcludeMessages()`. These static methods allow you to dynamically enable or disable parsing for specific messages.

*   **`MavLink.IncludeMessages(params uint[] messageIds)`**:
    *   **Purpose**: To enable parsing for specific MAVLink message ID(s) that were previously disabled, or to ensure they are enabled.
    *   **Behavior**: If no `messageIds` are provided (i.e., called as `MavLink.IncludeMessages();`) then *all currently loaded message definitions* will be marked for parsing. This effectively overrides any previous exclusions (except for HEARTBEAT, which remains always parsed). If specific `messageIds` are provided, only those messages will be marked as included.
    ```cs
    // After initialization, enable parsing for SYS_STATUS (#1) and ATTITUDE (#30)
    MavLink.IncludeMessages(1, 30);
    ```

*   **`MavLink.ExcludeMessages(params uint[] messageIds)`**:
    *   **Purpose**: To disable parsing for specific MAVLink message ID(s).
    *   **Behavior**: The specified messages will be marked as excluded and will be ignored by `frame.TryParse()`. Note that the **HEARTBEAT (#0)** message cannot be excluded.
    ```cs
    // Disable parsing for VFR_HUD (#74)
    MavLink.ExcludeMessages(74);
    ```

**Example Workflow:**

1.  **Scenario A: Parse only specific messages from the start:**
    ```cs
    // Initialize, loading definitions and enabling only HEARTBEAT, SYS_STATUS, and ATTITUDE
    MavLink.Initialize(DialectType.Common, 0, 1, 30); 
    ```
2.  **Scenario B: Parse all messages from the start:**
    ```cs
    // Initialize, loading definitions and enabling ALL messages
    MavLink.Initialize(DialectType.Common); 
    // (Alternatively, MavLink.Initialize(DialectType.Common, new uint[] {}); also enables all)
    ```
3.  **Scenario C: Parse most messages, but exclude a few:**
    ```cs
    MavLink.Initialize(DialectType.Common); // All messages enabled initially
    MavLink.ExcludeMessages(74, 100); // Exclude VFR_HUD and another message
    ```

After initialization and any fine-tuning, process incoming byte streams with `frame.TryParse()`. Only the enabled messages (including HEARTBEAT) will yield a valid `Frame` object.

## Command Protocol

Starting with version 1.8.0, `MavLinkSharp` provides a high-level **Command Protocol** (`MavLinkSharp.Protocols`) for sending MAVLink commands and processing acknowledgements without manual handshake logic.

### API Overview

| Method | Description |
|--------|-------------|
| `CommandProtocol.CreateCommandLong()` | Builds a `COMMAND_LONG` frame with up to 7 float parameters |
| `CommandProtocol.CreateCommandInt()` | Builds a `COMMAND_INT` frame with integer coordinates and up to 4 float parameters |
| `CommandProtocol.TryParseCommandAck()` | Safely parses a `COMMAND_ACK` frame into a `CommandResult` |
| `CommandProtocol.CreateCommandCancel()` | Builds a `COMMAND_CANCEL` frame |
| `CommandProtocol.SendCommandAsync()` | Sends a command and waits for a matching `COMMAND_ACK` with configurable timeout, retries, and progress reporting |

### CommandResult

```cs
public class CommandResult
{
    public ushort Command { get; set; }
    public MavResult Result { get; set; }
    public byte Progress { get; set; }
    public int ResultParam2 { get; set; }
    public bool Success => Result == MavResult.Accepted;
}
```

### MavResult Enum

| Value | Description |
|-------|-------------|
| `Accepted` | Command was accepted and executed |
| `TemporarilyRejected` | Command temporarily rejected (vehicle busy) |
| `Denied` | Command denied (safety check failed) |
| `Unsupported` | Command not supported |
| `Failed` | Command execution failed |
| `InProgress` | Long-running command in progress |
| `Cancelled` | Command was cancelled |
| `CommandDeniedLanding` | Denied because landing is in progress |

### Quick Start

```cs
using MavLinkSharp;
using MavLinkSharp.Protocols;

// Initialize with your dialect
MavLink.Initialize(DialectType.Common);

// 1. Create a COMMAND_LONG frame using the factory
var commandFrame = CommandProtocol.CreateCommandLong(
    MavLinkContext.Default,
    systemId: 1,
    componentId: 1,
    targetSystem: 2,
    targetComponent: 1,
    command: 180, // MAV_CMD_DO_CHANGE_SPEED
    parameters: [1f, 5f, 0f, 0f, 0f, 0f, 0f]);

// 2. Serialize and send over your transport
byte[] packet = commandFrame.ToBytes();
// udpClient.Send(packet, packet.Length, remoteEndPoint);

// 3. Parse a received COMMAND_ACK
if (CommandProtocol.TryParseCommandAck(parsedFrame, out var result))
{
    Console.WriteLine($"Command {result.Command}: {(result.Success ? "Accepted" : result.Result)}");
}

// 4. Or use the async send-and-wait convenience method
var ack = await CommandProtocol.SendCommandAsync(
    commandFrame,
    sendAsync: (bytes, ct) => udpClient.SendAsync(bytes, bytes.Length, remoteEndPoint),
    receiveFrameAsync: ct => Task.FromResult(await ReceiveNextFrameAsync(ct)),
    timeoutMs: 5000,
    retries: 2);

if (ack.Success)
    Console.WriteLine("Command accepted!");
```

## Mission Protocol

Starting with version 1.11.0, `MavLinkSharp` provides a high-level **Mission Protocol** (`MavLinkSharp.Protocols`) for uploading, downloading, and clearing flight plans (missions) without manual handshake logic. It implements the standard [MAVLink mission service](https://mavlink.io/en/services/mission.html) message flow with built-in timeout and retry.

### API Overview

| Method | Description |
|--------|-------------|
| `MissionProtocol.CreateMissionRequestList()` | Builds a `MISSION_REQUEST_LIST` frame to initiate a download |
| `MissionProtocol.CreateMissionCount()` | Builds a `MISSION_COUNT` frame (initiate upload or acknowledge download) |
| `MissionProtocol.CreateMissionRequestInt()` | Builds a `MISSION_REQUEST_INT` frame requesting a specific item |
| `MissionProtocol.CreateMissionItemInt()` | Builds a `MISSION_ITEM_INT` frame carrying a plan item |
| `MissionProtocol.CreateMissionClearAll()` | Builds a `MISSION_CLEAR_ALL` frame |
| `MissionProtocol.CreateMissionSetCurrent()` | Builds a `MISSION_SET_CURRENT` frame |
| `MissionProtocol.TryParseMissionAck()` | Safely parses a `MISSION_ACK` frame into a `MissionAck` |
| `MissionProtocol.TryParseMissionItem()` | Safely parses a `MISSION_ITEM_INT` frame into a `MissionItem` |
| `MissionProtocol.UploadMissionAsync()` | Uploads a plan with built-in timeout, retry, and progress |
| `MissionProtocol.DownloadMissionAsync()` | Downloads a plan with built-in timeout, retry, and progress |
| `MissionProtocol.ClearMissionAsync()` | Clears a plan and awaits the `MISSION_ACK` |

### MissionItem

```cs
public class MissionItem
{
    public ushort Seq { get; set; }
    public ushort Command { get; set; }        // MAV_CMD value
    public MavFrame Frame { get; set; }         // coordinate frame
    public byte Current { get; set; }
    public byte AutoContinue { get; set; }
    public float Param1 { get; set; }
    public float Param2 { get; set; }
    public float Param3 { get; set; }
    public float Param4 { get; set; }
    public int X { get; set; }                  // lat * 1e7 (global) or x * 1e4 (local)
    public int Y { get; set; }                  // lon * 1e7 (global) or y * 1e4 (local)
    public float Z { get; set; }                // altitude
    public MavMissionType Type { get; set; }
}
```

### Quick Start

```cs
using MavLinkSharp;
using MavLinkSharp.Protocols;

MavLink.Initialize(DialectType.Common);

// Build a simple mission with two waypoints
var items = new List<MissionItem>
{
    new MissionItem { Seq = 0, Command = 16 /* MAV_CMD_NAV_WAYPOINT */, Frame = MavFrame.GlobalRelativeAltInt,
        AutoContinue = 1, X = (int)(47.398m * 1e7m), Y = (int)(8.545m * 1e7m), Z = 50.0f, Type = MavMissionType.Mission },
    new MissionItem { Seq = 1, Command = 21 /* MAV_CMD_NAV_LAND */, Frame = MavFrame.GlobalRelativeAltInt,
        AutoContinue = 0, X = (int)(47.400m * 1e7m), Y = (int)(8.550m * 1e7m), Z = 0.0f, Type = MavMissionType.Mission }
};

// Upload the mission (MISSION_COUNT -> MISSION_REQUEST_INT -> MISSION_ITEM_INT -> MISSION_ACK)
var ack = await MissionProtocol.UploadMissionAsync(
    items,
    MavLinkContext.Default,
    systemId: 1, componentId: 1,
    targetSystem: 2, targetComponent: 1,
    missionType: MavMissionType.Mission,
    sendAsync: (bytes, ct) => udpClient.SendAsync(bytes, bytes.Length, remoteEndPoint),
    receiveFrameAsync: ct => Task.FromResult(await ReceiveNextFrameAsync(ct)),
    timeoutMs: 1500, itemTimeoutMs: 250, maxRetries: 5);

if (ack.Success)
    Console.WriteLine("Mission uploaded!");

// Download the current mission (MISSION_REQUEST_LIST -> MISSION_COUNT -> MISSION_ITEM_INTs)
var download = await MissionProtocol.DownloadMissionAsync(
    MavLinkContext.Default,
    systemId: 1, componentId: 1,
    targetSystem: 2, targetComponent: 1,
    missionType: MavMissionType.Mission,
    sendAsync: (bytes, ct) => udpClient.SendAsync(bytes, bytes.Length, remoteEndPoint),
    receiveFrameAsync: ct => Task.FromResult(await ReceiveNextFrameAsync(ct)));

foreach (var item in download.Items)
    Console.WriteLine($"Item {item.Seq}: command={item.Command}, x={item.X}, y={item.Y}, z={item.Z}");

// Clear the mission
var clearAck = await MissionProtocol.ClearMissionAsync(
    MavLinkContext.Default,
    systemId: 1, componentId: 1,
    targetSystem: 2, targetComponent: 1,
    sendAsync: (bytes, ct) => udpClient.SendAsync(bytes, bytes.Length, remoteEndPoint),
    receiveFrameAsync: ct => Task.FromResult(await ReceiveNextFrameAsync(ct)));

if (clearAck.Success)
    Console.WriteLine("Mission cleared!");
```

> **Note:** For geofence and rally-point plans, pass `MavMissionType.Fence` or `MavMissionType.Rally` to the `missionType` argument.

## Parameter Protocol

Starting with version 1.12.0, `MavLinkSharp` provides a high-level **Parameter Protocol** (`MavLinkSharp.Protocols`) for reading, streaming, and setting onboard parameters without manual handshake logic. It implements the standard [MAVLink parameter service](https://mavlink.io/en/services/parameter.html) message flow with built-in timeout and retry, and includes an in-memory typed parameter cache.

### API Overview

| Method | Description |
|--------|-------------|
| `ParameterProtocol.CreateParamRequestRead()` | Builds a `PARAM_REQUEST_READ` frame to read one parameter by id/index |
| `ParameterProtocol.CreateParamRequestList()` | Builds a `PARAM_REQUEST_LIST` frame to request all parameters |
| `ParameterProtocol.CreateParamValue()` | Builds a `PARAM_VALUE` frame emitting a single parameter |
| `ParameterProtocol.CreateParamSet()` | Builds a `PARAM_SET` frame to write a parameter value |
| `ParameterProtocol.TryParseParamValue()` | Safely parses a `PARAM_VALUE` frame into a `MavParamValue` |
| `ParameterProtocol.DownloadParametersAsync()` | Requests all parameters and fills a `ParameterCache` with timeout/retry |
| `ParameterProtocol.ReadParameterAsync()` | Reads a single parameter by id |
| `ParameterProtocol.SetParameterAsync()` | Sets a parameter and waits for the `PARAM_VALUE` acknowledgement |

### MavParamValue & ParameterCache

```cs
public class MavParamValue
{
    public string ParamId { get; set; }      // up to 16 chars
    public float Value { get; set; }         // MAVLink wire representation
    public MavParamType Type { get; set; }
    public ushort ParamCount { get; set; }
    public ushort ParamIndex { get; set; }
    public T Get<T>();                       // typed accessor (int, uint, float, double, ...)
}
```

```cs
public class ParameterCache
{
    public int Count { get; }
    public void Set(MavParamValue value);
    public MavParamValue? Get(string paramId);
    public bool TryGet(string paramId, out MavParamValue? value);
    public bool TryGet<T>(string paramId, out T value);  // typed access
    public IEnumerable<MavParamValue> Values { get; }
}
```

`MavParamType` mirrors `MAV_PARAM_TYPE` (`UInt8`, `Int8`, `UInt16`, `Int16`, `UInt32`, `Int32`, `UInt64`, `Int64`, `Real32`, `Real64`).

### Quick Start

```cs
using MavLinkSharp;
using MavLinkSharp.Protocols;

MavLink.Initialize(DialectType.Common);

// 1. Download all parameters into an in-memory cache
var download = await ParameterProtocol.DownloadParametersAsync(
    MavLinkContext.Default,
    systemId: 1, componentId: 1,
    targetSystem: 2, targetComponent: 1,
    sendAsync: (bytes, ct) => udpClient.SendAsync(bytes, bytes.Length, remoteEndPoint),
    receiveFrameAsync: ct => Task.FromResult(await ReceiveNextFrameAsync(ct)));

foreach (var p in download.Cache.Values)
    Console.WriteLine($"{p.ParamId} = {p.Value}");

// 2. Read a single parameter by id
var read = await ParameterProtocol.ReadParameterAsync(
    MavLinkContext.Default,
    systemId: 1, componentId: 1,
    targetSystem: 2, targetComponent: 1,
    paramId: "THR_MAX",
    sendAsync: (bytes, ct) => udpClient.SendAsync(bytes, bytes.Length, remoteEndPoint),
    receiveFrameAsync: ct => Task.FromResult(await ReceiveNextFrameAsync(ct)));
Console.WriteLine($"{read.ParamId} = {read.Value}");

// 3. Set a parameter (awaits the PARAM_VALUE acknowledgement)
var set = await ParameterProtocol.SetParameterAsync(
    MavLinkContext.Default,
    systemId: 1, componentId: 1,
    targetSystem: 2, targetComponent: 1,
    paramId: "THR_MAX", value: 850f, type: MavParamType.Real32,
    sendAsync: (bytes, ct) => udpClient.SendAsync(bytes, bytes.Length, remoteEndPoint),
    receiveFrameAsync: ct => Task.FromResult(await ReceiveNextFrameAsync(ct)));
Console.WriteLine($"Set {set.ParamId} = {set.Value}");
```

> **Note:** Extended parameters (`PARAM_EXT_*`) and transactional set with `PARAM_ACK_TRANSACTION` are planned for a future release; the standard flow is supported here.

## Connection Manager

Starting with version 1.9.0, `MavLinkSharp` provides a **Connection Manager** (`MavLinkSharp.Connection`) that wraps transport layers with an event-driven API, automatic sequence numbering, heartbeats, and reconnection support.

### Supported Transports

| Transport | Class | Description |
|-----------|-------|-------------|
| UDP | `UdpTransport` | Client and server modes, binds to local port, sends to remote endpoint |
| TCP | `TcpTransport` | Client (connect) and server (listen/accept) modes |
| Serial | `SerialTransport` | Wraps `System.IO.Ports.SerialPort` for UART communication |

### Key Features

- **Event-driven API:** Subscribe to `Connected`, `Disconnected`, and `PacketReceived` events.
- **Typed message handlers:** `OnMessage(uint, Action<Frame>)` and `OnMessage(string, Action<Frame>)` for specific message IDs or names.
- **Command ACK handler:** `OnCommandAck(Action<CommandResult>)` for automatic COMMAND_ACK processing.
- **Auto-sequence numbering:** `PacketSequence` is incremented automatically on each send.
- **Auto-heartbeat:** Configurable interval for sending HEARTBEAT messages.
- **Auto-reconnect:** Reconnects on transport failure and on an *initial* connect failure with configurable delay and max attempts. TCP client transports defer connecting until `ConnectAsync()`, so you can create them before the remote endpoint is available; `MavLinkConnection.ConnectAsync()` returns immediately and reconnects in the background until the target comes online.
- **IAsyncDisposable:** Clean async shutdown of transport and background tasks.

### Quick Start

```cs
using MavLinkSharp;
using MavLinkSharp.Connection;

// 1. Initialize the library
MavLink.Initialize(DialectType.Common);

// 2. Create a transport (UDP example)
var transport = new UdpTransport(localPort: 14550, remoteAddress: "127.0.0.1", remotePort: 14551);

// 3. Configure connection options
var options = new ConnectionOptions
{
    SystemId = 1,
    ComponentId = 1,
    HeartbeatIntervalMs = 1000,
    AutoReconnect = true
};

// 4. Create the connection
var connection = new MavLinkConnection(transport, options);

// 5. Subscribe to events
connection.OnMessage("HEARTBEAT", frame =>
{
    byte type = frame.GetByte("type");
    Console.WriteLine($"System {frame.SystemId} alive, type: {type}");
});

connection.OnMessage(30, frame => // ATTITUDE
{
    float roll = frame.GetSingle("roll");
    float pitch = frame.GetSingle("pitch");
    Console.WriteLine($"Attitude: Roll={roll}, Pitch={pitch}");
});

connection.OnCommandAck(result =>
{
    Console.WriteLine($"Command {result.Command}: {(result.Success ? "Accepted" : result.Result)}");
});

connection.Connected += (s, e) => Console.WriteLine("Connected!");
connection.Disconnected += (s, e) => Console.WriteLine($"Disconnected: {e.Exception?.Message}");

// 6. Connect and start receiving
await connection.ConnectAsync();

// 7. Send messages (sequence number is automatic)
var heartbeatMsg = Metadata.Messages[0]; // HEARTBEAT
var frame = new Frame
{
    StartMarker = Protocol.V2.StartMarker,
    MessageId = 0,
    Message = heartbeatMsg
};
frame.SetFields(new Dictionary<string, object>
{
    ["type"] = (byte)6,
    ["autopilot"] = (byte)8,
    ["base_mode"] = (byte)0,
    ["custom_mode"] = (uint)0,
    ["system_status"] = (byte)4,
    ["mavlink_version"] = (byte)3
});
await connection.SendAsync(frame);

// 8. Send a command and wait for ACK
var cmdFrame = CommandProtocol.CreateCommandLong(
    MavLinkContext.Default,
    systemId: 1, componentId: 1,
    targetSystem: 2, targetComponent: 1,
    command: 180, // MAV_CMD_DO_CHANGE_SPEED
    parameters: [1f, 5f, 0f, 0f, 0f, 0f, 0f]);

var ack = await connection.SendCommandAsync(cmdFrame, timeoutMs: 5000, retries: 2);
Console.WriteLine($"Command result: {ack.Result}");

// 9. Clean up
await connection.DisposeAsync();
```

### Serial Port Example

```cs
var serialTransport = new SerialTransport("COM3", 115200);
var connection = new MavLinkConnection(serialTransport, new ConnectionOptions
{
    SystemId = 255,
    ComponentId = 68
});

connection.OnMessage("HEARTBEAT", frame =>
    Console.WriteLine($"Vehicle {frame.SystemId} connected"));

await connection.ConnectAsync();
```

### TCP Server Example

```cs
var listener = new System.Net.Sockets.TcpListener(
    System.Net.IPAddress.Any, 5760);
listener.Start();

var client = await listener.AcceptTcpClientAsync();
var transport = new TcpTransport(client);
var connection = new MavLinkConnection(transport);

connection.OnPacketReceived += frame =>
    Console.WriteLine($"Received: {Metadata.Messages[frame.MessageId].Name}");

await connection.ConnectAsync();
```

### TCP Client Example (start before the server is up)

Client-mode transports defer connecting until `ConnectAsync()`, so a ground station can start
before the vehicle connects. With `AutoReconnect` enabled, `ConnectAsync()` returns immediately
and retries in the background, firing `Connected` once the target becomes available.

```cs
var transport = new TcpTransport("192.168.1.10", 5760); // may be unreachable right now
var connection = new MavLinkConnection(transport, new ConnectionOptions
{
    SystemId = 1,
    ComponentId = 1,
    AutoReconnect = true,
    ReconnectDelayMs = 5000,
    MaxReconnectAttempts = int.MaxValue // retry indefinitely
});

connection.Connected += (s, e) => Console.WriteLine("Vehicle connected!");
connection.Disconnected += (s, e) => Console.WriteLine("Connection lost, reconnecting...");

await connection.ConnectAsync(); // returns immediately; reconnects in the background
```

You can also pass a pre-connected `TcpClient`: `new TcpTransport(tcpClient)`. The transport
captures the remote endpoint so it can reconnect when a connection is lost.

### API Reference

#### MavLinkConnection

| Member | Description |
|--------|-------------|
| `ConnectAsync(CancellationToken)` | Connects and starts receive loop, heartbeat, and reconnection |
| `DisconnectAsync()` | Disconnects and stops all background tasks |
| `SendAsync(Frame, CancellationToken)` | Sends a frame with auto SystemId/ComponentId/Sequence |
| `SendCommandAsync(Frame, ...)` | Sends a command and waits for COMMAND_ACK |
| `SendHeartbeatAsync(CancellationToken)` | Sends a single HEARTBEAT message |
| `OnMessage(uint, Action<Frame>)` | Registers a handler for a specific message ID |
| `OnMessage(string, Action<Frame>)` | Registers a handler for a message by name |
| `OnCommandAck(Action<CommandResult>)` | Registers a handler for COMMAND_ACK |
| `Connected` event | Fires when the transport connects |
| `Disconnected` event | Fires when the transport disconnects |
| `PacketReceived` event | Fires for every received frame |

#### ConnectionOptions

| Property | Default | Description |
|----------|---------|-------------|
| `SystemId` | 1 | MAVLink system ID for outgoing messages |
| `ComponentId` | 1 | MAVLink component ID for outgoing messages |
| `Context` | `MavLinkContext.Default` | Dialect context for parsing |
| `Signing` | null | Optional MAVLink 2 signing configuration |
| `AutoReconnect` | true | Whether to auto-reconnect on failure |
| `ReconnectDelayMs` | 1000 | Delay between reconnection attempts |
| `MaxReconnectAttempts` | 5 | Max consecutive reconnection attempts |
| `HeartbeatIntervalMs` | 1000 | Heartbeat interval (0 = disabled) |
| `HeartbeatType` | 0 | MAV_TYPE for heartbeats |
| `HeartbeatAutopilot` | 0 | MAV_AUTOPILOT for heartbeats |
| `HeartbeatSystemStatus` | 0 | MAV_STATE for heartbeats |

#### ITransport

| Method | Description |
|--------|-------------|
| `ConnectAsync(CancellationToken)` | Establishes the transport connection |
| `DisconnectAsync(CancellationToken)` | Closes the transport connection |
| `SendAsync(ReadOnlyMemory<byte>, CancellationToken)` | Sends data over the transport |
| `ReceiveAsync(Memory<byte>, CancellationToken)` | Receives data from the transport |
| `DisposeAsync()` | Asynchronously releases resources |

## MAVLink 2 Signing

Starting with version 1.7.0, `MavLinkSharp` supports **MAVLink 2 packet signing** using HMAC-SHA256. This provides authentication and integrity verification for MAVLink 2 packets.

### Key Features
- **HMAC-SHA256 Signatures:** 13-byte signatures (1 byte link ID + 6 bytes timestamp + 6 bytes truncated HMAC)
- **Timestamp Validation:** Configurable timestamp window (default: 10 seconds) to prevent replay attacks
- **Passphrase-based Keys:** Generate signing keys from passphrases using SHA-256
- **Random Key Generation:** Create cryptographically secure random 32-byte keys

### Quick Start

```cs
using MavLinkSharp;

// 1. Create a signing configuration (from passphrase or random key)
var signing = new MavLinkSigning("my-secret-passphrase");
// Or: var key = MavLinkSigning.CreateRandomKey();
// Or: var signing = new MavLinkSigning(key);

// 2. Create and configure a frame
var frame = new Frame();
frame.StartMarker = Protocol.V2.StartMarker;
frame.SystemId = 1;
frame.ComponentId = 1;
frame.PacketSequence = 0;
frame.MessageId = 0; // HEARTBEAT
frame.Message = MavLinkContext.Default.Metadata.MessagesDictionary[0];
frame.SetFields(new Dictionary<string, object>()
{
    { "type", (byte)8 },
    { "autopilot", (byte)0 },
    { "base_mode", (byte)0 },
    { "custom_mode", (uint)0 },
    { "system_status", (byte)0 },
    { "mavlink_version", (byte)3 }
});

// 3. Enable signing on the frame
frame.EnableSigning(signing);

// 4. Serialize to bytes (signature is automatically appended)
byte[] signedPacket = frame.ToBytes();

// 5. Parse and validate on the receiving end
var parsedFrame = new Frame();
parsedFrame.Signing = signing; // Assign the same signing configuration
if (parsedFrame.TryParse(signedPacket))
{
    Console.WriteLine($"Valid signed packet received: Message ID {parsedFrame.MessageId}");
}
else
{
    Console.WriteLine($"Invalid packet: {parsedFrame.ErrorReason}");
}
```

### Using Different Keys for Different Systems

```cs
// Create signing configurations for different systems
var signing1 = new MavLinkSigning("vehicle-1-key");
var signing2 = new MavLinkSigning("vehicle-2-key");

// Each frame can have its own signing configuration
frame1.EnableSigning(signing1);
frame2.EnableSigning(signing2);

// When parsing, assign the appropriate signing configuration
parsedFrame.Signing = signing1; // or signing2, depending on sender
```

### API Reference

#### MavLinkSigning Class

```cs
public class MavLinkSigning
{
    // Constants
    public const byte SigningFlag = 0x01;
    public const int SecretKeyLength = 32;
    public const int SignatureLength = 13;
    public const long DefaultTimestampWindow = 10_000_000; // 10 seconds in microseconds

    // Properties
    public byte LinkId { get; set; }
    public bool AcceptTimestampsBeforeTimestamp { get; set; }
    public ReadOnlySpan<byte> SecretKey { get; }

    // Constructors
    public MavLinkSigning(byte[] secretKey); // 32-byte key
    public MavLinkSigning(string passphrase); // SHA-256 hashed passphrase

    // Methods
    public byte[] GenerateSignature(ReadOnlySpan<byte> packet);
    public bool ValidateSignature(ReadOnlySpan<byte> packet, ReadOnlySpan<byte> signature);
    public long GetCurrentTimestamp();
    public static byte[] CreateRandomKey();
}
```

#### Frame Extensions

```cs
public class Frame
{
    public MavLinkSigning? Signing { get; set; }
    public byte[] Signature { get; }
    public bool HasSignature { get; }

    public void EnableSigning(MavLinkSigning signing, byte? linkId = null);
}
```

## Code Example
```cs
using MavLinkSharp;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

// 1. Initialize the library with the desired dialect.
MavLink.Initialize(DialectType.Common);

// 2. Specify which messages you want to parse.
MavLink.IncludeMessages(1, 30);

// 3. Create a Frame object once and reuse it for high performance (zero allocation).
//    Frame uses ArrayPool internally — dispose it when done to return buffers to the pool.
using var frame = new Frame();

// Example: Listen for MAVLink packets on a local UDP port.
var endpoint = new IPEndPoint(IPAddress.Loopback, 14550);

using var udpClient = new UdpClient(endpoint);

Console.WriteLine($"Listening for MAVLink packets on {endpoint}...\n");

while (true)
{
    // Receive a raw byte packet.
    var packet = udpClient.Receive(ref endpoint);

    // 4. Try to parse the packet into the existing frame object.
    if (frame.TryParse(packet))
    {
        // 5. If successful, use the data.
        // You can access fields using the dynamic Fields dictionary:
        var fields = string.Join(", ", frame.Fields.Select(f => $"{f.Key}: {f.Value}"));
        Console.WriteLine($"Received: {Metadata.Messages[frame.MessageId].Name} => {fields}");

        // Or use high-performance typed accessors (preferred for low-latency scenarios):
        if (frame.MessageId == 30) // ATTITUDE
        {
            float roll = frame.GetSingle("roll");
            float pitch = frame.GetSingle("pitch");
            Console.WriteLine($"Attitude: Roll={roll}, Pitch={pitch}");
        }
    }
}
```

## Constructing and Sending Messages
`MavLinkSharp` makes it easy to construct MAVLink packets for transmission.

```cs
// 1. Get the message definition you want to send.
var heartbeatDef = Metadata.Messages[0]; // HEARTBEAT

// 2. Create a Frame and set the header information.
var frame = new Frame
{
    StartMarker = Protocol.V2.StartMarker,
    SystemId = 1,
    ComponentId = 1,
    MessageId = heartbeatDef.Id,
    Message = heartbeatDef,
    PacketSequence = 1
};

// 3. Set the field values.
// Field.SetValue automatically handles numeric type conversion (e.g., int to float).
var values = new Dictionary<string, object>
{
    { "custom_mode", 0 },
    { "type", 6 },         // MAV_TYPE_GCS
    { "autopilot", 8 },    // MAV_AUTOPILOT_INVALID
    { "base_mode", 0 },
    { "system_status", 4 }, // MAV_STATE_ACTIVE
    { "mavlink_version", 3 }
};
frame.SetFields(values);

// 4. Serialize to a byte array.
byte[] packet = frame.ToBytes();

// 5. Send over your transport (e.g., UDP).
udpClient.Send(packet, packet.Length, remoteEndPoint);
```

## Advanced: Multiple Dialects (MavLinkContext)
Starting with version 1.5.0, `MavLinkSharp` supports handling multiple MAVLink dialects simultaneously through the `MavLinkContext` class. This is useful for complex gateways or ground stations that communicate with different types of vehicles.

```cs
// 1. Create separate contexts for different dialects
var commonContext = new MavLinkContext();
commonContext.Initialize(DialectType.Common);

var ardupilotContext = new MavLinkContext();
ardupilotContext.Initialize(DialectType.Ardupilotmega);

// 2. Assign the context to the Frame object
var commonFrame = new Frame { Context = commonContext };
var ardupilotFrame = new Frame { Context = ardupilotContext };

// 3. Parse packets using their respective frames/contexts
if (commonFrame.TryParse(packetFromCommonVehicle)) { /* ... */ }
if (ardupilotFrame.TryParse(packetFromArduPilotVehicle)) { /* ... */ }
```
The static `MavLink.Initialize()` and `Metadata` properties still work and represent a `MavLinkContext.Default` instance for easy backward compatibility.

## Advanced: Asynchronous Streaming
For high-bandwidth or fragmented streams (like Serial or TCP), `MavLinkSharp` supports `System.IO.Pipelines`. This allows for highly efficient, asynchronous parsing without manual buffer management.

```cs
using System.IO.Pipelines;

public async Task ProcessMavLinkStreamAsync(PipeReader reader)
{
    var frame = new Frame();
    while (true)
    {
        ReadResult result = await reader.ReadAsync();
        ReadOnlySequence<byte> buffer = result.Buffer;

        // Try to parse as many frames as possible from the current buffer
        while (frame.TryParse(buffer, out SequencePosition consumed, out SequencePosition examined))
        {
            // Successfully parsed a frame!
            Console.WriteLine($"Parsed Message ID: {frame.MessageId}");
            
            // Advance the local buffer slice
            buffer = buffer.Slice(consumed);
        }

        // Tell the PipeReader how much we've consumed and examined
        reader.AdvanceTo(buffer.Start, buffer.End);

        if (result.IsCompleted) break;
    }
}
```

The `ReadOnlySequence<byte>` overload of `TryParse` also works with any multi-segment buffer, making it useful outside of `PipeReader` scenarios:

```cs
var frame = new Frame();
var sequence = new ReadOnlySequence<byte>(myArray);
if (frame.TryParse(sequence, out var consumed, out var examined))
{
    Console.WriteLine(frame); // Uses Frame.ToString()
}
```

## Example Project: MavLinkConsole

The `MavLinkConsole` project serves as a practical example demonstrating how to use the `MavLinkSharp` library for both sending and receiving MAVLink messages over UDP, all within a single console application. It's particularly useful for testing, development, and quickly observing MAVLink communication.

*   **`MavLinkConsole` (Transmitter & Receiver):** This console application runs two concurrent tasks:
    *   **Transmitter (Tx):** Generates and sends synthetic MAVLink messages (e.g., HEARTBEAT, GPS_RAW_INT, ATTITUDE) over UDP to the default MAVLink port (UDP 14550). It showcases how to construct MAVLink `Frame` objects and serialize them into byte arrays for transmission. Every 5th message uses the **Command Protocol** to send a `COMMAND_LONG` via `CommandProtocol.CreateCommandLong()`.
    *   **Receiver (Rx):** Listens for incoming MAVLink UDP packets on the default MAVLink port (UDP 14550). It demonstrates how to parse raw byte arrays into `Frame` objects using `frame.TryParse()` and access the decoded message fields. When a `COMMAND_LONG` is received, it responds with a `COMMAND_ACK`, which is then displayed via `CommandProtocol.TryParseCommandAck()`. Tx and Rx are displayed in separate halves of the screen. See the source code for details.
    *   **Mission Sample:** On startup, `MissionSample` runs a self-contained, in-memory demonstration of the **Mission Protocol** — it simulates both a ground station and a flight controller over in-memory channels and exercises mission **upload**, **download**, and **clear** using `MissionProtocol.UploadMissionAsync()`, `DownloadMissionAsync()`, and `ClearMissionAsync()`.
    *   **Parameter Sample:** `ParameterSample` runs a self-contained, in-memory demonstration of the **Parameter Protocol** — it simulates a ground station and a flight controller over in-memory channels and exercises parameter **download**, **read**, and **set** using `ParameterProtocol.DownloadParametersAsync()`, `ReadParameterAsync()`, and `SetParameterAsync()`.

This example provides a quick way to:
*   **Test your MAVLinkSharp integration:** Verify that your application can correctly send and receive messages.
*   **Debug MAVLink communication:** Simulate both a MAVLink source and a listener within one application.
*   **Understand basic usage:** See concrete implementations of MAVLink message handling.

**To run this example:**

1.  Navigate to the `MavLinkConsole` project directory in your terminal.
2.  Run the project using `dotnet run`.
    *   By default (no arguments) it shows an **interactive options menu**:

        ```
        === MavLinkConsole ===

        Protocol demos (in-memory, complete automatically):
          1. Mission Protocol demo
          2. Parameter Protocol demo
          3. All protocol demos

        UDP telemetry demo (S=pause, R=resume, Enter=back):
          4. Tx/Rx demo

          0. Exit
        ```

        Options **1–3** run the in-memory protocol demo(s) to completion (no duration prompt) and return to the menu. Option **4** streams UDP telemetry in a split-pane Tx/Rx view. While streaming, use the following keyboard controls:
        *   **S** — Pause the stream (Tx stops sending)
        *   **R** — Resume the stream (Tx starts sending again)
        *   **Enter** — Stop the stream and return to the menu
3.  Command-line options (run non-interactively):
    *   `dotnet run -- --all` — run the Mission, Parameter, and UDP Tx/Rx demos.
    *   `dotnet run -- --tx` — UDP Tx/Rx demo only (streams until Ctrl+C in non-interactive mode).
    *   `dotnet run -- --rx` — UDP Rx only, listening on the default port 14550 (until Ctrl+C).
    *   `dotnet run -- --mission` — in-memory Mission Protocol demo only (upload/download/clear), then exits.
    *   `dotnet run -- --param` — in-memory Parameter Protocol demo only (download/read/set), then exits.
    *   `dotnet run -- --help` — show usage help.

## Benchmark Project: MavLinkSharp.Benchmark

The `MavLinkSharp.Benchmark` project is a dedicated suite for measuring the performance characteristics of the `MavLinkSharp` library. It leverages **BenchmarkDotNet** to provide accurate and reliable performance metrics for critical operations.

Benchmarks currently included:
*   **CRC Calculation:** Measures the speed of `Crc.Calculate()` for MAVLink packet checksums.
*   **Message Parsing:** Evaluates the performance of `frame.TryParse()` for decoding incoming MAVLink packets.

*   **MavLink Initialization:** Measures the initial loading and parsing time of MAVLink XML dialect files via `MavLink.Initialize()`.

These benchmarks help identify performance bottlenecks and track optimizations within the library.

**To run the benchmarks:**

1.  Navigate to the `MavLinkSharp.Benchmark` project directory in your terminal.
2.  Run the project in Release mode (essential for accurate results) using the following command:
    ```bash
    dotnet run -c Release --project MavLinkSharp.Benchmark/MavLinkSharp.Benchmark.csproj -- --filter *
    ```
    The `--filter *` argument ensures all benchmarks within the project are executed. BenchmarkDotNet will produce detailed reports in the `BenchmarkDotNet.Artifacts/results` directory.

## MavLinkSharp vs pymavlink

If you're coming from the Python ecosystem, you're likely familiar with [pymavlink](https://github.com/ArduPilot/pymavlink). Here's how the two libraries compare:

| Aspect | MavLinkSharp | pymavlink |
|--------|-------------|-----------|
| **Language** | C# (.NET) | Python |
| **Dialect Handling** | **Runtime parsing** — XML files are loaded and parsed at startup. No code generation. | **Code generation** — XML files must be pre-processed with `mavgen.py` to produce Python code. |
| **Adding a New Dialect** | Drop the XML file into your project and call `MavLink.Initialize("dialect.xml")`. Restart and go. | Run `mavgen.py` to regenerate Python modules, update imports, and redeploy. |
| **Performance** | Compiled .NET with `Span<T>` optimizations, zero-allocation parsing paths. Significantly faster for high-throughput scenarios. | Interpreted Python — suitable for moderate throughput, but GC and GIL can be bottlenecks under load. |
| **Command Protocol** | Built-in `CommandProtocol` API with `COMMAND_LONG`/`COMMAND_INT` factories, `COMMAND_ACK` parsing, and async send with timeout/retry. | Manual `mavutil.mavlink.COMMAND_LONG` construction and ACK handling. |
| **Streaming** | Built-in `System.IO.Pipelines` support for zero-copy async stream parsing. | Manual buffering required. |
| **AOT / Native Compilation** | Supports .NET Native AOT — compile to a single native binary with no dependencies. | Not applicable (Python). |
| **Platform** | Cross-platform (Windows, Linux, macOS) via .NET. | Cross-platform (Python runtime required). |
| **Typical Use Case** | High-performance .NET applications across industries: GCS software, robotics, telemetry gateways, real-time services, embedded Linux systems, agriculture, and maritime platforms. | Python scripting, testing, simulation tooling, research workflows, companion-computer utilities. |

### When to Choose MavLinkSharp

- You're building a .NET application (C#, F#, VB.NET) and want **native performance** with **no code generation overhead**.
- You need **high-throughput MAVLink parsing** (e.g., recording full telemetry streams, gateway services).
- You're building robotics or autonomous-systems software across air, ground, or marine platforms.
- You want **AOT-compiled standalone binaries** for deployment without a runtime.
- You need built-in **MAVLink 2 signing** support.

### When to Choose pymavlink

- You're working in **Python** and need tight integration with Python-based tools.
- You're doing **quick prototyping, testing, or data analysis** in Jupyter notebooks.
- You need the extensive protocol-level utilities pymavlink provides (parameter handling, firmware upload, etc.).
