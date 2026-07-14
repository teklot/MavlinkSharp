using System;
using MavLinkSharp.Protocols;

namespace MavLinkSharp.Connection
{
    /// <summary>
    /// Configuration options for <see cref="MavLinkConnection"/>.
    /// </summary>
    public class ConnectionOptions
    {
        /// <summary>
        /// MAVLink system ID for outgoing messages. Defaults to 1.
        /// </summary>
        public byte SystemId { get; set; } = 1;

        /// <summary>
        /// MAVLink component ID for outgoing messages. Defaults to 1.
        /// </summary>
        public byte ComponentId { get; set; } = 1;

        /// <summary>
        /// The MAVLink dialect context to use for parsing and constructing messages.
        /// Defaults to <see cref="MavLinkContext.Default"/>.
        /// </summary>
        public MavLinkContext Context { get; set; }

        /// <summary>
        /// Optional MAVLink 2 signing configuration for outgoing frames.
        /// </summary>
        public MavLinkSigning Signing { get; set; }

        /// <summary>
        /// Delay in milliseconds between reconnection attempts. Defaults to 1000.
        /// </summary>
        public int ReconnectDelayMs { get; set; } = 1000;

        /// <summary>
        /// Maximum number of consecutive reconnection attempts before giving up.
        /// Use <see cref="int.MaxValue"/> for unlimited retries. Defaults to 5.
        /// </summary>
        public int MaxReconnectAttempts { get; set; } = 5;

        /// <summary>
        /// Whether to automatically reconnect on transport failure. Defaults to true.
        /// </summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>
        /// Interval in milliseconds for automatic heartbeat generation.
        /// Set to 0 or negative to disable automatic heartbeats. Defaults to 1000 (1 second).
        /// </summary>
        public int HeartbeatIntervalMs { get; set; } = 1000;

        /// <summary>
        /// MAV_TYPE value for auto-generated heartbeats. Defaults to 0 (MAV_TYPE_GENERIC).
        /// </summary>
        public byte HeartbeatType { get; set; } = 0;

        /// <summary>
        /// MAV_AUTOPILOT value for auto-generated heartbeats. Defaults to 0 (MAV_AUTOPILOT_GENERIC).
        /// </summary>
        public byte HeartbeatAutopilot { get; set; } = 0;

        /// <summary>
        /// MAV_STATE value for auto-generated heartbeats. Defaults to 0 (MAV_STATE_UNINIT).
        /// </summary>
        public byte HeartbeatSystemStatus { get; set; } = 0;

        /// <summary>
        /// Buffer size in bytes for the receive pipeline. Defaults to 65536.
        /// </summary>
        public int ReceiveBufferSize { get; set; } = 65536;
    }
}
