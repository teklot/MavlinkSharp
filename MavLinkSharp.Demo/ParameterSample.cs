using MavLinkSharp;
using MavLinkSharp.Protocols;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MavLinkConsole
{
    /// <summary>
    /// Self-contained, in-memory demonstration of the Parameter Protocol (MavLinkSharp.Protocols.ParameterProtocol).
    /// Simulates both a ground station (GCS) and a flight controller (vehicle) connected over an in-memory link,
    /// then exercises parameter download, read, and set without any network socket.
    /// </summary>
    static class ParameterSample
    {
        public static async Task RunAsync(CancellationToken cancellationToken = default)
        {
            // Fresh simulation state per run so the demo is re-runnable (e.g. from the menu loop).
            var sim = new ParameterSimulation();
            await sim.RunAsync(cancellationToken);
        }

        private sealed class ParameterSimulation
        {
            // Two one-way in-memory links: GCS -> Vehicle and Vehicle -> GCS.
            private readonly Channel<Frame> GcsToVehicle = Channel.CreateUnbounded<Frame>();
            private readonly Channel<Frame> VehicleToGcs = Channel.CreateUnbounded<Frame>();

            // "Parameter storage" on the simulated vehicle.
            private readonly Dictionary<string, MavParamValue> ParamsById = new();

            public async Task RunAsync(CancellationToken cancellationToken)
        {
            TerminalLayout.WriteTx("Param => start (in-memory GCS <-> vehicle demo)");

            // Wire up the simulated vehicle as a background responder.
            var vehicleTask = Task.Run(() => VehicleResponderAsync(cancellationToken), cancellationToken);

            // GCS 1: download all parameters into an in-memory cache.
            TerminalLayout.WriteTx("Param => GCS downloading all parameters...");
            var download = await ParameterProtocol.DownloadParametersAsync(
                MavLinkContext.Default,
                systemId: 1, componentId: 1,
                targetSystem: 2, targetComponent: 1,
                sendAsync: (bytes, ct) => RelayAsync(GcsToVehicle, bytes, ct),
                receiveFrameAsync: ct => ReceiveAsync(VehicleToGcs, ct),
                timeoutMs: 1500, itemTimeoutMs: 250, maxRetries: 5,
                cancellationToken: cancellationToken);
            foreach (var p in download.Cache.Values)
                TerminalLayout.WriteTx($"Param =>   {p.ParamId} = {p.Value} ({p.Type})");

            // GCS 2: read a single parameter by id.
            TerminalLayout.WriteTx("Param => GCS reading THR_MAX...");
            var read = await ParameterProtocol.ReadParameterAsync(
                MavLinkContext.Default,
                systemId: 1, componentId: 1,
                targetSystem: 2, targetComponent: 1,
                paramId: "THR_MAX",
                sendAsync: (bytes, ct) => RelayAsync(GcsToVehicle, bytes, ct),
                receiveFrameAsync: ct => ReceiveAsync(VehicleToGcs, ct),
                cancellationToken: cancellationToken);
            TerminalLayout.WriteTx($"Param =>   read: THR_MAX = {read.Value}");

            // GCS 3: set a parameter value (the vehicle ack's with a PARAM_VALUE).
            TerminalLayout.WriteTx("Param => GCS setting THR_MAX = 850...");
            var set = await ParameterProtocol.SetParameterAsync(
                MavLinkContext.Default,
                systemId: 1, componentId: 1,
                targetSystem: 2, targetComponent: 1,
                paramId: "THR_MAX", value: 850f, type: MavParamType.Real32,
                sendAsync: (bytes, ct) => RelayAsync(GcsToVehicle, bytes, ct),
                receiveFrameAsync: ct => ReceiveAsync(VehicleToGcs, ct),
                cancellationToken: cancellationToken);
            TerminalLayout.WriteTx($"Param =>   set ack: THR_MAX = {set.Value}");

            // Stop the vehicle responder.
            GcsToVehicle.Writer.TryComplete();
            await vehicleTask;

            TerminalLayout.WriteTx("Param => done");
        }

        private void SeedParams()
        {
            ParamsById["THR_MAX"] = new MavParamValue { ParamId = "THR_MAX", Value = 900f, Type = MavParamType.Real32, ParamCount = 3, ParamIndex = 0 };
            ParamsById["RC1_MAX"] = new MavParamValue { ParamId = "RC1_MAX", Value = 1000f, Type = MavParamType.Real32, ParamCount = 3, ParamIndex = 1 };
            ParamsById["BATT_VOLT"] = new MavParamValue { ParamId = "BATT_VOLT", Value = 12.6f, Type = MavParamType.Real32, ParamCount = 3, ParamIndex = 2 };
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

        private async Task VehicleResponderAsync(CancellationToken ct)
        {
            SeedParams();
            try
            {
                while (await GcsToVehicle.Reader.WaitToReadAsync(ct))
                {
                    while (GcsToVehicle.Reader.TryRead(out var frame))
                        await HandleRequestAsync(frame, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Done.
            }
        }

        private async Task HandleRequestAsync(Frame request, CancellationToken ct)
        {
            switch (request.MessageId)
            {
                case ParameterProtocol.ParamRequestListId:
                {
                    // GCS wants all parameters: emit a PARAM_VALUE for each.
                    foreach (var p in ParamsById.Values)
                    {
                        var count = (ushort)ParamsById.Count;
                        var copy = new MavParamValue { ParamId = p.ParamId, Value = p.Value, Type = p.Type, ParamCount = count, ParamIndex = p.ParamIndex };
                        await SendFrameAsync(VehicleToGcs, ParameterProtocol.CreateParamValue(MavLinkContext.Default, 2, 1, copy), ct);
                    }
                    break;
                }

                case ParameterProtocol.ParamRequestReadId:
                {
                    // GCS wants one parameter by id.
                    var id = FromParamId((char[])request.Fields["param_id"]);
                    if (ParamsById.TryGetValue(id, out var p))
                        await SendFrameAsync(VehicleToGcs, ParameterProtocol.CreateParamValue(MavLinkContext.Default, 2, 1, p), ct);
                    break;
                }

                case ParameterProtocol.ParamSetId:
                {
                    // GCS set a parameter: store it and acknowledge with a PARAM_VALUE.
                    var id = FromParamId((char[])request.Fields["param_id"]);
                    var updated = new MavParamValue
                    {
                        ParamId = id,
                        Value = (float)request.Fields["param_value"],
                        Type = (MavParamType)(byte)request.Fields["param_type"],
                        ParamCount = (ushort)(ParamsById.TryGetValue(id, out var old) ? old.ParamCount : 0),
                        ParamIndex = (ushort)(ParamsById.TryGetValue(id, out var old2) ? old2.ParamIndex : 0)
                    };
                    ParamsById[id] = updated;
                    await SendFrameAsync(VehicleToGcs, ParameterProtocol.CreateParamValue(MavLinkContext.Default, 2, 1, updated), ct);
                    break;
                }
            }
        }

        private static string FromParamId(char[] raw)
        {
            int len = 0;
            while (len < raw.Length && raw[len] != '\0') len++;
            return new string(raw, 0, len);
        }

            private static Task SendFrameAsync(Channel<Frame> link, Frame frame, CancellationToken ct)
                => link.Writer.WriteAsync(frame, ct).AsTask();
        }
    }
}
