using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
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
    /// MAVLink extended parameter type values (MAV_PARAM_EXT_TYPE). Unlike classic parameters,
    /// extended parameters carry their value as raw bytes and support 64-bit types and strings.
    /// </summary>
    public enum MavParamExtType : byte
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
        Real64 = 10,
        /// <summary>Null-terminated string (up to 128 bytes).</summary>
        Custom = 11
    }

    /// <summary>
    /// Parameter acknowledgement results (PARAM_ACK), returned in PARAM_EXT_ACK and
    /// PARAM_ACK_TRANSACTION messages.
    /// </summary>
    public enum ParamExtAckResult : byte
    {
        /// <summary>Parameter value accepted and set.</summary>
        Accepted = 0,
        /// <summary>Parameter value unknown or unsupported.</summary>
        ValueUnsupported = 1,
        /// <summary>Parameter failed to set.</summary>
        Failed = 2,
        /// <summary>Parameter value received but not yet set; a final result will follow.</summary>
        InProgress = 3
    }

    /// <summary>
    /// Parameter transaction actions (PARAM_TRANSACTION_ACTION), used with MAV_CMD_PARAM_TRANSACTION.
    /// </summary>
    public enum ParamTransactionAction : byte
    {
        /// <summary>Start a parameter transaction.</summary>
        Start = 0,
        /// <summary>Commit the current parameter transaction.</summary>
        Commit = 1,
        /// <summary>Cancel the current parameter transaction.</summary>
        Cancel = 2
    }

    /// <summary>
    /// Parameter transaction transports (PARAM_TRANSACTION_TRANSPORT), used with MAV_CMD_PARAM_TRANSACTION.
    /// </summary>
    public enum ParamTransactionTransport : byte
    {
        /// <summary>Transaction over the classic param transport (PARAM_SET / PARAM_VALUE).</summary>
        Param = 0,
        /// <summary>Transaction over the extended param transport (PARAM_EXT_SET / PARAM_EXT_VALUE).</summary>
        ParamExt = 1
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
    /// Represents a single extended parameter value emitted in a PARAM_EXT_VALUE message.
    /// The value travels as raw bytes (little-endian for numeric types, null-terminated string
    /// for <see cref="MavParamExtType.Custom"/>).
    /// </summary>
    public class MavParamExtValue
    {
        /// <summary>Onboard parameter id (max 16 characters).</summary>
        public string ParamId { get; set; } = string.Empty;

        /// <summary>Raw parameter value bytes as they appear on the wire (numeric values are little-endian).</summary>
        public byte[] RawValue { get; set; } = new byte[ParameterProtocol.ExtValueLength];

        /// <summary>Extended parameter type.</summary>
        public MavParamExtType Type { get; set; }

        /// <summary>Total number of extended parameters.</summary>
        public ushort ParamCount { get; set; }

        /// <summary>Index of this extended parameter.</summary>
        public ushort ParamIndex { get; set; }

        /// <summary>
        /// Returns the parameter value converted to <typeparamref name="T"/>. Numeric types are decoded
        /// from the raw little-endian bytes according to <see cref="Type"/>; <see cref="string"/> is only
        /// supported for <see cref="MavParamExtType.Custom"/> parameters.
        /// </summary>
        public T Get<T>()
        {
            var target = typeof(T);

            if (target == typeof(string))
            {
                if (Type != MavParamExtType.Custom)
                    throw new InvalidCastException($"Parameter '{ParamId}' has type {Type}; only Custom parameters can be read as string.");
                return (T)(object)DecodeCustomString();
            }

            if (Type == MavParamExtType.Custom)
                throw new InvalidCastException($"Parameter '{ParamId}' has type Custom; numeric values are not available.");

            var raw = DecodeBoxed();
            if (raw.GetType() == target)
                return (T)raw;

            if (target == typeof(float)) return (T)(object)Convert.ToSingle(raw);
            if (target == typeof(double)) return (T)(object)Convert.ToDouble(raw);
            if (target == typeof(decimal)) return (T)(object)Convert.ToDecimal(raw);
            if (target == typeof(byte)) return (T)(object)Convert.ToByte(raw);
            if (target == typeof(sbyte)) return (T)(object)Convert.ToSByte(raw);
            if (target == typeof(short)) return (T)(object)Convert.ToInt16(raw);
            if (target == typeof(ushort)) return (T)(object)Convert.ToUInt16(raw);
            if (target == typeof(int)) return (T)(object)Convert.ToInt32(raw);
            if (target == typeof(uint)) return (T)(object)Convert.ToUInt32(raw);
            if (target == typeof(long)) return (T)(object)Convert.ToInt64(raw);
            if (target == typeof(ulong)) return (T)(object)Convert.ToUInt64(raw);

            throw new InvalidCastException($"Unsupported parameter conversion to type '{target.FullName}'.");
        }

        /// <summary>
        /// Creates a new extended parameter value, encoding <paramref name="value"/> according to the
        /// given type. Numeric values are encoded little-endian; string values require
        /// <see cref="MavParamExtType.Custom"/> and are truncated to 128 bytes.
        /// </summary>
        public static MavParamExtValue Create<T>(string paramId, T value, MavParamExtType type)
        {
            if (string.IsNullOrEmpty(paramId)) throw new ArgumentException("Param id cannot be empty.", nameof(paramId));
            if (value == null) throw new ArgumentNullException(nameof(value));

            var result = new MavParamExtValue { ParamId = paramId, Type = type };

            if (value is string text)
            {
                if (type != MavParamExtType.Custom)
                    throw new ArgumentException("String values require MavParamExtType.Custom.", nameof(type));
                var bytes = Encoding.ASCII.GetBytes(text);
                Array.Copy(bytes, result.RawValue, Math.Min(bytes.Length, result.RawValue.Length));
            }
            else
            {
                result.EncodeNumeric(value!);
            }

            return result;
        }

        /// <summary>
        /// Creates a new extended parameter value, inferring the type from the runtime type of
        /// <paramref name="value"/> (strings map to <see cref="MavParamExtType.Custom"/>,
        /// <see cref="decimal"/> maps to <see cref="MavParamExtType.Real64"/>).
        /// </summary>
        public static MavParamExtValue Create<T>(string paramId, T value)
        {
            if (value is string)
                return Create(paramId, value, MavParamExtType.Custom);
            return Create(paramId, value, InferType(value));
        }

        private static MavParamExtType InferType<T>(T value)
        {
            var t = value?.GetType() ?? typeof(T);
            if (t == typeof(string)) return MavParamExtType.Custom;
            if (t == typeof(byte)) return MavParamExtType.UInt8;
            if (t == typeof(sbyte)) return MavParamExtType.Int8;
            if (t == typeof(short)) return MavParamExtType.Int16;
            if (t == typeof(ushort)) return MavParamExtType.UInt16;
            if (t == typeof(int)) return MavParamExtType.Int32;
            if (t == typeof(uint)) return MavParamExtType.UInt32;
            if (t == typeof(long)) return MavParamExtType.Int64;
            if (t == typeof(ulong)) return MavParamExtType.UInt64;
            if (t == typeof(float)) return MavParamExtType.Real32;
            if (t == typeof(double)) return MavParamExtType.Real64;
            if (t == typeof(decimal)) return MavParamExtType.Real64;
            throw new NotSupportedException($"Cannot infer MavParamExtType from '{t.FullName}'.");
        }

        private object DecodeBoxed()
        {
            switch (Type)
            {
                case MavParamExtType.UInt8: return RawValue[0];
                case MavParamExtType.Int8: return (sbyte)RawValue[0];
                case MavParamExtType.UInt16: return ReadUInt16();
                case MavParamExtType.Int16: return (short)ReadUInt16();
                case MavParamExtType.UInt32: return ReadUInt32();
                case MavParamExtType.Int32: return (int)ReadUInt32();
                case MavParamExtType.UInt64: return ReadUInt64();
                case MavParamExtType.Int64: return (long)ReadUInt64();
                case MavParamExtType.Real32: return BitHelpers.Int32BitsToSingle((int)ReadUInt32());
                case MavParamExtType.Real64: return BitHelpers.Int64BitsToDouble((long)ReadUInt64());
                default:
                    throw new InvalidCastException($"Unknown extended parameter type {Type}.");
            }
        }

        private string DecodeCustomString()
        {
            int len = Array.IndexOf(RawValue, (byte)0);
            if (len < 0) len = RawValue.Length;
            return Encoding.ASCII.GetString(RawValue, 0, len);
        }

        private void EncodeNumeric(object value)
        {
            switch (Type)
            {
                case MavParamExtType.UInt8: RawValue[0] = Convert.ToByte(value); break;
                case MavParamExtType.Int8: RawValue[0] = unchecked((byte)Convert.ToSByte(value)); break;
                case MavParamExtType.UInt16: WriteUInt16(Convert.ToUInt16(value)); break;
                case MavParamExtType.Int16: WriteUInt16(unchecked((ushort)Convert.ToInt16(value))); break;
                case MavParamExtType.UInt32: WriteUInt32(Convert.ToUInt32(value)); break;
                case MavParamExtType.Int32: WriteUInt32(unchecked((uint)Convert.ToInt32(value))); break;
                case MavParamExtType.UInt64: WriteUInt64(Convert.ToUInt64(value)); break;
                case MavParamExtType.Int64: WriteUInt64(unchecked((ulong)Convert.ToInt64(value))); break;
                case MavParamExtType.Real32: WriteUInt32(unchecked((uint)BitHelpers.SingleToInt32Bits(Convert.ToSingle(value)))); break;
                case MavParamExtType.Real64: WriteUInt64(unchecked((ulong)BitHelpers.DoubleToInt64Bits(Convert.ToDouble(value)))); break;
                default:
                    throw new ArgumentException($"Value of type '{value.GetType().FullName}' cannot be encoded as {Type}.", nameof(value));
            }
        }

        private ushort ReadUInt16() => (ushort)(RawValue[0] | (RawValue[1] << 8));

        private uint ReadUInt32() => (uint)(RawValue[0] | (RawValue[1] << 8) | (RawValue[2] << 16) | (RawValue[3] << 24));

        private ulong ReadUInt64()
        {
            ulong value = 0;
            for (int i = 7; i >= 0; i--)
                value = (value << 8) | RawValue[i];
            return value;
        }

        private void WriteUInt16(ushort value)
        {
            RawValue[0] = (byte)value;
            RawValue[1] = (byte)(value >> 8);
        }

        private void WriteUInt32(uint value)
        {
            RawValue[0] = (byte)value;
            RawValue[1] = (byte)(value >> 8);
            RawValue[2] = (byte)(value >> 16);
            RawValue[3] = (byte)(value >> 24);
        }

        private void WriteUInt64(ulong value)
        {
            for (int i = 0; i < 8; i++)
                RawValue[i] = (byte)(value >> (8 * i));
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var display = Type == MavParamExtType.Custom ? Get<string>() : DecodeBoxed().ToString();
            return $"{ParamId} = {display} ({Type})";
        }
    }

    /// <summary>
    /// Acknowledgement of a PARAM_EXT_SET message (PARAM_EXT_ACK), echoing the parameter value
    /// (the new value if accepted, the current value otherwise) and the result of the operation.
    /// </summary>
    public class ParamExtAck : MavParamExtValue
    {
        /// <summary>Result of the set operation.</summary>
        public ParamExtAckResult Result { get; set; }

        /// <inheritdoc />
        public override string ToString() => $"{base.ToString()} [{Result}]";
    }

    /// <summary>
    /// Response to a PARAM_SET sent within a parameter transaction (PARAM_ACK_TRANSACTION),
    /// echoing the parameter value (the new value if accepted, the current value otherwise)
    /// and the result of the operation.
    /// </summary>
    public class ParamAckTransaction : MavParamValue
    {
        /// <summary>Result of the set operation.</summary>
        public ParamExtAckResult Result { get; set; }

        /// <inheritdoc />
        public override string ToString() => $"{base.ToString()} [{Result}]";
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
    /// An in-memory, thread-safe cache of extended parameters keyed by parameter id.
    /// Used by <see cref="ParameterProtocol.DownloadExtParametersAsync"/> and for typed access.
    /// </summary>
    public class ParameterExtCache
    {
        private readonly ConcurrentDictionary<string, MavParamExtValue> _values =
            new ConcurrentDictionary<string, MavParamExtValue>(StringComparer.Ordinal);

        /// <summary>Number of cached extended parameters.</summary>
        public int Count => _values.Count;

        /// <summary>Enumerates the cached extended parameter values (in arbitrary order).</summary>
        public IEnumerable<MavParamExtValue> Values => _values.Values;

        /// <summary>Enumerates the cached extended parameter ids.</summary>
        public IEnumerable<string> Keys => _values.Keys;

        /// <summary>Gets or updates a cached extended parameter value.</summary>
        public void Set(MavParamExtValue value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (string.IsNullOrEmpty(value.ParamId)) throw new ArgumentException("Param id cannot be empty.", nameof(value));

            _values[value.ParamId] = value;
        }

        /// <summary>Attempts to get a cached extended parameter by id.</summary>
        public bool TryGet(string paramId, out MavParamExtValue? value) => _values.TryGetValue(paramId, out value);

        /// <summary>Gets a cached extended parameter by id, or <c>null</c> if absent.</summary>
        public MavParamExtValue? Get(string paramId)
            => _values.TryGetValue(paramId, out var value) ? value : null;

        /// <summary>Attempts to get a cached extended parameter, converted to <typeparamref name="T"/>.</summary>
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

        /// <summary>Removes a cached extended parameter. Returns <c>true</c> if it was present.</summary>
        public bool Remove(string paramId) => _values.TryRemove(paramId, out _);

        /// <summary>Clears all cached extended parameters.</summary>
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

    /// <summary>
    /// Result of an extended parameter download, containing the populated <see cref="ParameterExtCache"/>
    /// and summary counts.
    /// </summary>
    public class ExtParameterDownload
    {
        /// <summary>The cache populated with the downloaded extended parameters.</summary>
        public ParameterExtCache Cache { get; } = new ParameterExtCache();

        /// <summary>Total number of extended parameters reported by the target.</summary>
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
    /// Also implements the extended parameter protocol (PARAM_EXT_*), which carries values as raw
    /// bytes and supports 64-bit types and strings, and parameter transactions
    /// (MAV_CMD_PARAM_TRANSACTION) for atomic multi-step parameter writes.
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

        /// <summary>PARAM_EXT_REQUEST_READ message ID.</summary>
        public const uint ParamExtRequestReadId = 320;
        /// <summary>PARAM_EXT_REQUEST_LIST message ID.</summary>
        public const uint ParamExtRequestListId = 321;
        /// <summary>PARAM_EXT_VALUE message ID.</summary>
        public const uint ParamExtValueId = 322;
        /// <summary>PARAM_EXT_SET message ID.</summary>
        public const uint ParamExtSetId = 323;
        /// <summary>PARAM_EXT_ACK message ID.</summary>
        public const uint ParamExtAckId = 324;
        /// <summary>PARAM_ACK_TRANSACTION message ID (only defined in the Development dialect).</summary>
        public const uint ParamAckTransactionId = 19;
        /// <summary>MAV_CMD_PARAM_TRANSACTION command value.</summary>
        public const ushort MavCmdParamTransaction = 900;

        /// <summary>Default timeout in milliseconds for a general parameter-protocol exchange.</summary>
        public const int DefaultTimeoutMs = 1500;

        /// <summary>Default timeout in milliseconds for a per-value exchange.</summary>
        public const int DefaultItemTimeoutMs = 250;

        /// <summary>Default maximum number of retries per exchange.</summary>
        public const int DefaultRetries = 5;

        /// <summary>The wire length of a MAVLink param id field.</summary>
        public const int ParamIdLength = 16;

        /// <summary>The wire length in bytes of a MAVLink extended parameter value field.</summary>
        public const int ExtValueLength = 128;

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
        /// Creates a PARAM_EXT_REQUEST_READ frame (id 320) requesting a single extended parameter
        /// by id or index. PARAM_EXT_VALUE should be emitted in response.
        /// </summary>
        public static Frame CreateParamExtRequestRead(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            string paramId,
            short paramIndex = -1,
            byte sequence = 0)
        {
            var msg = context.Metadata.MessagesDictionary[ParamExtRequestReadId];
            var frame = NewFrame(context, systemId, componentId, sequence, ParamExtRequestReadId, msg);
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
        /// Creates a PARAM_EXT_REQUEST_LIST frame (id 321) requesting all extended parameters of a component.
        /// </summary>
        public static Frame CreateParamExtRequestList(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            byte sequence = 0)
        {
            var msg = context.Metadata.MessagesDictionary[ParamExtRequestListId];
            var frame = NewFrame(context, systemId, componentId, sequence, ParamExtRequestListId, msg);
            frame.SetFields(new Dictionary<string, object>
            {
                ["target_system"] = targetSystem,
                ["target_component"] = targetComponent
            });
            return frame;
        }

        /// <summary>
        /// Creates a PARAM_EXT_VALUE frame (id 322) emitting a single extended parameter value.
        /// </summary>
        public static Frame CreateParamExtValue(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            MavParamExtValue value,
            byte sequence = 0)
        {
            var msg = context.Metadata.MessagesDictionary[ParamExtValueId];
            var frame = NewFrame(context, systemId, componentId, sequence, ParamExtValueId, msg);
            frame.SetFields(new Dictionary<string, object>
            {
                ["param_id"] = ToParamId(value.ParamId),
                ["param_value"] = ToWireValue(value.RawValue),
                ["param_type"] = (byte)value.Type,
                ["param_count"] = value.ParamCount,
                ["param_index"] = value.ParamIndex
            });
            return frame;
        }

        /// <summary>
        /// Creates a PARAM_EXT_SET frame (id 323) setting a single extended parameter value.
        /// </summary>
        public static Frame CreateParamExtSet(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            MavParamExtValue value,
            byte sequence = 0)
        {
            var msg = context.Metadata.MessagesDictionary[ParamExtSetId];
            var frame = NewFrame(context, systemId, componentId, sequence, ParamExtSetId, msg);
            frame.SetFields(new Dictionary<string, object>
            {
                ["target_system"] = targetSystem,
                ["target_component"] = targetComponent,
                ["param_id"] = ToParamId(value.ParamId),
                ["param_value"] = ToWireValue(value.RawValue),
                ["param_type"] = (byte)value.Type
            });
            return frame;
        }

        /// <summary>
        /// Creates a PARAM_EXT_ACK frame (id 324) acknowledging a PARAM_EXT_SET, echoing the value
        /// (the new value if accepted, the current value otherwise) and the result.
        /// </summary>
        public static Frame CreateParamExtAck(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            MavParamExtValue value,
            ParamExtAckResult result,
            byte sequence = 0)
        {
            var msg = context.Metadata.MessagesDictionary[ParamExtAckId];
            var frame = NewFrame(context, systemId, componentId, sequence, ParamExtAckId, msg);
            frame.SetFields(new Dictionary<string, object>
            {
                ["param_id"] = ToParamId(value.ParamId),
                ["param_value"] = ToWireValue(value.RawValue),
                ["param_type"] = (byte)value.Type,
                ["param_result"] = (byte)result
            });
            return frame;
        }

        /// <summary>
        /// Creates a PARAM_ACK_TRANSACTION frame (id 19) acknowledging a PARAM_SET sent within a
        /// parameter transaction. Requires a dialect that defines the message (e.g.
        /// <see cref="Enums.DialectType.Development"/>); <paramref name="targetSystem"/> and
        /// <paramref name="targetComponent"/> identify the original sender of the PARAM_SET.
        /// </summary>
        public static Frame CreateParamAckTransaction(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            MavParamValue value,
            ParamExtAckResult result,
            byte sequence = 0)
        {
            if (!context.Metadata.MessagesDictionary.TryGetValue(ParamAckTransactionId, out var msg) || msg == null)
                throw new ParameterExchangeException(
                    "PARAM_ACK_TRANSACTION (message id 19) is not defined in the current dialect; " +
                    "classic parameter transactions require a dialect that includes it, e.g. DialectType.Development.");

            var frame = NewFrame(context, systemId, componentId, sequence, ParamAckTransactionId, msg);
            frame.SetFields(new Dictionary<string, object>
            {
                ["target_system"] = targetSystem,
                ["target_component"] = targetComponent,
                ["param_id"] = ToParamId(value.ParamId),
                ["param_value"] = value.Value,
                ["param_type"] = (byte)value.Type,
                ["param_result"] = (byte)result
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
        /// Attempts to parse a PARAM_EXT_VALUE frame (id 322) into a <see cref="MavParamExtValue"/>.
        /// </summary>
        public static bool TryParseParamExtValue(Frame frame, out MavParamExtValue? value)
        {
            value = null;
            if (frame.MessageId != ParamExtValueId || frame.Fields == null)
                return false;

            value = new MavParamExtValue
            {
                ParamId = FromParamId((char[])frame.Fields["param_id"]),
                RawValue = FromWireValue((char[])frame.Fields["param_value"]),
                Type = (MavParamExtType)(byte)frame.Fields["param_type"],
                ParamCount = (ushort)frame.Fields["param_count"],
                ParamIndex = (ushort)frame.Fields["param_index"]
            };
            return true;
        }

        /// <summary>
        /// Attempts to parse a PARAM_EXT_ACK frame (id 324) into a <see cref="ParamExtAck"/>.
        /// </summary>
        public static bool TryParseParamExtAck(Frame frame, out ParamExtAck? ack)
        {
            ack = null;
            if (frame.MessageId != ParamExtAckId || frame.Fields == null)
                return false;

            ack = new ParamExtAck
            {
                ParamId = FromParamId((char[])frame.Fields["param_id"]),
                RawValue = FromWireValue((char[])frame.Fields["param_value"]),
                Type = (MavParamExtType)(byte)frame.Fields["param_type"],
                Result = (ParamExtAckResult)(byte)frame.Fields["param_result"]
            };
            return true;
        }

        /// <summary>
        /// Attempts to parse a PARAM_ACK_TRANSACTION frame (id 19) into a <see cref="ParamAckTransaction"/>.
        /// Returns <c>false</c> unless the frame is a PARAM_ACK_TRANSACTION (only parsed by dialects
        /// that define it, e.g. <see cref="Enums.DialectType.Development"/>).
        /// </summary>
        public static bool TryParseParamAckTransaction(Frame frame, out ParamAckTransaction? ack)
        {
            ack = null;
            if (frame.MessageId != ParamAckTransactionId || frame.Fields == null)
                return false;

            ack = new ParamAckTransaction
            {
                ParamId = FromParamId((char[])frame.Fields["param_id"]),
                Value = (float)frame.Fields["param_value"],
                Type = (MavParamType)(byte)frame.Fields["param_type"],
                Result = (ParamExtAckResult)(byte)frame.Fields["param_result"]
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

        /// <summary>
        /// Requests and collects all extended parameters from the target, populating the returned
        /// <see cref="ExtParameterDownload"/>. Emits PARAM_EXT_REQUEST_LIST, then consumes the
        /// PARAM_EXT_VALUE stream until all reported parameters are received (or a configurable
        /// number of idle timeouts occurs), then verifies completeness.
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
        public static async Task<ExtParameterDownload> DownloadExtParametersAsync(
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

            var requestFrame = CreateParamExtRequestList(context, systemId, componentId, targetSystem, targetComponent);
            var download = new ExtParameterDownload();

            // Send the request once (with retries for the request itself).
            await SendWithRetryAsync(requestFrame, timeoutMs, maxRetries, sendAsync, receiveFrameAsync, cancellationToken).ConfigureAwait(false);

            // Consume the PARAM_EXT_VALUE stream until we observe idleTolerance consecutive timeouts.
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

                if (TryParseParamExtValue(response, out var value) && value != null)
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
        /// Reads a single extended parameter by id. Emits PARAM_EXT_REQUEST_READ and waits for the
        /// matching PARAM_EXT_VALUE.
        /// </summary>
        public static async Task<MavParamExtValue> ReadExtParameterAsync(
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

            var requestFrame = CreateParamExtRequestRead(context, systemId, componentId, targetSystem, targetComponent, paramId);
            var frame = await SendAndMatchExtValueAsync(
                requestFrame, paramId, timeoutMs, maxRetries, sendAsync, receiveFrameAsync, cancellationToken).ConfigureAwait(false);

            if (!TryParseParamExtValue(frame, out var value) || value == null)
                throw new ParameterExchangeException("Received frame was not a valid PARAM_EXT_VALUE.");

            return value;
        }

        /// <summary>
        /// Sets a single extended parameter to the given value. Emits PARAM_EXT_SET and waits for the
        /// matching PARAM_EXT_ACK, keeping the exchange open while the target reports
        /// <see cref="ParamExtAckResult.InProgress"/>. Throws <see cref="ParameterExchangeException"/>
        /// if the final result is not <see cref="ParamExtAckResult.Accepted"/>.
        /// </summary>
        public static async Task<ParamExtAck> SetExtParameterAsync(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            MavParamExtValue value,
            Func<byte[], CancellationToken, Task> sendAsync,
            Func<CancellationToken, Task<Frame>> receiveFrameAsync,
            int timeoutMs = DefaultTimeoutMs,
            int maxRetries = DefaultRetries,
            CancellationToken cancellationToken = default)
        {
            if (sendAsync == null) throw new ArgumentNullException(nameof(sendAsync));
            if (receiveFrameAsync == null) throw new ArgumentNullException(nameof(receiveFrameAsync));
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (string.IsNullOrEmpty(value.ParamId)) throw new ArgumentException("Param id cannot be empty.", nameof(value));

            var setFrame = CreateParamExtSet(context, systemId, componentId, targetSystem, targetComponent, value);
            var ack = await SendAndMatchExtAckAsync(
                setFrame, value.ParamId, timeoutMs, maxRetries, sendAsync, receiveFrameAsync, cancellationToken).ConfigureAwait(false);

            if (ack.Result != ParamExtAckResult.Accepted)
                throw new ParameterExchangeException($"Extended parameter set for '{value.ParamId}' failed: {ack.Result}.");

            return ack;
        }

        /// <summary>
        /// Begins a parameter transaction by sending MAV_CMD_PARAM_TRANSACTION with the Start action.
        /// Returns the COMMAND_ACK result; check <see cref="CommandResult.Success"/>.
        /// </summary>
        /// <param name="context">The MAVLink dialect context.</param>
        /// <param name="systemId">Sending system ID.</param>
        /// <param name="componentId">Sending component ID.</param>
        /// <param name="targetSystem">Target system ID.</param>
        /// <param name="targetComponent">Target component ID.</param>
        /// <param name="transport">The transport the transaction will run over (classic or extended parameters).</param>
        /// <param name="sendAsync">Callback that sends the serialized command bytes.</param>
        /// <param name="receiveFrameAsync">Callback that returns one parsed frame, or <c>null</c> on timeout.</param>
        /// <param name="timeoutMs">Milliseconds to wait for the COMMAND_ACK per attempt.</param>
        /// <param name="maxRetries">Number of retry attempts on timeout.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public static Task<CommandResult> BeginTransactionAsync(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            ParamTransactionTransport transport,
            Func<byte[], CancellationToken, Task> sendAsync,
            Func<CancellationToken, Task<Frame>> receiveFrameAsync,
            int timeoutMs = DefaultTimeoutMs,
            int maxRetries = DefaultRetries,
            CancellationToken cancellationToken = default)
            => SendTransactionCommandAsync(context, systemId, componentId, targetSystem, targetComponent,
                ParamTransactionAction.Start, transport, sendAsync, receiveFrameAsync, timeoutMs, maxRetries, cancellationToken);

        /// <summary>
        /// Commits the current parameter transaction by sending MAV_CMD_PARAM_TRANSACTION with the
        /// Commit action. Returns the COMMAND_ACK result; check <see cref="CommandResult.Success"/>.
        /// </summary>
        public static Task<CommandResult> CommitTransactionAsync(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            ParamTransactionTransport transport,
            Func<byte[], CancellationToken, Task> sendAsync,
            Func<CancellationToken, Task<Frame>> receiveFrameAsync,
            int timeoutMs = DefaultTimeoutMs,
            int maxRetries = DefaultRetries,
            CancellationToken cancellationToken = default)
            => SendTransactionCommandAsync(context, systemId, componentId, targetSystem, targetComponent,
                ParamTransactionAction.Commit, transport, sendAsync, receiveFrameAsync, timeoutMs, maxRetries, cancellationToken);

        /// <summary>
        /// Cancels the current parameter transaction by sending MAV_CMD_PARAM_TRANSACTION with the
        /// Cancel action. Returns the COMMAND_ACK result; check <see cref="CommandResult.Success"/>.
        /// </summary>
        public static Task<CommandResult> CancelTransactionAsync(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            ParamTransactionTransport transport,
            Func<byte[], CancellationToken, Task> sendAsync,
            Func<CancellationToken, Task<Frame>> receiveFrameAsync,
            int timeoutMs = DefaultTimeoutMs,
            int maxRetries = DefaultRetries,
            CancellationToken cancellationToken = default)
            => SendTransactionCommandAsync(context, systemId, componentId, targetSystem, targetComponent,
                ParamTransactionAction.Cancel, transport, sendAsync, receiveFrameAsync, timeoutMs, maxRetries, cancellationToken);

        /// <summary>
        /// Sets a single classic parameter inside a parameter transaction, making the write atomic:
        /// sends MAV_CMD_PARAM_TRANSACTION (Start), then PARAM_SET, waits for the final
        /// PARAM_ACK_TRANSACTION (following any <see cref="ParamExtAckResult.InProgress"/> responses),
        /// then sends MAV_CMD_PARAM_TRANSACTION (Commit). The transaction is not cancelled
        /// automatically if a step fails; use <see cref="CancelTransactionAsync"/> if needed.
        /// Requires a dialect that defines PARAM_ACK_TRANSACTION (e.g. <see cref="Enums.DialectType.Development"/>).
        /// </summary>
        public static async Task<ParamAckTransaction> SetParameterTransactionalAsync(
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

            EnsureParamAckTransactionSupported(context);

            var begin = await BeginTransactionAsync(
                context, systemId, componentId, targetSystem, targetComponent, ParamTransactionTransport.Param,
                sendAsync, receiveFrameAsync, timeoutMs, maxRetries, cancellationToken).ConfigureAwait(false);
            if (!begin.Success)
                throw new ParameterExchangeException($"Failed to begin parameter transaction: {begin.Result}.");

            var setFrame = CreateParamSet(context, systemId, componentId, targetSystem, targetComponent, paramId, value, type);
            var ack = await SendAndMatchAckTransactionAsync(
                setFrame, paramId, timeoutMs, maxRetries, sendAsync, receiveFrameAsync, cancellationToken).ConfigureAwait(false);

            if (ack.Result != ParamExtAckResult.Accepted)
                throw new ParameterExchangeException($"Parameter set for '{paramId}' failed: {ack.Result}.");

            var commit = await CommitTransactionAsync(
                context, systemId, componentId, targetSystem, targetComponent, ParamTransactionTransport.Param,
                sendAsync, receiveFrameAsync, timeoutMs, maxRetries, cancellationToken).ConfigureAwait(false);
            if (!commit.Success)
                throw new ParameterExchangeException($"Failed to commit parameter transaction: {commit.Result}.");

            return ack;
        }

        /// <summary>
        /// Sets a single extended parameter inside a parameter transaction, making the write atomic:
        /// sends MAV_CMD_PARAM_TRANSACTION (Start) over the param_ext transport, then PARAM_EXT_SET,
        /// waits for the final PARAM_EXT_ACK (following any <see cref="ParamExtAckResult.InProgress"/>
        /// responses), then sends MAV_CMD_PARAM_TRANSACTION (Commit). The transaction is not cancelled
        /// automatically if a step fails; use <see cref="CancelTransactionAsync"/> if needed.
        /// </summary>
        public static async Task<ParamExtAck> SetExtParameterTransactionalAsync(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            MavParamExtValue value,
            Func<byte[], CancellationToken, Task> sendAsync,
            Func<CancellationToken, Task<Frame>> receiveFrameAsync,
            int timeoutMs = DefaultTimeoutMs,
            int maxRetries = DefaultRetries,
            CancellationToken cancellationToken = default)
        {
            if (sendAsync == null) throw new ArgumentNullException(nameof(sendAsync));
            if (receiveFrameAsync == null) throw new ArgumentNullException(nameof(receiveFrameAsync));
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (string.IsNullOrEmpty(value.ParamId)) throw new ArgumentException("Param id cannot be empty.", nameof(value));

            var begin = await BeginTransactionAsync(
                context, systemId, componentId, targetSystem, targetComponent, ParamTransactionTransport.ParamExt,
                sendAsync, receiveFrameAsync, timeoutMs, maxRetries, cancellationToken).ConfigureAwait(false);
            if (!begin.Success)
                throw new ParameterExchangeException($"Failed to begin parameter transaction: {begin.Result}.");

            var setFrame = CreateParamExtSet(context, systemId, componentId, targetSystem, targetComponent, value);
            var ack = await SendAndMatchExtAckAsync(
                setFrame, value.ParamId, timeoutMs, maxRetries, sendAsync, receiveFrameAsync, cancellationToken).ConfigureAwait(false);

            if (ack.Result != ParamExtAckResult.Accepted)
                throw new ParameterExchangeException($"Extended parameter set for '{value.ParamId}' failed: {ack.Result}.");

            var commit = await CommitTransactionAsync(
                context, systemId, componentId, targetSystem, targetComponent, ParamTransactionTransport.ParamExt,
                sendAsync, receiveFrameAsync, timeoutMs, maxRetries, cancellationToken).ConfigureAwait(false);
            if (!commit.Success)
                throw new ParameterExchangeException($"Failed to commit parameter transaction: {commit.Result}.");

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

        private static char[] ToWireValue(byte[] raw)
        {
            var chars = new char[ExtValueLength];
            for (int i = 0; i < ExtValueLength; i++)
                chars[i] = i < raw.Length ? (char)raw[i] : '\0';
            return chars;
        }

        private static byte[] FromWireValue(char[] raw)
        {
            var bytes = new byte[ExtValueLength];
            int count = Math.Min(raw.Length, ExtValueLength);
            for (int i = 0; i < count; i++)
                bytes[i] = (byte)raw[i];
            return bytes;
        }

        private static void EnsureParamAckTransactionSupported(MavLinkContext context)
        {
            if (!context.Metadata.MessagesDictionary.ContainsKey(ParamAckTransactionId))
                throw new ParameterExchangeException(
                    "PARAM_ACK_TRANSACTION (message id 19) is not defined in the current dialect; " +
                    "classic parameter transactions require a dialect that includes it, e.g. DialectType.Development.");
        }

        private static Task<CommandResult> SendTransactionCommandAsync(
            MavLinkContext context,
            byte systemId,
            byte componentId,
            byte targetSystem,
            byte targetComponent,
            ParamTransactionAction action,
            ParamTransactionTransport transport,
            Func<byte[], CancellationToken, Task> sendAsync,
            Func<CancellationToken, Task<Frame>> receiveFrameAsync,
            int timeoutMs,
            int maxRetries,
            CancellationToken cancellationToken)
        {
            if (sendAsync == null) throw new ArgumentNullException(nameof(sendAsync));
            if (receiveFrameAsync == null) throw new ArgumentNullException(nameof(receiveFrameAsync));

            var frame = CommandProtocol.CreateCommandLong(
                context, systemId, componentId, targetSystem, targetComponent,
                MavCmdParamTransaction,
                [(float)action, (float)transport, 0f, 0f, 0f, 0f, 0f],
                confirmation: 0);

            return CommandProtocol.SendCommandAsync(
                frame, sendAsync, receiveFrameAsync, timeoutMs, maxRetries, null, cancellationToken);
        }

        private static async Task<Frame> SendAndMatchExtValueAsync(
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
                        if (TryParseParamExtValue(response, out var value) && value != null && value.ParamId == expectedParamId)
                            return response;
                    }
                }
                catch (ParameterExchangeException)
                {
                    // Retry after timeout.
                }
            }

            throw new TimeoutException($"Extended parameter exchange for '{expectedParamId}' timed out after {totalTries} attempts.");
        }

        private static async Task<ParamExtAck> SendAndMatchExtAckAsync(
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
                        if (TryParseParamExtAck(response, out var ack) && ack != null && ack.ParamId == expectedParamId)
                        {
                            if (ack.Result == ParamExtAckResult.InProgress)
                                continue; // The final acknowledgement follows.

                            return ack;
                        }
                    }
                }
                catch (ParameterExchangeException)
                {
                    // Retry after timeout.
                }
            }

            throw new TimeoutException($"Extended parameter set for '{expectedParamId}' timed out after {totalTries} attempts.");
        }

        private static async Task<ParamAckTransaction> SendAndMatchAckTransactionAsync(
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
                        if (TryParseParamAckTransaction(response, out var ack) && ack != null && ack.ParamId == expectedParamId)
                        {
                            if (ack.Result == ParamExtAckResult.InProgress)
                                continue; // The final acknowledgement follows.

                            return ack;
                        }
                    }
                }
                catch (ParameterExchangeException)
                {
                    // Retry after timeout.
                }
            }

            throw new TimeoutException($"Parameter transaction set for '{expectedParamId}' timed out after {totalTries} attempts.");
        }
    }
}
