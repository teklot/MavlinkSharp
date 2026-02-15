# MavLinkSharp

MavlinkSharp is a lightweight .NET library for parsing [MAVLink](https://mavlink.io/) v1/v2 raw messages using standard or custom dialects. It is extremely fast, flexible, and easy to use, and also provides tools for constructing and encoding MAVLink packets for transmission over any communication protocol.

## Features
 - **Runtime Dialect Parsing:** Consumes standard MAVLink XML dialect files at runtime. No code generation required.
 - **Extensible:** Supports custom dialects with no extra effort. Just provide the XML file.
 - **High Performance:** Designed for speed and low allocation to handle high-throughput MAVLink streams.
 - **Streaming Ready:** Built-in support for `System.IO.Pipelines` (`PipeReader`) to handle fragmented data streams efficiently.
 - **Cross-Platform:** Can be used on any platform that supports .NET Standard 2.0 (Windows, Linux, macOS, etc.).
 - **Minimal Dependencies:** Only requires `System.Memory` and `System.IO.Pipelines`.

## Supported Frameworks

`MavLinkSharp` targets **.NET Standard 2.0**, providing broad compatibility across nearly all modern and legacy .NET platforms, including .NET Framework.

### Compatible Platforms:

*   **.NET Core / .NET (5+):** All versions.
*   **.NET Framework:** 4.6.1 and later (4.7.2+ recommended).
*   **Mono:** 5.4 and later.
*   **Xamarin.iOS:** 10.14 and later.
*   **Xamarin.Android:** 8.0 and later.
*   **UWP:** 10.0.16299 and later.

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

## Dialect Handling

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

## Filtering Messages

The `MavLinkSharp` library offers flexible control over which MAVLink messages are parsed, allowing you to optimize for performance by only processing messages relevant to your application.

### Initializing Message Parsing

Message filtering begins with the `MavLink.Initialize()` method. This method loads message definitions from your specified MAVLink dialect(s) and simultaneously defines the initial set of messages that will be processed.

The `MavLink.Initialize()` method has the following signatures:
```cs
public static void Initialize(DialectType dialogType, params uint[] messageIds)

public static void Initialize(string dialectPath, params uint[] messageIds)
```
*   **`dialogType/dialectPath`**: (Required) The type of or path to the main dialect file to load.
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
var frame = new Frame();

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
        var fields = string.Join(", ", frame.Fields.Select(f => $"{f.Key}: {f.Value}"));
        
        Console.WriteLine($"Received: {Metadata.Messages[frame.MessageId].Name} => {fields}");
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
var values = new Dictionary<string, object>
{
    { "custom_mode", (uint)0 },
    { "type", (byte)6 },         // MAV_TYPE_GCS
    { "autopilot", (byte)8 },    // MAV_AUTOPILOT_INVALID
    { "base_mode", (byte)0 },
    { "system_status", (byte)4 }, // MAV_STATE_ACTIVE
    { "mavlink_version", (byte)3 }
};
frame.SetFields(values);

// 4. Serialize to a byte array.
byte[] packet = frame.ToBytes();

// 5. Send over your transport (e.g., UDP).
udpClient.Send(packet, packet.Length, remoteEndPoint);
```

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

## Example Project: MavLinkConsole

The `MavLinkConsole` project serves as a practical example demonstrating how to use the `MavLinkSharp` library for both sending and receiving MAVLink messages over UDP, all within a single console application. It's particularly useful for testing, development, and quickly observing MAVLink communication.

*   **`MavLinkConsole` (Transmitter & Receiver):** This console application runs two concurrent tasks:
    *   **Transmitter (Tx):** Generates and sends synthetic MAVLink messages (e.g., HEARTBEAT, GPS_RAW_INT, ATTITUDE) over UDP to the default MAVLink port (UDP 14550). It showcases how to construct MAVLink `Frame` objects and serialize them into byte arrays for transmission.
    *   **Receiver (Rx):** Listens for incoming MAVLink UDP packets on the default MAVLink port (UDP 14550). It demonstrates how to parse raw byte arrays into `Frame` objects using `frame.TryParse()` and access the decoded message fields. Tx and Rx are displayed in separate halves of the screen. See the source code for details.

This example provides a quick way to:
*   **Test your MAVLinkSharp integration:** Verify that your application can correctly send and receive messages.
*   **Debug MAVLink communication:** Simulate both a MAVLink source and a listener within one application.
*   **Understand basic usage:** See concrete implementations of MAVLink message handling.

**To run this example:**

1.  Navigate to the `MavLinkConsole` project directory in your terminal.
2.  Run the project using `dotnet run`.
    *   You will see both `Tx =>` (transmitted) and `Rx =>` (received) messages in the same terminal.

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
