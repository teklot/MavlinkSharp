# MavLinkSharp

MavlinkSharp is a lightweight .NET library for parsing MAVLink v1/v2 raw messages using standard or custom dialects. It is extremely fast, flexible, and easy to use, and also provides tools for constructing and encoding MAVLink packets for transmission over any communication protocol.

## Features
 - **Runtime Dialect Parsing:** Consumes standard MAVLink XML dialect files at runtime. No code generation required.
 - **Extensible:** Supports custom dialects with no extra effort. Just provide the XML file.
 - **High Performance:** Designed for speed and low allocation to handle high-throughput MAVLink streams.
 - **Cross-Platform:** Can be used on any platform that supports .NET Standard 2.1 (Windows, Linux, macOS, etc.).
 - **No External Dependencies:** The core parsing library is self-contained.

## Supported Frameworks

`MavLinkSharp` targets **.NET Standard 2.1**, making it compatible with a wide range of modern .NET platforms. This choice ensures access to a rich set of APIs while maintaining broad compatibility for most new and existing projects.

### Compatible Platforms:

*   **.NET Core:** 3.0 and later.
*   **Mono:** 6.4 and later.
*   **Xamarin.iOS:** 12.16 and later.
*   **Xamarin.Mac:** 5.18 and later.
*   **Xamarin.Android:** 10.0 and later.

**Important Note:** .NET Standard 2.1 is **NOT compatible with .NET Framework**. If you require compatibility with .NET Framework applications (versions 4.6.1 through 4.8), you would need to use a library targeting `.NET Standard 2.0`.

## Getting Started

Using the library involves four main steps:

1.  **Add the NuGet Package:** Install the `MavLinkSharp` package from NuGet into your .NET project.
	```
	Install-Package MavLinkSharp
	```
2.  **Initialize the Library:** At application startup, call the static `MavLink.Initialize()` method. You must specify which dialect file to use (e.g., `common.xml`).
    > **Important:** This step is mandatory. Calling `Message.TryParse()` before `MavLink.Initialize()` will result in an `InvalidOperationException`.
3.  **Parse Incoming Data:** As you receive data from a MAVLink stream (e.g., a UDP client or serial port), pass the raw `byte[]` packet to the `Message.TryParse()` static method.
4.  **Use the Result:** If `TryParse()` returns `true`, the `out` parameter will be a populated `Frame` object containing the decoded message and its fields.

## Dialect Handling

The `MavLinkSharp` library utilizes a **runtime parsing mechanism** for MAVLink XML dialect files. This means message definitions are loaded and processed dynamically when your application starts, rather than requiring code generation.

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
    *   **Behavior**: The specified messages will be marked as excluded and will be ignored by `Message.TryParse()`. Note that the **HEARTBEAT (#0)** message cannot be excluded.
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

After initialization and any fine-tuning, process incoming byte streams with `Message.TryParse()`. Only the enabled messages (including HEARTBEAT) will yield a valid `Frame` object.

## Code Example
```cs
using MavLinkSharp;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

// 1. Initialize the library with the desired dialect.
// This loads the message definitions from 'common.xml'.
MavLink.Initialize(DialectType.Common);

// 2. Specify which messages you want to parse.
// Let's listen for SYS_STATUS (#1) and ATTITUDE (#30).
// The HEARTBEAT (#0) message is always included by default.
MavLink.IncludeMessages(1, 30);

// Example: Listen for MAVLink packets on a local UDP port.
var endpoint = new IPEndPoint(IPAddress.Loopback, 14550);

using var udpClient = new UdpClient(endpoint);

Console.WriteLine($"Listening for MAVLink packets on {endpoint}...\n"});

while (true)
{
    // Receive a raw byte packet.
    var packet = udpClient.Receive(ref endpoint);

    // 3. Try to parse the packet.
    if (Message.TryParse(packet, out var frame))
    {
        // 4. If successful, use the data.
        var fields = string.Join(", ", frame.Fields.Select(f => $"{f.Key}: {f.Value}"));
        
        Console.WriteLine($"Received: {Metadata.Messages[frame.MessageId].Name} => {fields}");
    }
}
```

## Example Projects: MavLinkTx and MavLinkRx

The `MavLinkTx` and `MavLinkRx` projects serve as practical examples demonstrating how to use the `MavLinkSharp` library for sending and receiving MAVLink messages over UDP. They are particularly useful for testing and development.

*   **`MavLinkTx` (Transmitter):** This console application generates and sends synthetic MAVLink messages (e.g., HEARTBEAT, GPS_RAW_INT, ATTITUDE) over UDP to the default MAVLink port (UDP 14550). It showcases how to construct MAVLink `Frame` objects and serialize them into byte arrays for transmission.

*   **`MavLinkRx` (Receiver):** This console application listens for incoming MAVLink UDP packets on the default MAVLink port (UDP 14550). It demonstrates how to parse raw byte arrays into `Frame` objects using `Message.TryParse()` and access the decoded message fields.

These examples provide a quick way to:
*   **Test your MAVLinkSharp integration:** Verify that your application can correctly send and receive messages.
*   **Debug MAVLink communication:** Use `MavLinkTx` to simulate a MAVLink source and `MavLinkRx` to inspect incoming messages.
*   **Understand basic usage:** See concrete implementations of MAVLink message handling.

**To run these examples:**

1.  Navigate to the `MavLinkTx` or `MavLinkRx` project directory in your terminal.
2.  Run the project using `dotnet run`.
    *   For `MavLinkTx`, you will see messages being sent.
    *   For `MavLinkRx`, it will wait for and display incoming MAVLink messages.
