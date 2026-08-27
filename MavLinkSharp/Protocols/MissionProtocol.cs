using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MavLinkSharp.Protocols
{
    /// <summary>
    /// MAVLink coordinate frame values (MAV_FRAME) used by mission items.
    /// </summary>
    public enum MavFrame : byte
    {
        /// <summary>Global (WGS84) coordinate frame + MSL altitude.</summary>
        Global = 0,
        /// <summary>NED local tangent frame (x: North, y: East, z: Down).</summary>
        LocalNed = 1,
        /// <summary>NOT a coordinate frame, indicates a mission command.</summary>
        Mission = 2,
        /// <summary>Global coordinate frame + altitude relative to the home position.</summary>
        GlobalRelativeAlt = 3,
        /// <summary>ENU local tangent frame (x: East, y: North, z: Up).</summary>
        LocalEnu = 4,
        /// <summary>Global (WGS84) coordinate frame (scaled) + MSL altitude.</summary>
        GlobalInt = 5,
        /// <summary>Global coordinate frame (scaled) + altitude relative to the home position.</summary>
        GlobalRelativeAltInt = 6,
        /// <summary>NED local tangent frame whose origin travels with the vehicle.</summary>
        LocalOffsetNed = 7,
        /// <summary>Global coordinate frame with AGL altitude.</summary>
        GlobalTerrainAlt = 10,
        /// <summary>Global coordinate frame (scaled) with AGL altitude.</summary>
        GlobalTerrainAltInt = 11,
        /// <summary>FRD local frame aligned to the vehicle's attitude.</summary>
        BodyFrd = 12,
        /// <summary>FRD local tangent frame with origin fixed relative to earth.</summary>
        LocalFrd = 20,
        /// <summary>FLU local tangent frame with origin fixed relative to earth.</summary>
        LocalFlu = 21
    }

    /// <summary>
    /// MAVLink mission operation result values (MAV_MISSION_RESULT).
    /// </summary>
    public enum MavMissionResult : byte
    {
        /// <summary>Mission accepted OK.</summary>
        Accepted = 0,
        /// <summary>Generic error / not accepting mission commands at all right now.</summary>
        Error = 1,
        /// <summary>Coordinate frame is not supported.</summary>
        UnsupportedFrame = 2,
        /// <summary>Command is not supported.</summary>
        Unsupported = 3,
        /// <summary>Mission items exceed storage space.</summary>
        NoSpace = 4,
        /// <summary>One of the parameters has an invalid value.</summary>
        Invalid = 5,
        /// <summary>param1 has an invalid value.</summary>
        InvalidParam1 = 6,
        /// <summary>param2 has an invalid value.</summary>
        InvalidParam2 = 7,
        /// <summary>param3 has an invalid value.</summary>
        InvalidParam3 = 8,
        /// <summary>param4 has an invalid value.</summary>
        InvalidParam4 = 9,
        /// <summary>x / param5 has an invalid value.</summary>
        InvalidParam5X = 10,
        /// <summary>y / param6 has an invalid value.</summary>
        InvalidParam6Y = 11,
        /// <summary>z / param7 has an invalid value.</summary>
        InvalidParam7 = 12,
        /// <summary>Mission item received out of sequence.</summary>
        InvalidSequence = 13,
        /// <summary>Not accepting any mission commands from this communication partner.</summary>
        Denied = 14,
        /// <summary>Current mission operation cancelled (e.g. mission upload, mission download).</summary>
        OperationCancelled = 15
    }

    /// <summary>
    /// MAVLink mission type values (MAV_MISSION_TYPE).
    /// </summary>
    public enum MavMissionType : byte
    {
        /// <summary>Items are mission commands for the main mission.</summary>
        Mission = 0,
        /// <summary>Specifies GeoFence area(s).</summary>
        Fence = 1,
        /// <summary>Specifies the rally points for the vehicle.</summary>
        Rally = 2,
        /// <summary>Mission type is not specified / all missions.</summary>
        All = 255
    }

    /// <summary>
    /// Represents a mission plan item transmitted in a MISSION_ITEM_INT message.
    /// </summary>
    public class MissionItem
    {
        /// <summary>Waypoint ID (sequence number). Starts at zero.</summary>
        public ushort Seq { get; set; }

        /// <summary>The scheduled action (MAV_CMD value) for the waypoint.</summary>
        public ushort Command { get; set; }

        /// <summary>The coordinate system of the waypoint.</summary>
        public MavFrame Frame { get; set; }

        /// <summary>Whether this is the current mission item. false: 0, true: 1.</summary>
        public byte Current { get; set; }

        /// <summary>Autocontinue to next waypoint. Set false to pause after the item completes.</summary>
        public byte AutoContinue { get; set; }

        /// <summary>PARAM1, see MAV_CMD enum.</summary>
        public float Param1 { get; set; }

        /// <summary>PARAM2, see MAV_CMD enum.</summary>
        public float Param2 { get; set; }

        /// <summary>PARAM3, see MAV_CMD enum.</summary>
        public float Param3 { get; set; }

        /// <summary>PARAM4, see MAV_CMD enum.</summary>
        public float Param4 { get; set; }

        /// <summary>PARAM5 / local: x in meters*1e4, global: latitude in degrees*1e7.</summary>
        public int X { get; set; }

        /// <summary>PARAM6 / y: local in meters*1e4, global: longitude in degrees*1e7.</summary>
        public int Y { get; set; }

        /// <summary>PARAM7 / z: altitude in meters (relative or absolute, depending on frame).</summary>
        public float Z { get; set; }

        /// <summary>Mission type.</summary>
        public MavMissionType Type { get; set; }
    }

    /// <summary>
    /// Represents the result of a mission operation, parsed from a MISSION_ACK frame.
    /// </summary>
    public class MissionAck
    {
        /// <summary>The result of the mission operation.</summary>
        public MavMissionResult Result { get; set; }

        /// <summary>The mission type being acknowledged.</summary>
        public MavMissionType Type { get; set; }

        /// <summary>The target system.</summary>
        public byte TargetSystem { get; set; }

        /// <summary>The target component.</summary>
        public byte TargetComponent { get; set; }

        /// <summary>Whether the mission operation was accepted.</summary>
        public bool Success => Result == MavMissionResult.Accepted;
    }

    /// <summary>
    /// Represents the result of a mission download operation.
    /// </summary>
    public class MissionDownload
    {
        /// <summary>The downloaded mission items, ordered by sequence number.</summary>
        public IReadOnlyList<MissionItem> Items { get; set; } = Array.Empty<MissionItem>();

        /// <summary>The mission type that was downloaded.</summary>
        public MavMissionType Type { get; set; }
    }

    /// <summary>
    /// Represents progress during a mission upload or download operation.
    /// </summary>
    public class MissionProgress
    {
        /// <summary>The current item being processed (index).</summary>
        public int Current { get; internal set; }

        /// <summary>The total number of items being processed.</summary>
        public int Total { get; internal set; }
    }

    /// <summary>
    /// Thrown when a mission operation is aborted by a non-accepted MISSION_ACK.
    /// </summary>
    public sealed class MissionAbortedException : Exception
    {
        /// <summary>
        /// The MISSION_ACK that aborted the operation.
        /// </summary>
        public MissionAck Ack { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MissionAbortedException"/> class.
        /// </summary>
        public MissionAbortedException(MissionAck ack)
            : base($"Mission operation aborted with result '{ack.Result}'.")
        {
            Ack = ack;
        }
    }

    /// <summary>
    /// Thrown internally (and converted to <see cref="TimeoutException"/>) when the expected
    /// mission-protocol response is not received within the timeout.
    /// </summary>
    internal sealed class MissionTimeoutException : Exception
    {
        public MissionTimeoutException(string message) : base(message) { }
    }

    /// <summary>
    /// Provides methods for constructing and processing MAVLink mission protocol messages
    /// (MISSION_REQUEST_LIST, MISSION_COUNT, MISSION_REQUEST_INT, MISSION_ITEM_INT, MISSION_ACK,
    /// MISSION_CLEAR_ALL, MISSION_SET_CURRENT) and for uploading/downloading/clearing flight plans.
    /// </summary>
    public static class MissionProtocol
    {
        /// <summary>MISSION_SET_CURRENT message ID.</summary>
        public const uint MissionSetCurrentId = 41;
        /// <summary>MISSION_CURRENT message ID.</summary>
        public const uint MissionCurrentId = 42;
        /// <summary>MISSION_REQUEST_LIST message ID.</summary>
        public const uint MissionRequestListId = 43;
        /// <summary>MISSION_COUNT message ID.</summary>
        public const uint MissionCountId = 44;
        /// <summary>MISSION_CLEAR_ALL message ID.</summary>
        public const uint MissionClearAllId = 45;
        /// <summary>MISSION_ITEM_REACHED message ID.</summary>
        public const uint MissionItemReachedId = 46;
        /// <summary>MISSION_ACK message ID.</summary>
        public const uint MissionAckId = 47;
        /// <summary>MISSION_REQUEST_INT message ID.</summary>
        public const uint MissionRequestIntId = 51;
        /// <summary>MISSION_ITEM_INT message ID.</summary>
        public const uint MissionItemIntId = 73;

        /// <summary>Default timeout in milliseconds for general mission-protocol exchanges.</summary>
        public const int DefaultTimeoutMs = 1500;

        /// <summary>Default timeout in milliseconds for individual plan-item exchanges.</summary>
        public const int DefaultItemTimeoutMs = 250;

        /// <summary>Default maximum number of retries per exchange.</summary>
        public const int DefaultRetries = 5;

        /// <summary>
        /// Creates a MISSION_REQUEST_LIST frame (id 43) to initiate a mission download.
        /// </summary>
        public static Frame CreateMissionRequestList(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            MavMissionType missionType = MavMissionType.Mission,
            byte sequence = 0)
        {
            var msg = context.Metadata.MessagesDictionary[MissionRequestListId];
            var frame = NewFrame(context, systemId, componentId, sequence, MissionRequestListId, msg);
            frame.SetFields(new Dictionary<string, object>
            {
                ["target_system"] = targetSystem,
                ["target_component"] = targetComponent,
                ["mission_type"] = (byte)missionType
            });
            return frame;
        }

        /// <summary>
        /// Creates a MISSION_COUNT frame (id 44) to initiate a mission upload or acknowledge a download.
        /// </summary>
        /// <param name="context">The MAVLink dialect context.</param>
        /// <param name="systemId">Sending system ID.</param>
        /// <param name="componentId">Sending component ID.</param>
        /// <param name="targetSystem">Target system ID.</param>
        /// <param name="targetComponent">Target component ID.</param>
        /// <param name="count">The number of mission items in the sequence.</param>
        /// <param name="missionType">The mission type.</param>
        /// <param name="sequence">Packet sequence number.</param>
        public static Frame CreateMissionCount(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            ushort count,
            MavMissionType missionType = MavMissionType.Mission,
            byte sequence = 0)
        {
            var msg = context.Metadata.MessagesDictionary[MissionCountId];
            var frame = NewFrame(context, systemId, componentId, sequence, MissionCountId, msg);
            frame.SetFields(new Dictionary<string, object>
            {
                ["target_system"] = targetSystem,
                ["target_component"] = targetComponent,
                ["count"] = count,
                ["mission_type"] = (byte)missionType
            });
            return frame;
        }

        /// <summary>
        /// Creates a MISSION_CLEAR_ALL frame (id 45) to clear a mission from the target system.
        /// </summary>
        public static Frame CreateMissionClearAll(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            MavMissionType missionType = MavMissionType.Mission,
            byte sequence = 0)
        {
            var msg = context.Metadata.MessagesDictionary[MissionClearAllId];
            var frame = NewFrame(context, systemId, componentId, sequence, MissionClearAllId, msg);
            frame.SetFields(new Dictionary<string, object>
            {
                ["target_system"] = targetSystem,
                ["target_component"] = targetComponent,
                ["mission_type"] = (byte)missionType
            });
            return frame;
        }

        /// <summary>
        /// Creates a MISSION_REQUEST_INT frame (id 51) requesting a specific mission item.
        /// </summary>
        /// <param name="context">The MAVLink dialect context.</param>
        /// <param name="systemId">Sending system ID.</param>
        /// <param name="componentId">Sending component ID.</param>
        /// <param name="targetSystem">Target system ID.</param>
        /// <param name="targetComponent">Target component ID.</param>
        /// <param name="seq">The sequence number of the requested item.</param>
        /// <param name="missionType">The mission type.</param>
        /// <param name="sequence">Packet sequence number.</param>
        public static Frame CreateMissionRequestInt(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            ushort seq,
            MavMissionType missionType = MavMissionType.Mission,
            byte sequence = 0)
        {
            var msg = context.Metadata.MessagesDictionary[MissionRequestIntId];
            var frame = NewFrame(context, systemId, componentId, sequence, MissionRequestIntId, msg);
            frame.SetFields(new Dictionary<string, object>
            {
                ["target_system"] = targetSystem,
                ["target_component"] = targetComponent,
                ["seq"] = seq,
                ["mission_type"] = (byte)missionType
            });
            return frame;
        }

        /// <summary>
        /// Creates a MISSION_ITEM_INT frame (id 73) carrying a mission plan item.
        /// </summary>
        public static Frame CreateMissionItemInt(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            MissionItem item,
            byte sequence = 0)
        {
            var msg = context.Metadata.MessagesDictionary[MissionItemIntId];
            var frame = NewFrame(context, systemId, componentId, sequence, MissionItemIntId, msg);
            frame.SetFields(new Dictionary<string, object>
            {
                ["target_system"] = targetSystem,
                ["target_component"] = targetComponent,
                ["seq"] = item.Seq,
                ["frame"] = (byte)item.Frame,
                ["command"] = item.Command,
                ["current"] = item.Current,
                ["autocontinue"] = item.AutoContinue,
                ["param1"] = item.Param1,
                ["param2"] = item.Param2,
                ["param3"] = item.Param3,
                ["param4"] = item.Param4,
                ["x"] = item.X,
                ["y"] = item.Y,
                ["z"] = item.Z,
                ["mission_type"] = (byte)item.Type
            });
            return frame;
        }

        /// <summary>
        /// Creates a MISSION_SET_CURRENT frame (id 41) to set the current mission item.
        /// </summary>
        /// <param name="context">The MAVLink dialect context.</param>
        /// <param name="systemId">Sending system ID.</param>
        /// <param name="componentId">Sending component ID.</param>
        /// <param name="targetSystem">Target system ID.</param>
        /// <param name="targetComponent">Target component ID.</param>
        /// <param name="seq">The sequence number to set as current.</param>
        /// <param name="sequence">Packet sequence number.</param>
        public static Frame CreateMissionSetCurrent(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            ushort seq,
            byte sequence = 0)
        {
            var msg = context.Metadata.MessagesDictionary[MissionSetCurrentId];
            var frame = NewFrame(context, systemId, componentId, sequence, MissionSetCurrentId, msg);
            frame.SetFields(new Dictionary<string, object>
            {
                ["target_system"] = targetSystem,
                ["target_component"] = targetComponent,
                ["seq"] = seq
            });
            return frame;
        }

        /// <summary>
        /// Attempts to parse a MISSION_ACK frame (id 47) into a <see cref="MissionAck"/>.
        /// </summary>
        /// <param name="frame">The parsed frame to inspect.</param>
        /// <param name="result">When successful, the parsed mission ack.</param>
        /// <returns><c>true</c> if the frame is a valid MISSION_ACK; otherwise <c>false</c>.</returns>
        public static bool TryParseMissionAck(Frame frame, out MissionAck? result)
        {
            result = null;
            if (frame.MessageId != MissionAckId || frame.Fields == null)
                return false;

            result = new MissionAck
            {
                Result = (MavMissionResult)(byte)frame.Fields["type"],
                Type = frame.Fields.ContainsKey("mission_type") ? (MavMissionType)(byte)frame.Fields["mission_type"] : MavMissionType.Mission,
                TargetSystem = frame.Fields.ContainsKey("target_system") ? (byte)frame.Fields["target_system"] : (byte)0,
                TargetComponent = frame.Fields.ContainsKey("target_component") ? (byte)frame.Fields["target_component"] : (byte)0
            };
            return true;
        }

        /// <summary>
        /// Extracts a <see cref="MissionItem"/> from a MISSION_ITEM_INT frame (id 73).
        /// </summary>
        /// <param name="frame">The parsed frame to inspect.</param>
        /// <param name="item">When successful, the parsed mission item.</param>
        /// <returns><c>true</c> if the frame is a valid MISSION_ITEM_INT; otherwise <c>false</c>.</returns>
        public static bool TryParseMissionItem(Frame frame, out MissionItem? item)
        {
            item = null;
            if (frame.MessageId != MissionItemIntId || frame.Fields == null)
                return false;

            item = new MissionItem
            {
                Seq = (ushort)frame.Fields["seq"],
                Frame = (MavFrame)(byte)frame.Fields["frame"],
                Command = (ushort)frame.Fields["command"],
                Current = (byte)frame.Fields["current"],
                AutoContinue = (byte)frame.Fields["autocontinue"],
                Param1 = (float)frame.Fields["param1"],
                Param2 = (float)frame.Fields["param2"],
                Param3 = (float)frame.Fields["param3"],
                Param4 = (float)frame.Fields["param4"],
                X = (int)frame.Fields["x"],
                Y = (int)frame.Fields["y"],
                Z = (float)frame.Fields["z"],
                Type = frame.Fields.ContainsKey("mission_type") ? (MavMissionType)(byte)frame.Fields["mission_type"] : MavMissionType.Mission
            };
            return true;
        }

        /// <summary>
        /// Uploads a mission plan to the target system and waits for a MISSION_ACK with built-in
        /// timeout and retry. Emits MISSION_COUNT, then handles each
        /// MISSION_REQUEST_INT / MISSION_ITEM_INT exchange, and finally awaits the MISSION_ACK.
        /// </summary>
        /// <param name="items">The mission items to upload, ordered by sequence.</param>
        /// <param name="context">The MAVLink dialect context.</param>
        /// <param name="systemId">Sending system ID.</param>
        /// <param name="componentId">Sending component ID.</param>
        /// <param name="targetSystem">Target system ID.</param>
        /// <param name="targetComponent">Target component ID.</param>
        /// <param name="missionType">Mission type.</param>
        /// <param name="sendAsync">Callback that sends the serialized message bytes.</param>
        /// <param name="receiveFrameAsync">Callback that returns one parsed frame, or <c>null</c> on timeout.</param>
        /// <param name="timeoutMs">Milliseconds to wait for a general response per attempt.</param>
        /// <param name="itemTimeoutMs">Milliseconds to wait for a plan-item response per attempt.</param>
        /// <param name="maxRetries">Number of retry attempts on timeout.</param>
        /// <param name="progress">Optional progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The final <see cref="MissionAck"/> from the vehicle.</returns>
        public static async Task<MissionAck> UploadMissionAsync(
            IReadOnlyList<MissionItem> items,
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            MavMissionType missionType,
            Func<byte[], CancellationToken, Task> sendAsync,
            Func<CancellationToken, Task<Frame>> receiveFrameAsync,
            int timeoutMs = DefaultTimeoutMs,
            int itemTimeoutMs = DefaultItemTimeoutMs,
            int maxRetries = DefaultRetries,
            IProgress<MissionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (sendAsync == null) throw new ArgumentNullException(nameof(sendAsync));
            if (receiveFrameAsync == null) throw new ArgumentNullException(nameof(receiveFrameAsync));

            int count = items.Count;
            if (count > ushort.MaxValue)
                throw new ArgumentException("Mission item count exceeds the MAVLink ushort limit.", nameof(items));

            // An empty mission upload is equivalent to clearing the mission.
            if (count == 0)
            {
                return await ClearMissionAsync(
                    context, systemId, componentId, targetSystem, targetComponent,
                    sendAsync, receiveFrameAsync, missionType, timeoutMs, maxRetries, cancellationToken).ConfigureAwait(false);
            }

            // Step 1: announce the item count and wait for the first MISSION_REQUEST_INT(0).
            var countFrame = CreateMissionCount(context, systemId, componentId, targetSystem, targetComponent, (ushort)count, missionType);
            await SendAndMatchAsync(
                countFrame,
                timeoutMs,
                maxRetries,
                sendAsync,
                receiveFrameAsync,
                (f, seq) => TryMatchRequest(f, seq),
                0,
                missionType,
                cancellationToken).ConfigureAwait(false);

            progress?.Report(new MissionProgress { Current = 0, Total = count });

            // Steps 2-4: iterate the MISSION_REQUEST_INT / MISSION_ITEM_INT cycle.
            for (int i = 0; i < count; i++)
            {
                var itemFrame = CreateMissionItemInt(context, systemId, componentId, targetSystem, targetComponent, items[i]);
                var nextItemSeq = (ushort)(i + 1);
                bool isLast = i == count - 1;

                if (!isLast)
                {
                    // Non-final item: wait for the next MISSION_REQUEST_INT(i+1).
                    await SendAndMatchAsync(
                        itemFrame,
                        itemTimeoutMs,
                        maxRetries,
                        sendAsync,
                        receiveFrameAsync,
                        (f, seq) => TryMatchRequest(f, seq),
                        nextItemSeq,
                        missionType,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // Final item: send it and wait for the MISSION_ACK that completes the upload.
                    return await SendAndMatchAckAsync(
                        itemFrame,
                        timeoutMs,
                        maxRetries,
                        sendAsync,
                        receiveFrameAsync,
                        missionType,
                        cancellationToken).ConfigureAwait(false);
                }

                progress?.Report(new MissionProgress { Current = i + 1, Total = count });
            }

            throw new InvalidOperationException("Unreachable upload path.");
        }

        /// <summary>
        /// Downloads a mission plan from the target system with built-in timeout and retry.
        /// Emits MISSION_REQUEST_LIST, awaits MISSION_COUNT, requests each item,
        /// collects the MISSION_ITEM_INT messages, and finally sends a MISSION_ACK.
        /// </summary>
        /// <returns>The downloaded mission and its type.</returns>
        public static async Task<MissionDownload> DownloadMissionAsync(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            MavMissionType missionType,
            Func<byte[], CancellationToken, Task> sendAsync,
            Func<CancellationToken, Task<Frame>> receiveFrameAsync,
            int timeoutMs = DefaultTimeoutMs,
            int itemTimeoutMs = DefaultItemTimeoutMs,
            int maxRetries = DefaultRetries,
            IProgress<MissionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (sendAsync == null) throw new ArgumentNullException(nameof(sendAsync));
            if (receiveFrameAsync == null) throw new ArgumentNullException(nameof(receiveFrameAsync));

            // Step 1: request the list and await the MISSION_COUNT response.
            var listFrame = CreateMissionRequestList(context, systemId, componentId, targetSystem, targetComponent, missionType);
            ushort count = await SendAndMatchCountAsync(
                listFrame,
                timeoutMs,
                maxRetries,
                sendAsync,
                receiveFrameAsync,
                missionType,
                cancellationToken).ConfigureAwait(false);

            var items = new List<MissionItem>(count);
            progress?.Report(new MissionProgress { Current = 0, Total = count });

            // Steps 2-4: request each item and collect the corresponding item.
            for (ushort i = 0; i < count; i++)
            {
                var requestFrame = CreateMissionRequestInt(context, systemId, componentId, targetSystem, targetComponent, i, missionType);
                var received = await SendAndMatchFrameAsync(
                    requestFrame,
                    itemTimeoutMs,
                    maxRetries,
                    sendAsync,
                    receiveFrameAsync,
                    (f, seq) => TryMatchItem(f, seq),
                    i,
                    missionType,
                    cancellationToken).ConfigureAwait(false);

                if (!TryParseMissionItem(received, out var item) || item == null)
                    throw new InvalidOperationException("Received frame was not a valid MISSION_ITEM_INT.");

                items.Add(item);
                progress?.Report(new MissionProgress { Current = items.Count, Total = count });
            }

            // Step 5: acknowledge receipt of all items.
            var ackFrame = CreateMissionCount(context, systemId, componentId, targetSystem, targetComponent, count, missionType);
            var ackBytes = ackFrame.ToBytes();
            await sendAsync(ackBytes, cancellationToken).ConfigureAwait(false);

            return new MissionDownload { Items = items, Type = missionType };
        }

        /// <summary>
        /// Clears a mission plan from the target system and waits for a MISSION_ACK with built-in
        /// timeout and retry. Emits MISSION_CLEAR_ALL and awaits the acknowledgement.
        /// </summary>
        /// <returns>The resulting <see cref="MissionAck"/> from the vehicle.</returns>
        public static async Task<MissionAck> ClearMissionAsync(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            Func<byte[], CancellationToken, Task> sendAsync,
            Func<CancellationToken, Task<Frame>> receiveFrameAsync,
            MavMissionType missionType = MavMissionType.Mission,
            int timeoutMs = DefaultTimeoutMs,
            int maxRetries = DefaultRetries,
            CancellationToken cancellationToken = default)
        {
            if (sendAsync == null) throw new ArgumentNullException(nameof(sendAsync));
            if (receiveFrameAsync == null) throw new ArgumentNullException(nameof(receiveFrameAsync));

            var clearFrame = CreateMissionClearAll(context, systemId, componentId, targetSystem, targetComponent, missionType);
            return await SendAndMatchAckAsync(
                clearFrame,
                timeoutMs,
                maxRetries,
                sendAsync,
                receiveFrameAsync,
                missionType,
                cancellationToken).ConfigureAwait(false);
        }

        private static Frame NewFrame(MavLinkContext context, byte systemId, byte componentId, byte sequence, uint messageId, Message message)
        {
            return new Frame
            {
                Context = context,
                StartMarker = Protocol.V2.StartMarker,
                SystemId = systemId,
                ComponentId = componentId,
                PacketSequence = sequence,
                MessageId = messageId,
                Message = message
            };
        }

        private static bool TryMatchRequest(Frame frame, ushort seq)
        {
            return frame.MessageId == MissionRequestIntId
                   && frame.Fields != null
                   && (ushort)frame.Fields["seq"] == seq;
        }

        private static bool TryMatchItem(Frame frame, ushort seq)
        {
            return frame.MessageId == MissionItemIntId
                   && frame.Fields != null
                   && (ushort)frame.Fields["seq"] == seq;
        }

        private static MissionAck IfAbortingAck(Frame frame, MavMissionType missionType, out MissionAck? ack)
        {
            if (TryParseMissionAck(frame, out ack) && ack != null && !ack.Success && ack.Type == missionType)
                throw new MissionAbortedException(ack);
            ack = null;
            return null!;
        }

        private static async Task<Frame> ReceiveMatchingAsync(
            Func<byte[], CancellationToken, Task> sendAsync,
            Func<CancellationToken, Task<Frame>> receiveFrameAsync,
            Frame frame,
            Func<Frame, ushort, bool> match,
            ushort expectedSeq,
            int timeoutMs,
            int maxRetries,
            MavMissionType missionType,
            CancellationToken cancellationToken)
        {
            int totalTries = maxRetries + 1;
            for (int attempt = 0; attempt < totalTries; attempt++)
            {
                try
                {
                    await sendAsync(frame.ToBytes(), cancellationToken).ConfigureAwait(false);

                    while (true)
                    {
                        var response = await ReceiveFrameAsync(receiveFrameAsync, timeoutMs, missionType, cancellationToken).ConfigureAwait(false);
                        if (match(response, expectedSeq))
                            return response;
                        IfAbortingAck(response, missionType, out _);
                    }
                }
                catch (MissionTimeoutException)
                {
                    // Retry after timeout.
                }
            }

            throw new TimeoutException($"Mission exchange timed out after {totalTries} attempts.");
        }

        private static async Task<ushort> ReceiveCountAsync(
            Func<byte[], CancellationToken, Task> sendAsync,
            Func<CancellationToken, Task<Frame>> receiveFrameAsync,
            Frame frame,
            int timeoutMs,
            int maxRetries,
            MavMissionType missionType,
            CancellationToken cancellationToken)
        {
            int totalTries = maxRetries + 1;
            for (int attempt = 0; attempt < totalTries; attempt++)
            {
                try
                {
                    await sendAsync(frame.ToBytes(), cancellationToken).ConfigureAwait(false);

                    while (true)
                    {
                        var response = await ReceiveFrameAsync(receiveFrameAsync, timeoutMs, missionType, cancellationToken).ConfigureAwait(false);
                        if (response.MessageId == MissionCountId && response.Fields != null)
                            return (ushort)response.Fields["count"];
                        IfAbortingAck(response, missionType, out _);
                    }
                }
                catch (MissionTimeoutException)
                {
                    // Retry after timeout.
                }
            }

            throw new TimeoutException($"Mission exchange timed out after {totalTries} attempts.");
        }

        private static async Task<Frame> ReceiveFrameAsync(
            Func<CancellationToken, Task<Frame>> receiveFrameAsync,
            int timeoutMs,
            MavMissionType missionType,
            CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);
            try
            {
                var frame = await receiveFrameAsync(cts.Token).ConfigureAwait(false);
                if (frame == null)
                    throw new MissionTimeoutException($"No response received within {timeoutMs}ms.");
                return frame;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new MissionTimeoutException($"No response received within {timeoutMs}ms.");
            }
        }

        private static async Task<MissionAck> WaitForAckAsync(
            Func<CancellationToken, Task<Frame>> receiveFrameAsync,
            int timeoutMs,
            int maxRetries,
            MavMissionType missionType,
            CancellationToken cancellationToken)
        {
            int totalTries = maxRetries + 1;
            for (int attempt = 0; attempt < totalTries; attempt++)
            {
                try
                {
                    while (true)
                    {
                        var response = await ReceiveFrameAsync(receiveFrameAsync, timeoutMs, missionType, cancellationToken).ConfigureAwait(false);
                        if (TryParseMissionAck(response, out var ack) && ack != null)
                            return ack;
                        IfAbortingAck(response, missionType, out _);
                    }
                }
                catch (MissionTimeoutException)
                {
                    // Retry after timeout.
                }
            }

            throw new TimeoutException($"Mission acknowledgment timed out after {totalTries} attempts.");
        }

        private static async Task<MissionAck> SendAndMatchAckAsync(
            Frame frame,
            int timeoutMs,
            int maxRetries,
            Func<byte[], CancellationToken, Task> sendAsync,
            Func<CancellationToken, Task<Frame>> receiveFrameAsync,
            MavMissionType missionType,
            CancellationToken cancellationToken)
        {
            int totalTries = maxRetries + 1;
            for (int attempt = 0; attempt < totalTries; attempt++)
            {
                try
                {
                    await sendAsync(frame.ToBytes(), cancellationToken).ConfigureAwait(false);

                    while (true)
                    {
                        var response = await ReceiveFrameAsync(receiveFrameAsync, timeoutMs, missionType, cancellationToken).ConfigureAwait(false);
                        if (TryParseMissionAck(response, out var ack) && ack != null && ack.Type == missionType)
                            return ack;
                        IfAbortingAck(response, missionType, out _);
                    }
                }
                catch (MissionTimeoutException)
                {
                    // Retry after timeout.
                }
            }

            throw new TimeoutException($"Mission acknowledgment timed out after {totalTries} attempts.");
        }

        private static Task SendAndMatchAsync(
            Frame frame,
            int timeoutMs,
            int maxRetries,
            Func<byte[], CancellationToken, Task> sendAsync,
            Func<CancellationToken, Task<Frame>> receiveFrameAsync,
            Func<Frame, ushort, bool> match,
            ushort expectedSeq,
            MavMissionType missionType,
            CancellationToken cancellationToken)
        {
            return ReceiveMatchingAsync(sendAsync, receiveFrameAsync, frame, match, expectedSeq, timeoutMs, maxRetries, missionType, cancellationToken);
        }

        private static Task<ushort> SendAndMatchCountAsync(
            Frame frame,
            int timeoutMs,
            int maxRetries,
            Func<byte[], CancellationToken, Task> sendAsync,
            Func<CancellationToken, Task<Frame>> receiveFrameAsync,
            MavMissionType missionType,
            CancellationToken cancellationToken)
        {
            return ReceiveCountAsync(sendAsync, receiveFrameAsync, frame, timeoutMs, maxRetries, missionType, cancellationToken);
        }

        private static Task<Frame> SendAndMatchFrameAsync(
            Frame frame,
            int timeoutMs,
            int maxRetries,
            Func<byte[], CancellationToken, Task> sendAsync,
            Func<CancellationToken, Task<Frame>> receiveFrameAsync,
            Func<Frame, ushort, bool> match,
            ushort expectedSeq,
            MavMissionType missionType,
            CancellationToken cancellationToken)
        {
            return ReceiveMatchingAsync(sendAsync, receiveFrameAsync, frame, match, expectedSeq, timeoutMs, maxRetries, missionType, cancellationToken);
        }
    }
}
