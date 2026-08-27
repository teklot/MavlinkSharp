using MavLinkSharp;
using MavLinkSharp.Protocols;
using System.Threading.Channels;

namespace MavLinkConsole;

/// <summary>
/// Self-contained, in-memory demonstration of the Mission Protocol (MavLinkSharp.Protocols.MissionProtocol).
/// Simulates both a ground station (GCS) and a flight controller (vehicle) connected over an in-memory link,
/// then exercises mission upload, download, and clear without any network socket.
/// </summary>
static class MissionSample
{
    // Two one-way in-memory links: GCS -> Vehicle and Vehicle -> GCS.
    private static readonly Channel<Frame> GcsToVehicle = Channel.CreateUnbounded<Frame>();
    private static readonly Channel<Frame> VehicleToGcs = Channel.CreateUnbounded<Frame>();

    // "Mission storage" on the simulated vehicle.
    private static readonly List<MissionItem> StoredMission = new();
    private static readonly object VehicleLock = new();
    private static int _expectedMissionCount;

    public static async Task RunAsync(CancellationToken cancellationToken = default)
    {
        TerminalLayout.WriteTx("Mission => start (in-memory GCS <-> vehicle demo)");

        // Wire up the simulated vehicle as a background responder.
        var vehicleTask = Task.Run(() => VehicleResponderAsync(cancellationToken), cancellationToken);

        // GCS 1: upload a small mission.
        var uploadItems = BuildMission();
        TerminalLayout.WriteTx($"Mission => GCS uploading {uploadItems.Count} items...");
        var uploadAck = await MissionProtocol.UploadMissionAsync(
            uploadItems,
            MavLinkContext.Default,
            systemId: 1, componentId: 1,
            targetSystem: 2, targetComponent: 1,
            missionType: MavMissionType.Mission,
            sendAsync: (bytes, ct) => RelayAsync(GcsToVehicle, bytes, ct),
            receiveFrameAsync: ct => ReceiveAsync(VehicleToGcs, ct),
            timeoutMs: 1500,
            itemTimeoutMs: 250,
            maxRetries: 5,
            cancellationToken: cancellationToken);
        TerminalLayout.WriteTx($"Mission => upload: {(uploadAck.Success ? "ACCEPTED" : uploadAck.Result)}");

        // GCS 2: download it back and print the items.
        TerminalLayout.WriteTx("Mission => GCS downloading mission...");
        var download = await MissionProtocol.DownloadMissionAsync(
            MavLinkContext.Default,
            systemId: 1, componentId: 1,
            targetSystem: 2, targetComponent: 1,
            missionType: MavMissionType.Mission,
            sendAsync: (bytes, ct) => RelayAsync(GcsToVehicle, bytes, ct),
            receiveFrameAsync: ct => ReceiveAsync(VehicleToGcs, ct),
            timeoutMs: 1500,
            itemTimeoutMs: 250,
            maxRetries: 5,
            cancellationToken: cancellationToken);
        foreach (var item in download.Items)
            TerminalLayout.WriteTx($"Mission =>   item {item.Seq}: command={item.Command} x={item.X} y={item.Y} z={item.Z}");

        // GCS 3: clear the mission.
        TerminalLayout.WriteTx("Mission => GCS clearing mission...");
        var clearAck = await MissionProtocol.ClearMissionAsync(
            MavLinkContext.Default,
            systemId: 1, componentId: 1,
            targetSystem: 2, targetComponent: 1,
            missionType: MavMissionType.Mission,
            sendAsync: (bytes, ct) => RelayAsync(GcsToVehicle, bytes, ct),
            receiveFrameAsync: ct => ReceiveAsync(VehicleToGcs, ct),
            timeoutMs: 1500,
            maxRetries: 5,
            cancellationToken: cancellationToken);
        TerminalLayout.WriteTx($"Mission => clear: {(clearAck.Success ? "ACCEPTED" : clearAck.Result)}");

        // Stop the vehicle responder.
        GcsToVehicle.Writer.TryComplete();
        await vehicleTask;

        TerminalLayout.WriteTx("Mission => done");
    }

    private static List<MissionItem> BuildMission()
    {
        return new List<MissionItem>
        {
            new MissionItem { Seq = 0, Command = 16 /* MAV_CMD_NAV_WAYPOINT */, Frame = MavFrame.GlobalRelativeAltInt, AutoContinue = 1, X = (int)(47.398m * 1e7m), Y = (int)(8.545m * 1e7m), Z = 50.0f, Type = MavMissionType.Mission },
            new MissionItem { Seq = 1, Command = 16 /* MAV_CMD_NAV_WAYPOINT */, Frame = MavFrame.GlobalRelativeAltInt, AutoContinue = 1, X = (int)(47.399m * 1e7m), Y = (int)(8.546m * 1e7m), Z = 75.0f, Type = MavMissionType.Mission },
            new MissionItem { Seq = 2, Command = 21 /* MAV_CMD_NAV_LAND */, Frame = MavFrame.GlobalRelativeAltInt, AutoContinue = 0, X = (int)(47.400m * 1e7m), Y = (int)(8.550m * 1e7m), Z = 0.0f, Type = MavMissionType.Mission }
        };
    }

