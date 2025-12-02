namespace MavLinkSharp.Enums
{
    /// <summary>
    /// Specifies the possible reasons why a MAVLink frame parsing operation might fail.
    /// </summary>
    public enum ErrorReason
    {
        /// <summary>
        /// No error occurred. Parsing was successful.
        /// </summary>
        None = 0,
        /// <summary>
        /// The start marker (0xFE for MAVLink 1, 0xFD for MAVLink 2) was not found at the expected position.
        /// </summary>
        StartMarkerNotFound,
        /// <summary>
        /// The calculated checksum does not match the checksum in the MAVLink frame.
        /// </summary>
        BadChecksum,
        /// <summary>
        /// The MAVLink frame is shorter than the minimum expected length for its protocol version.
        /// </summary>
        FrameTooShort,
        /// <summary>
        /// The MAVLink frame is longer than the maximum expected length for its protocol version.
        /// </summary>
        FrameTooLong,
        /// <summary>
        /// The message ID found in the frame does not correspond to any known message in the loaded dialects.
        /// </summary>
        MessageNotFound,
        /// <summary>
        /// The message was found in the dialects, but it has been explicitly excluded from parsing.
        /// </summary>
        MessageExcluded,
        /// <summary>
        /// The payload length specified in the frame header is invalid or inconsistent with the message definition.
        /// </summary>
        PayloadLengthInvalid,
        /// <summary>
        /// The frame's length indicates it should contain a checksum, but it does not.
        /// </summary>
        FrameHasNoChecksum,
        /// <summary>
        /// The signature length in a MAVLink 2 frame is invalid.
        /// </summary>
        SignatureLengthInvalid,
    }
}
