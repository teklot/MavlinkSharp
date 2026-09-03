using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MavLinkSharp.Protocols
{
    /// <summary>
    /// MAVLink onboard parameter type values (MAV_PARAM_TYPE).
    /// </summary>
    public enum MavParamType : byte
    {
        /// <summary>8-bit unsigned integer.</summary>
        UInt8 = 1,
        /// <summary>8-bit signed integer.</summary>
        Int8 = 2,
        /// <summary>16-bit unsigned integer.</summary>
        UInt16 = 3,
        /// <summary>16-bit signed integer.</summary>
        Int16 = 4,
        /// <summary>32-bit unsigned integer.</summary>
        UInt32 = 5,
        /// <summary>32-bit signed integer.</summary>
        Int32 = 6,
        /// <summary>64-bit unsigned integer.</summary>
        UInt64 = 7,
        /// <summary>64-bit signed integer.</summary>
        Int64 = 8,
        /// <summary>32-bit floating point.</summary>
        Real32 = 9,
        /// <summary>64-bit floating point.</summary>
        Real64 = 10
    }

    /// <summary>
    /// Represents a single onboard parameter value emitted in a PARAM_VALUE message.
    /// </summary>
    public class MavParamValue
    {
        /// <summary>Onboard parameter id (max 16 characters).</summary>
        public string ParamId { get; set; } = string.Empty;

        /// <summary>Onboard parameter value stored as a float (the MAVLink wire representation).</summary>
        public float Value { get; set; }

        /// <summary>Onboard parameter type.</summary>
        public MavParamType Type { get; set; }

        /// <summary>Total number of onboard parameters.</summary>
        public ushort ParamCount { get; set; }

        /// <summary>Index of this onboard parameter.</summary>
        public ushort ParamIndex { get; set; }

        /// <summary>
        /// Returns the parameter value converted to <typeparamref name="T"/> using the parameter's type.
        /// Supports integral, floating-point, and <see cref="decimal"/> conversions.
        /// </summary>
        public T Get<T>()
        {
            var raw = Value;
            var target = typeof(T);

            if (target == typeof(float)) return (T)(object)raw;
            if (target == typeof(double)) return (T)(object)(double)raw;
            if (target == typeof(decimal)) return (T)(object)(decimal)raw;

            if (target == typeof(byte)) return (T)(object)(byte)raw;
            if (target == typeof(sbyte)) return (T)(object)(sbyte)raw;
            if (target == typeof(short)) return (T)(object)(short)raw;
            if (target == typeof(ushort)) return (T)(object)(ushort)raw;
            if (target == typeof(int)) return (T)(object)(int)raw;
            if (target == typeof(uint)) return (T)(object)(uint)raw;
            if (target == typeof(long)) return (T)(object)(long)raw;
            if (target == typeof(ulong)) return (T)(object)(ulong)raw;

            throw new InvalidCastException($"Unsupported parameter conversion to type '{typeof(T).FullName}'.");
        }

        /// <inheritdoc />
        public override string ToString() => $"{ParamId} = {Value} ({Type})";
    }

    /// <summary>
    /// An in-memory, thread-safe cache of onboard parameters keyed by parameter id.
    /// Used by <see cref="ParameterProtocol.DownloadParametersAsync"/> and for typed access.
    /// </summary>
    public class ParameterCache
    {
        private readonly ConcurrentDictionary<string, MavParamValue> _values =
            new ConcurrentDictionary<string, MavParamValue>(StringComparer.Ordinal);

        /// <summary>Number of cached parameters.</summary>
        public int Count => _values.Count;

        /// <summary>Enumerates the cached parameter values (in arbitrary order).</summary>
        public IEnumerable<MavParamValue> Values => _values.Values;

        /// <summary>Enumerates the cached parameter ids.</summary>
        public IEnumerable<string> Keys => _values.Keys;

        /// <summary>Gets or updates a cached parameter value.</summary>
        public void Set(MavParamValue value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (string.IsNullOrEmpty(value.ParamId)) throw new ArgumentException("Param id cannot be empty.", nameof(value));

            _values[value.ParamId] = value;
        }

        /// <summary>Attempts to get a cached parameter by id.</summary>
        public bool TryGet(string paramId, out MavParamValue? value) => _values.TryGetValue(paramId, out value);

        /// <summary>Gets a cached parameter by id, or <c>null</c> if absent.</summary>
        public MavParamValue? Get(string paramId)
            => _values.TryGetValue(paramId, out var value) ? value : null;

        /// <summary>Attempts to get a cached parameter, converted to <typeparamref name="T"/>.</summary>
        public bool TryGet<T>(string paramId, out T value)
        {
            if (_values.TryGetValue(paramId, out var param))
            {
                value = param.Get<T>();
                return true;
            }
            value = default!;
            return false;
        }

        /// <summary>Removes a cached parameter. Returns <c>true</c> if it was present.</summary>
        public bool Remove(string paramId) => _values.TryRemove(paramId, out _);

        /// <summary>Clears all cached parameters.</summary>
        public void Clear() => _values.Clear();
    }

    /// <summary>
    /// Result of a parameter download, containing the populated <see cref="ParameterCache"/> and summary counts.
    /// </summary>
    public class ParameterDownload
    {
        /// <summary>The cache populated with the downloaded parameters.</summary>
        public ParameterCache Cache { get; } = new ParameterCache();

        /// <summary>Total number of parameters reported by the target.</summary>
        public int Total { get; set; }

        /// <summary>Whether the download completed with all expected parameters received.</summary>
        public bool Complete { get; set; }
    }

    /// <summary>Reports progress during a parameter transfer.</summary>
    public class ParameterProgress
    {
        /// <summary>Number of parameters processed so far.</summary>
        public int Current { get; set; }

        /// <summary>Total number of parameters.</summary>
        public int Total { get; set; }

        /// <summary>The parameter id most recently processed, if any.</summary>
        public string? ParamId { get; set; }
    }

    /// <summary>Thrown when the parameter protocol cannot complete (timeout, validation, or unexpected frame).</summary>
    public sealed class ParameterExchangeException : Exception
    {
        /// <summary>Creates a new instance.</summary>
        public ParameterExchangeException(string message) : base(message) { }

        /// <summary>Creates a new instance with an inner exception.</summary>
        public ParameterExchangeException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// High-level Parameter Protocol implementation for reading, streaming, and setting onboard
    /// MAVLink parameters (PARAM_REQUEST_READ, PARAM_REQUEST_LIST, PARAM_VALUE, PARAM_SET) with
    /// built-in timeout and retry. Mirrors the design of <see cref="MissionProtocol"/>.
    /// </summary>
    public static class ParameterProtocol
    {
        /// <summary>PARAM_REQUEST_READ message ID.</summary>
        public const uint ParamRequestReadId = 20;
        /// <summary>PARAM_REQUEST_LIST message ID.</summary>
        public const uint ParamRequestListId = 21;
        /// <summary>PARAM_VALUE message ID.</summary>
        public const uint ParamValueId = 22;
        /// <summary>PARAM_SET message ID.</summary>
        public const uint ParamSetId = 23;

        /// <summary>Default timeout in milliseconds for a general parameter-protocol exchange.</summary>
        public const int DefaultTimeoutMs = 1500;

        /// <summary>Default timeout in milliseconds for a per-value exchange.</summary>
        public const int DefaultItemTimeoutMs = 250;

        /// <summary>Default maximum number of retries per exchange.</summary>
        public const int DefaultRetries = 5;

        /// <summary>The wire length of a MAVLink param id field.</summary>
        public const int ParamIdLength = 16;

        /// <summary>
        /// Creates a PARAM_REQUEST_READ frame (id 20) requesting a single parameter by id or index.
        /// </summary>
        public static Frame CreateParamRequestRead(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            string paramId,
            short paramIndex = -1,
            byte sequence = 0)
        {
            var msg = context.Metadata.MessagesDictionary[ParamRequestReadId];
            var frame = NewFrame(context, systemId, componentId, sequence, ParamRequestReadId, msg);
            frame.SetFields(new Dictionary<string, object>
            {
                ["target_system"] = targetSystem,
                ["target_component"] = targetComponent,
                ["param_id"] = ToParamId(paramId),
                ["param_index"] = paramIndex
            });
            return frame;
        }

        /// <summary>
        /// Creates a PARAM_REQUEST_LIST frame (id 21) requesting all parameters of a component.
        /// </summary>
        public static Frame CreateParamRequestList(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            byte sequence = 0)
        {
            var msg = context.Metadata.MessagesDictionary[ParamRequestListId];
            var frame = NewFrame(context, systemId, componentId, sequence, ParamRequestListId, msg);
            frame.SetFields(new Dictionary<string, object>
            {
                ["target_system"] = targetSystem,
                ["target_component"] = targetComponent
            });
            return frame;
        }

        /// <summary>
        /// Creates a PARAM_VALUE frame (id 22) emitting a single onboard parameter value.
        /// </summary>
        public static Frame CreateParamValue(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            MavParamValue value,
            byte sequence = 0)
        {
            var msg = context.Metadata.MessagesDictionary[ParamValueId];
            var frame = NewFrame(context, systemId, componentId, sequence, ParamValueId, msg);
            frame.SetFields(new Dictionary<string, object>
            {
                ["param_id"] = ToParamId(value.ParamId),
                ["param_value"] = value.Value,
                ["param_type"] = (byte)value.Type,
                ["param_count"] = value.ParamCount,
                ["param_index"] = value.ParamIndex
            });
            return frame;
        }

        /// <summary>
        /// Creates a PARAM_SET frame (id 23) setting a single onboard parameter value.
        /// </summary>
        public static Frame CreateParamSet(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            string paramId,
            float value,
            MavParamType type,
            byte sequence = 0)
        {
            var msg = context.Metadata.MessagesDictionary[ParamSetId];
            var frame = NewFrame(context, systemId, componentId, sequence, ParamSetId, msg);
            frame.SetFields(new Dictionary<string, object>
            {
                ["target_system"] = targetSystem,
                ["target_component"] = targetComponent,
                ["param_id"] = ToParamId(paramId),
                ["param_value"] = value,
                ["param_type"] = (byte)type
            });
            return frame;
        }

        /// <summary>
        /// Attempts to parse a PARAM_VALUE frame (id 22) into a <see cref="MavParamValue"/>.
        /// </summary>
        public static bool TryParseParamValue(Frame frame, out MavParamValue? value)
        {
            value = null;
            if (frame.MessageId != ParamValueId || frame.Fields == null)
                return false;

            value = new MavParamValue
            {
                ParamId = FromParamId((char[])frame.Fields["param_id"]),
                Value = (float)frame.Fields["param_value"],
                Type = (MavParamType)(byte)frame.Fields["param_type"],
                ParamCount = (ushort)frame.Fields["param_count"],
                ParamIndex = (ushort)frame.Fields["param_index"]
            };
            return true;
        }

        /// <summary>
        /// Requests and collects all parameters from the target, populating the returned <see cref="ParameterDownload"/>.
        /// Emits PARAM_REQUEST_LIST, then consumes the PARAM_VALUE stream until all reported parameters are received
        /// (or a configurable number of idle timeouts occurs), then verifies completeness.
        /// </summary>
        /// <param name="context">The MAVLink dialect context.</param>
        /// <param name="systemId">Sending system ID.</param>
        /// <param name="componentId">Sending component ID.</param>
        /// <param name="targetSystem">Target system ID.</param>
        /// <param name="targetComponent">Target component ID.</param>
        /// <param name="sendAsync">Callback that sends the serialized message bytes.</param>
        /// <param name="receiveFrameAsync">Callback that returns one parsed frame, or <c>null</c> on timeout.</param>
        /// <param name="timeoutMs">Milliseconds to wait for a general response per attempt.</param>
        /// <param name="itemTimeoutMs">Milliseconds to wait for each value before considering the stream idle.</param>
        /// <param name="maxRetries">Number of retry attempts for the initial request.</param>
        /// <param name="idleTolerance">Number of consecutive idle timeouts allowed before considering the stream complete.</param>
        /// <param name="progress">Optional progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public static async Task<ParameterDownload> DownloadParametersAsync(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            Func<byte[], CancellationToken, Task> sendAsync,
            Func<CancellationToken, Task<Frame>> receiveFrameAsync,
            int timeoutMs = DefaultTimeoutMs,
            int itemTimeoutMs = DefaultItemTimeoutMs,
            int maxRetries = DefaultRetries,
            int idleTolerance = 3,
            IProgress<ParameterProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (sendAsync == null) throw new ArgumentNullException(nameof(sendAsync));
            if (receiveFrameAsync == null) throw new ArgumentNullException(nameof(receiveFrameAsync));

            var requestFrame = CreateParamRequestList(context, systemId, componentId, targetSystem, targetComponent);
            var download = new ParameterDownload();

            // Send the request once (with retries for the request itself).
            await SendWithRetryAsync(requestFrame, timeoutMs, maxRetries, sendAsync, receiveFrameAsync, cancellationToken).ConfigureAwait(false);

            // Consume the PARAM_VALUE stream until we observe idleTolerance consecutive timeouts.
            int idleCount = 0;
            while (idleCount < idleTolerance)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Frame response;
                try
                {
                    response = await ReceiveFrameAsync(receiveFrameAsync, itemTimeoutMs, cancellationToken).ConfigureAwait(false);
                }
                catch (ParameterExchangeException)
                {
                    idleCount++;
                    continue;
                }

                if (TryParseParamValue(response, out var value) && value != null)
                {
                    idleCount = 0;
                    download.Cache.Set(value);
                    download.Total = Math.Max(download.Total, value.ParamCount);
                    progress?.Report(new ParameterProgress { Current = download.Cache.Count, Total = value.ParamCount, ParamId = value.ParamId });
                }
            }

            // If the target reported a total that matches the cache count, consider it complete.
            int cached = download.Cache.Count;
            download.Complete = cached > 0 && cached == download.Total;
            return download;
        }

        /// <summary>
        /// Reads a single parameter by id. Emits PARAM_REQUEST_READ and waits for the matching PARAM_VALUE.
        /// </summary>
        public static async Task<MavParamValue> ReadParameterAsync(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            string paramId,
            Func<byte[], CancellationToken, Task> sendAsync,
            Func<CancellationToken, Task<Frame>> receiveFrameAsync,
            int timeoutMs = DefaultTimeoutMs,
            int maxRetries = DefaultRetries,
            CancellationToken cancellationToken = default)
        {
            if (sendAsync == null) throw new ArgumentNullException(nameof(sendAsync));
            if (receiveFrameAsync == null) throw new ArgumentNullException(nameof(receiveFrameAsync));
            if (string.IsNullOrEmpty(paramId)) throw new ArgumentException("Param id cannot be empty.", nameof(paramId));

            var requestFrame = CreateParamRequestRead(context, systemId, componentId, targetSystem, targetComponent, paramId);
            var frame = await SendAndMatchValueAsync(
                requestFrame, paramId, timeoutMs, maxRetries, sendAsync, receiveFrameAsync, cancellationToken).ConfigureAwait(false);

            if (!TryParseParamValue(frame, out var value) || value == null)
                throw new ParameterExchangeException("Received frame was not a valid PARAM_VALUE.");

            return value;
        }

        /// <summary>
        /// Sets a single parameter to the given value. Emits PARAM_SET and waits for the matching
        /// PARAM_VALUE acknowledgement (matching by param id), with built-in timeout and retry.
        /// </summary>
        public static async Task<MavParamValue> SetParameterAsync(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            string paramId,
            float value,
            MavParamType type,
            Func<byte[], CancellationToken, Task> sendAsync,
            Func<CancellationToken, Task<Frame>> receiveFrameAsync,
            int timeoutMs = DefaultTimeoutMs,
            int maxRetries = DefaultRetries,
            CancellationToken cancellationToken = default)
        {
            if (sendAsync == null) throw new ArgumentNullException(nameof(sendAsync));
            if (receiveFrameAsync == null) throw new ArgumentNullException(nameof(receiveFrameAsync));
            if (string.IsNullOrEmpty(paramId)) throw new ArgumentException("Param id cannot be empty.", nameof(paramId));

            var setFrame = CreateParamSet(context, systemId, componentId, targetSystem, targetComponent, paramId, value, type);
            var ackFrame = await SendAndMatchValueAsync(
                setFrame, paramId, timeoutMs, maxRetries, sendAsync, receiveFrameAsync, cancellationToken).ConfigureAwait(false);

            if (!TryParseParamValue(ackFrame, out var ack) || ack == null)
                throw new ParameterExchangeException("Received frame was not a valid PARAM_VALUE acknowledgement.");

            return ack;
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

        private static char[] ToParamId(string paramId)
        {
            var result = new char[ParamIdLength];
            for (int i = 0; i < ParamIdLength; i++)
                result[i] = i < paramId.Length ? paramId[i] : '\0';
            return result;
        }

        private static string FromParamId(char[] raw)
        {
            int len = 0;
            while (len < raw.Length && raw[len] != '\0') len++;
            return new string(raw, 0, len);
        }

        private static async Task SendWithRetryAsync(
            Frame frame,
            int timeoutMs,
            int maxRetries,
            Func<byte[], CancellationToken, Task> sendAsync,
            Func<CancellationToken, Task<Frame>> receiveFrameAsync,
            CancellationToken cancellationToken)
        {
            int totalTries = maxRetries + 1;
            for (int attempt = 0; attempt < totalTries; attempt++)
            {
                try
                {
                    await sendAsync(frame.ToBytes(), cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (ParameterExchangeException)
                {
                    // Retry the request.
                }
            }

            throw new TimeoutException($"Parameter request failed to send after {totalTries} attempts.");
        }

        private static async Task<Frame> SendAndMatchValueAsync(
            Frame requestFrame,
            string expectedParamId,
            int timeoutMs,
            int maxRetries,
            Func<byte[], CancellationToken, Task> sendAsync,
            Func<CancellationToken, Task<Frame>> receiveFrameAsync,
            CancellationToken cancellationToken)
        {
            int totalTries = maxRetries + 1;
            for (int attempt = 0; attempt < totalTries; attempt++)
            {
                try
                {
                    await sendAsync(requestFrame.ToBytes(), cancellationToken).ConfigureAwait(false);

                    while (true)
                    {
                        var response = await ReceiveFrameAsync(receiveFrameAsync, timeoutMs, cancellationToken).ConfigureAwait(false);
                        if (TryParseParamValue(response, out var value) && value != null && value.ParamId == expectedParamId)
                            return response;
                    }
                }
                catch (ParameterExchangeException)
                {
                    // Retry after timeout.
                }
            }

            throw new TimeoutException($"Parameter exchange for '{expectedParamId}' timed out after {totalTries} attempts.");
        }

        private static async Task<Frame> ReceiveFrameAsync(
            Func<CancellationToken, Task<Frame>> receiveFrameAsync,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);
            try
            {
                var frame = await receiveFrameAsync(cts.Token).ConfigureAwait(false);
                if (frame == null)
                    throw new ParameterExchangeException($"No response received within {timeoutMs}ms.");
                return frame;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ParameterExchangeException($"No response received within {timeoutMs}ms.");
            }
        }
    }
}