    // ---------- In-memory transport helpers ----------

    private static Task RelayAsync(Channel<Frame> link, byte[] bytes, CancellationToken ct)
    {
        var frame = new Frame { Context = MavLinkContext.Default };
        frame.TryParse(bytes);
        return link.Writer.WriteAsync(frame, ct).AsTask();
    }

    private static async Task<Frame> ReceiveAsync(Channel<Frame> link, CancellationToken ct)
    {
        return await link.Reader.ReadAsync(ct).AsTask();
    }

    // ---------- Simulated flight controller ----------

    private static async Task VehicleResponderAsync(CancellationToken ct)
    {
        try
        {
            while (await GcsToVehicle.Reader.WaitToReadAsync(ct))
            {
                while (GcsToVehicle.Reader.TryRead(out var frame))
                    await HandleMissionRequestAsync(frame, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Done.
        }
    }

    private static async Task HandleMissionRequestAsync(Frame request, CancellationToken ct)
    {
        switch (request.MessageId)
        {
            case MissionProtocol.MissionCountId:
            {
                // GCS announced an upload: remember the total and request the first item.
                _expectedMissionCount = (ushort)request.Fields["count"];
                var seq = (ushort)0;
                await SendFrameAsync(VehicleToGcs, MissionProtocol.CreateMissionRequestInt(MavLinkContext.Default, 2, 1, 1, 1, seq), ct);
                break;
            }

            case MissionProtocol.MissionItemIntId:
            {
                MissionProtocol.TryParseMissionItem(request, out var item);
                Frame? requestNext = null;
                bool complete = false;

                lock (VehicleLock)
                {
                    if (item!.Seq >= StoredMission.Count) StoredMission.Add(item);
                    else StoredMission[item.Seq] = item;

                    // If more items remain per the announced count, request the next;
                    // otherwise acknowledge completion.
                    if (item.Seq + 1 < _expectedMissionCount)
                    {
                        requestNext = MissionProtocol.CreateMissionRequestInt(MavLinkContext.Default, 2, 1, 1, 1, (ushort)(item.Seq + 1));
                    }
                    else
                    {
                        complete = true;
                    }
                }

                if (requestNext != null)
                {
                    await SendFrameAsync(VehicleToGcs, requestNext, ct);
                }
                else if (complete)
                {
                    int count;
                    lock (VehicleLock) count = StoredMission.Count;
                    await SendFrameAsync(VehicleToGcs, MissionProtocol.CreateMissionCount(MavLinkContext.Default, 2, 1, 1, 1, (ushort)count), ct);
                    await SendFrameAsync(VehicleToGcs, BuildAck(MavMissionResult.Accepted), ct);
                }
                break;
            }

            case MissionProtocol.MissionRequestListId:
            {
                // GCS wants to download: answer with the stored item count.
                int count;
                lock (VehicleLock) count = StoredMission.Count;
                await SendFrameAsync(VehicleToGcs, MissionProtocol.CreateMissionCount(MavLinkContext.Default, 2, 1, 1, 1, (ushort)count), ct);
                break;
            }

            case MissionProtocol.MissionRequestIntId:
            {
                // GCS wants a specific item: send it.
                var seq = (ushort)request.Fields["seq"];
                MissionItem? item;
                lock (VehicleLock) item = seq < StoredMission.Count ? StoredMission[seq] : null;
                if (item != null)
                    await SendFrameAsync(VehicleToGcs, MissionProtocol.CreateMissionItemInt(MavLinkContext.Default, 2, 1, 1, 1, item), ct);
                break;
            }

            case MissionProtocol.MissionClearAllId:
            {
                lock (VehicleLock) StoredMission.Clear();
                await SendFrameAsync(VehicleToGcs, BuildAck(MavMissionResult.Accepted), ct);
                break;
            }
        }
    }

    private static Frame BuildAck(MavMissionResult result)
    {
        var msg = MavLinkContext.Default.Metadata.MessagesDictionary[MissionProtocol.MissionAckId];
        var frame = new Frame
        {
            Context = MavLinkContext.Default,
            StartMarker = Protocol.V2.StartMarker,
            SystemId = 2,
            ComponentId = 1,
            MessageId = MissionProtocol.MissionAckId,
            Message = msg
        };
        frame.SetFields(new Dictionary<string, object>
        {
            ["target_system"] = (byte)1,
            ["target_component"] = (byte)1,
            ["type"] = (byte)result,
            ["mission_type"] = (byte)MavMissionType.Mission
        });
        return frame;
    }

    private static Task SendFrameAsync(Channel<Frame> link, Frame frame, CancellationToken ct)
        => link.Writer.WriteAsync(frame, ct).AsTask();
}
