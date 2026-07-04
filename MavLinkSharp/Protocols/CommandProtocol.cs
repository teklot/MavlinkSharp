using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MavLinkSharp.Protocols
{
    /// <summary>
    /// MAVLink command result values (MAV_RESULT).
    /// </summary>
    public enum MavResult : byte
    {
        /// <summary>Command was accepted and executed.</summary>
        Accepted = 0,
        /// <summary>Command is temporarily rejected (e.g., vehicle is busy).</summary>
        TemporarilyRejected = 1,
        /// <summary>Command was denied (e.g., safety check failed).</summary>
        Denied = 2,
        /// <summary>Command is not supported by the system.</summary>
        Unsupported = 3,
        /// <summary>Command execution failed.</summary>
        Failed = 4,
        /// <summary>Command is in progress (long-running command).</summary>
        InProgress = 5,
        /// <summary>Command was cancelled.</summary>
        Cancelled = 6,
        /// <summary>Command denied because landing is in progress.</summary>
        CommandDeniedLanding = 7
    }

    /// <summary>
    /// Represents the result of a MAVLink command, parsed from a COMMAND_ACK frame.
    /// </summary>
    public class CommandResult
    {
        /// <summary>
        /// The command ID being acknowledged (MAV_CMD value).
        /// </summary>
        public ushort Command { get; set; }

        /// <summary>
        /// The result of the command.
        /// </summary>
        public MavResult Result { get; set; }

        /// <summary>
        /// Progress percentage (0-100), or 0 if unknown.
        /// </summary>
        public byte Progress { get; set; }

        /// <summary>
        /// Additional result parameter (e.g., MAV_MISSION_RESULT).
        /// </summary>
        public int ResultParam2 { get; set; }

        /// <summary>
        /// System that sent the original command (MAVLink 2 extension).
        /// </summary>
        public byte TargetSystem { get; set; }

        /// <summary>
        /// Component that sent the original command (MAVLink 2 extension).
        /// </summary>
        public byte TargetComponent { get; set; }

        /// <summary>
        /// Whether the command was accepted.
        /// </summary>
        public bool Success => Result == MavResult.Accepted;
    }

    /// <summary>
    /// Provides methods for constructing and processing MAVLink command protocol messages
    /// (COMMAND_LONG, COMMAND_INT, COMMAND_ACK, COMMAND_CANCEL).
    /// </summary>
    public static class CommandProtocol
    {
        /// <summary>COMMAND_INT message ID.</summary>
        public const uint CommandIntId = 75;
        /// <summary>COMMAND_LONG message ID.</summary>
        public const uint CommandLongId = 76;
        /// <summary>COMMAND_ACK message ID.</summary>
        public const uint CommandAckId = 77;
        /// <summary>COMMAND_CANCEL message ID.</summary>
        public const uint CommandCancelId = 80;

        /// <summary>
        /// Creates a COMMAND_LONG frame for sending a command with float parameters.
        /// </summary>
        /// <param name="context">The MAVLink dialect context.</param>
        /// <param name="systemId">Sending system ID.</param>
        /// <param name="componentId">Sending component ID.</param>
        /// <param name="targetSystem">Target system ID.</param>
        /// <param name="targetComponent">Target component ID (0 for all components).</param>
        /// <param name="command">The MAV_CMD command value.</param>
        /// <param name="parameters">Up to 7 float parameters.</param>
        /// <param name="confirmation">0 for first send, increment for retries.</param>
        /// <param name="sequence">Packet sequence number.</param>
        public static Frame CreateCommandLong(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            ushort command,
            ReadOnlySpan<float> parameters,
            byte confirmation = 0,
            byte sequence = 0)
        {
            var msg = context.Metadata.MessagesDictionary[CommandLongId];
            var frame = new Frame
            {
                Context = context,
                StartMarker = Protocol.V2.StartMarker,
                SystemId = systemId,
                ComponentId = componentId,
                PacketSequence = sequence,
                MessageId = CommandLongId,
                Message = msg
            };

            var values = new Dictionary<string, object>
            {
                ["target_system"] = targetSystem,
                ["target_component"] = targetComponent,
                ["command"] = command,
                ["confirmation"] = confirmation
            };

            int count = Math.Min(parameters.Length, 7);
            for (int i = 0; i < count; i++)
                values[$"param{i + 1}"] = parameters[i];

            frame.SetFields(values);
            return frame;
        }

        /// <summary>
        /// Creates a COMMAND_INT frame for sending a command with integer coordinates.
        /// </summary>
        /// <param name="context">The MAVLink dialect context.</param>
        /// <param name="systemId">Sending system ID.</param>
        /// <param name="componentId">Sending component ID.</param>
        /// <param name="targetSystem">Target system ID.</param>
        /// <param name="targetComponent">Target component ID (0 for all components).</param>
        /// <param name="frameType">MAV_FRAME coordinate frame.</param>
        /// <param name="command">The MAV_CMD command value.</param>
        /// <param name="parameters">Up to 4 float parameters.</param>
        /// <param name="x">X coordinate (int32, latitude * 1e7).</param>
        /// <param name="y">Y coordinate (int32, longitude * 1e7).</param>
        /// <param name="z">Z coordinate (float, altitude).</param>
        /// <param name="sequence">Packet sequence number.</param>
        public static Frame CreateCommandInt(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            byte frameType,
            ushort command,
            ReadOnlySpan<float> parameters,
            int x,
            int y,
            float z,
            byte sequence = 0)
        {
            var msg = context.Metadata.MessagesDictionary[CommandIntId];
            var frame = new Frame
            {
                Context = context,
                StartMarker = Protocol.V2.StartMarker,
                SystemId = systemId,
                ComponentId = componentId,
                PacketSequence = sequence,
                MessageId = CommandIntId,
                Message = msg
            };

            var values = new Dictionary<string, object>
            {
                ["target_system"] = targetSystem,
                ["target_component"] = targetComponent,
                ["frame"] = frameType,
                ["command"] = command,
                ["current"] = (byte)0,
                ["autocontinue"] = (byte)0,
                ["x"] = x,
                ["y"] = y,
                ["z"] = z
            };

            int count = Math.Min(parameters.Length, 4);
            for (int i = 0; i < count; i++)
                values[$"param{i + 1}"] = parameters[i];

            frame.SetFields(values);
            return frame;
        }

        /// <summary>
        /// Attempts to parse a COMMAND_ACK frame into a <see cref="CommandResult"/>.
        /// </summary>
        /// <param name="frame">The parsed frame to inspect.</param>
        /// <param name="result">When successful, the parsed command result.</param>
        /// <returns><c>true</c> if the frame is a valid COMMAND_ACK; otherwise <c>false</c>.</returns>
        public static bool TryParseCommandAck(Frame frame, out CommandResult result)
        {
            result = null;
            if (frame.MessageId != CommandAckId || frame.Fields == null)
                return false;

            result = new CommandResult
            {
                Command = (ushort)frame.Fields["command"],
                Result = (MavResult)(byte)frame.Fields["result"],
                Progress = frame.Fields.ContainsKey("progress") ? (byte)frame.Fields["progress"] : (byte)0,
                ResultParam2 = frame.Fields.ContainsKey("result_param2") ? (int)frame.Fields["result_param2"] : 0,
                TargetSystem = frame.Fields.ContainsKey("target_system") ? (byte)frame.Fields["target_system"] : (byte)0,
                TargetComponent = frame.Fields.ContainsKey("target_component") ? (byte)frame.Fields["target_component"] : (byte)0
            };
            return true;
        }

        /// <summary>
        /// Creates a COMMAND_CANCEL frame to cancel a long-running command.
        /// </summary>
        public static Frame CreateCommandCancel(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            ushort command,
            byte sequence = 0)
        {
            var msg = context.Metadata.MessagesDictionary[CommandCancelId];
            var frame = new Frame
            {
                Context = context,
                StartMarker = Protocol.V2.StartMarker,
                SystemId = systemId,
                ComponentId = componentId,
                PacketSequence = sequence,
                MessageId = CommandCancelId,
                Message = msg
            };

            frame.SetFields(new Dictionary<string, object>
            {
                ["target_system"] = targetSystem,
                ["target_component"] = targetComponent,
                ["command"] = command
            });
            return frame;
        }

        /// <summary>
        /// Sends a command frame and waits for a matching COMMAND_ACK with timeout and retry support.
        /// </summary>
        /// <param name="commandFrame">The command frame to send (created via <see cref="CreateCommandLong"/> or <see cref="CreateCommandInt"/>).</param>
        /// <param name="sendAsync">Callback that sends the serialized command bytes.</param>
        /// <param name="receiveFrameAsync">Callback that returns one parsed frame from the incoming stream, or <c>null</c> on timeout.</param>
        /// <param name="timeoutMs">Milliseconds to wait for a COMMAND_ACK per attempt.</param>
        /// <param name="retries">Number of retry attempts on timeout (0 = single attempt).</param>
        /// <param name="progress">Optional progress reporter for in-progress results.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public static async Task<CommandResult> SendCommandAsync(
            Frame commandFrame,
            Func<byte[], CancellationToken, Task> sendAsync,
            Func<CancellationToken, Task<Frame>> receiveFrameAsync,
            int timeoutMs = 5000,
            int retries = 0,
            IProgress<CommandResult> progress = null,
            CancellationToken cancellationToken = default)
        {
            ushort expectedCommand = (ushort)commandFrame.Fields["command"];

            for (int attempt = 0; attempt <= retries; attempt++)
            {
                if (attempt > 0 && commandFrame.MessageId == CommandLongId)
                {
                    commandFrame.SetFields(new Dictionary<string, object>
                    {
                        ["confirmation"] = (byte)attempt
                    });
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeoutMs);

                try
                {
                    var bytes = commandFrame.ToBytes();
                    await sendAsync(bytes, cts.Token);

                    while (!cts.Token.IsCancellationRequested)
                    {
                        var response = await receiveFrameAsync(cts.Token);
                        if (response == null)
                            break;

                        if (TryParseCommandAck(response, out var result))
                        {
                            if (result.Command != expectedCommand)
                                continue;

                            progress?.Report(result);

                            if (result.Result == MavResult.InProgress)
                                continue;

                            return result;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    continue;
                }
            }

            return new CommandResult
            {
                Command = expectedCommand,
                Result = MavResult.Failed,
                Progress = 0,
                ResultParam2 = 0
            };
        }
    }
}
