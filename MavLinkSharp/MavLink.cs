using MavLinkSharp.Enums;
using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace MavLinkSharp
{
    /// <summary>
    /// The format and structure of a dialect file.
    /// </summary>
    [XmlRoot(ElementName = "mavlink")]
    public class MavLink
    {
        /// <summary>
        /// Used to specify any other XML files included in your dialect.
        /// </summary>
        [XmlElement(ElementName = "include")]
        public List<string> Includes { get; set; } = new List<string>();

        /// <summary>
        /// The minor version number for the release, as included in the HEARTBEAT mavlink_version field.
        /// </summary>
        [XmlElement(ElementName = "version")]
        public string Version { get; set; }

        /// <summary>
        /// Unique number for your dialect.
        /// </summary>
        [XmlElement(ElementName = "dialect")]
        public string Dialect { get; set; }

        /// <summary>
        /// Dialect-specific enumerations, used to define named values that may be used as options in
        /// messages, commands, errors, states, or modes.
        /// </summary>
        [XmlArray("enums")]
        [XmlArrayItem("enum")]
        public List<Enum> Enums { get; set; } = new List<Enum>();

        /// <summary>
        /// Dialect-specific messages.
        /// </summary>
        [XmlArray("messages")]
        [XmlArrayItem("message")]
        public List<Message> Messages { get; set; } = new List<Message>();

        /// <summary>
        /// Throws an exception if the Default context has not been initialized.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public static void ThrowIfNotInitialized()
        {
            if (!MavLinkContext.Default.IsInitialized)
            {
                throw new InvalidOperationException("MavLink.Initialize() must be called before using the library.");
            }
        }
        
        /// <summary>
        /// Initializes the global default context using the given dialect type.
        /// </summary>
        public static void Initialize(DialectType dialectType, params uint[] messageIds)
        {
            MavLinkContext.Default.Initialize(dialectType, messageIds);
        }

        /// <summary>
        /// Initializes the global default context using the given dialect path.
        /// </summary>
        public static void Initialize(string dialectPath = "common.xml", params uint[] messageIds)
        {
            MavLinkContext.Default.Initialize(dialectPath, messageIds);
        }

        /// <summary>
        /// Include message IDs for parsing in the default context.
        /// </summary>
        public static void IncludeMessages(params uint[] messageIds)
        {
            MavLinkContext.Default.IncludeMessages(messageIds);
        }

        /// <summary>
        /// Exclude message IDs from parsing in the default context.
        /// </summary>
        public static void ExcludeMessages(params uint[] messageIds)
        {
            MavLinkContext.Default.ExcludeMessages(messageIds);
        }
    }
}
