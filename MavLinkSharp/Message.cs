using MavLinkSharp.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace MavLinkSharp
{
    /// <summary>
    /// Represents a message definition from a MAVLink XML dialect.
    /// This class also serves as the main entry point for parsing the MAVLink protocol.
    /// </summary>
    [XmlType("message")]
    public class Message
    {
        /// <summary>
        /// Unique index number of this message.
        /// </summary>
        [XmlAttribute(AttributeName = "id")]
        public uint Id { get; set; }

        /// <summary>
        /// Human readable form for the message. It is used for naming helper functions in generated libraries, but is not sent over the wire.
        /// </summary>
        [XmlAttribute(AttributeName = "name")]
        public string Name { get; set; }

        /// <summary>
        /// A tag indicating that the message is a "work in progress" (optional).
        /// </summary>
        [XmlElement(ElementName = "wip")]
        public Wip Wip { get; set; }

        /// <summary>
        /// A tag indicating that the message is deprecated (optional).
        /// </summary>
        [XmlElement(ElementName = "deprecated")]
        public Deprecated Deprecated { get; set; }

        /// <summary>
        /// Human readable description of message, shown in user interfaces and in code comments. This should contain all information (and hyperlinks) to fully understand the message.
        /// </summary>
        [XmlElement(ElementName = "description")]
        public string Description { get; set; }

        /// <summary>
        /// Encodes one field of the message. The field value is its name/text string used in GUI documentation (but not sent over the wire). Every message must have at least one field.
        /// </summary>
        [XmlElement(ElementName = "field")]
        public List<Field> Fields { get; set; } = new List<Field>();

        /// <summary>
        /// This self-closing tag is used to indicate that subsequent fields apply to MAVLink 2 only.
        /// </summary>
        /// <remarks><![CDATA[The tag should be used for MAVLink 1 messages only (id < 256) that have been extended in MAVLink 2.]]></remarks>
        [XmlElement(ElementName = "extensions")]
        public Extensions Extensions { get; set; }

        #region Helpers
        /// <summary>
        /// Payload length in bytes (base fields only).
        /// </summary>
        public int PayloadLength { get; set; }

        /// <summary>
        /// Maximum payload length in bytes (including extensions).
        /// </summary>
        public int MaxPayloadLength { get; set; }

        /// <summary>
        /// The message base fields ordered according to the MAVLink spec.
        /// </summary>
        [XmlIgnore]
        public List<Field> OrderedBaseFields => OrderedFields.Where(x => !x.Extended).ToList();

        /// <summary>
        /// The message extended fields.
        /// </summary>
        [XmlIgnore]
        public List<Field> ExtendedFields => OrderedFields.Where(x => x.Extended).ToList();

        /// <summary>
        /// The message fields ordered according to the MAVLink spec.
        /// </summary>
        [XmlIgnore]
        public List<Field> OrderedFields { get; private set; }

        /// <summary>
        /// Whether the message to be parsed.
        /// </summary>
        [XmlIgnore]
        public bool Included { get; private set; }

        /// <summary>
        /// The checksum of the XML structure for each message used to verify that the sender
        /// and receiver have a shared understanding of the over-the-wire format of a
        /// particular message. 
        /// Format: "message_name [field1_type field1_name [field2_type field2_name [...]]]"
        /// </summary>
        /// <remarks>
        /// Extension fields are not included in the CRC_EXTRA calculation.
        /// See <seealso cref="SetCrcExtra"/> for more details.
        /// </remarks>
        [XmlIgnore]
        public byte CrcExtra { get; private set; }

        /// <summary>
        /// Include the message for parsing.
        /// </summary>
        /// <remarks>
        /// The recommended way to filter messages is by using the static <see cref="MavLink.IncludeMessages(uint[])"/> method.
        /// </remarks>
        /// <seealso cref="MavLink.IncludeMessages(uint[])"/>
        public void Include()
        {
            Included = true;
        }

        /// <summary>
        /// Exclude the message from parsing.
        /// </summary>
        /// <remarks>
        /// The recommended way to filter messages is by using the static <see cref="MavLink.ExcludeMessages(uint[])"/> method.
        /// </remarks>
        /// <seealso cref="MavLink.ExcludeMessages(uint[])"/>
        public void Exclude()
        {
            Included = false;
        }

        internal void SetOrderedFields()
        {
            // Fields are sorted according to their native data size
            var fields = Fields.Where(x => !x.Extended).OrderByDescending(x => x.Ordinal).ToList();

            // Extension fields are sent in XML-declaration order and are not included
            var extensions = Fields.Where(x => x.Extended).ToList();

            OrderedFields = new List<Field>(fields);

            OrderedFields.AddRange(extensions);
        }

        internal void SetCrcExtra()
        {
            var extra = new List<byte>(Encoding.UTF8.GetBytes($"{Name} "));

            foreach (var field in OrderedBaseFields)
            {
                // TODO: create a get property for the curated field type, e.g. CuratedType
                var position = field.Type.IndexOf("[");

                var type = position > -1 ? field.Type.Substring(0, position) : field.Type.Replace("_mavlink_version", "");

                extra.AddRange(Encoding.UTF8.GetBytes($"{type} {field.Name} "));

                if (field.ArrayLength > 0)
                {
                    extra.Add(Convert.ToByte(field.ArrayLength));
                }
            }

            var crc = Crc.Calculate(extra.ToArray());

            CrcExtra = (byte)((crc & 0xFF) ^ (crc >> 8));
        }
        #endregion
    }
}