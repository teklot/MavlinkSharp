using MavLinkSharp.Enums;
using System;
using System.Collections.Generic;

namespace MavLinkSharp
{
    /// <summary>
    /// Represents a single, parsed MAVLink message frame.
    /// This class contains the raw data from the wire and the decoded payload fields.
    /// </summary>
    public class Frame
    {
        #region Common Properties
        /// <summary>
        /// Protocol start marker.
        /// For MAVLink 1, this is 0xFE. For MAVLink 2, this is 0xFD.
        /// </summary>
        public byte StartMarker { get; internal set; }
        /// <summary>
        /// Length of the payload.
        /// </summary>
        public byte PayloadLength { get; internal set; }
        /// <summary>
        /// MAVLink 2 incompatibility flags. If null, the frame is MAVLink 1.
        /// </summary>
        public byte? IncompatibilityFlags { get; internal set; }
        /// <summary>
        /// MAVLink 2 compatibility flags. If null, the frame is MAVLink 1.
        /// </summary>
        public byte? CompatibilityFlags { get; internal set; }
        /// <summary>
        /// Sequence of the packet.
        /// </summary>
        public byte PacketSequence { get; internal set; }
        /// <summary>
        /// ID of the sending system.
        /// </summary>
        public byte SystemId { get; internal set; }
        /// <summary>
        /// ID of the sending component.
        /// </summary>
        public byte ComponentId { get; internal set; }
        /// <summary>
        /// ID of the message.
        /// </summary>
        public uint MessageId { get; internal set; }
        /// <summary>
        /// The raw message payload.
        /// </summary>
        public byte[] Payload { get; internal set; }
        /// <summary>
        /// Checksum of the frame.
        /// </summary>
        public ushort Checksum { get; internal set; }
        /// <summary>
        /// MAVLink 2 signature for signing packets. If null, the frame is not signed.
        /// </summary>
        public byte[] Signature { get; internal set; }
        #endregion

        #region Extra Properties
        /// <summary>
        /// The UTC timestamp when the frame object was created.
        /// </summary>
        public DateTime Timestamp { get; }
        /// <summary>
        /// A dictionary holding the decoded payload fields as key-value pairs (field name and value).
        /// </summary>
        public Dictionary<string, object> Fields { get; }
        /// <summary>
        /// If an error occurred during parsing, this property specifies the reason.
        /// </summary>
        public ErrorReason ErrorReason { get; internal set; }
        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="Frame"/> class and sets the creation timestamp.
        /// </summary>
        /// <param name="messageName">For internal use. (Currently not used and can be ignored).</param>
        public Frame(string messageName = null)
        {
            Timestamp = DateTime.UtcNow;
            Fields = new Dictionary<string, object>();
        }
    }
}
