using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Serialization;

namespace MavLinkSharp
{
    /// <summary>
    /// <![CDATA[Represents an <enum> tag within a MAVLink XML dialect, defining a named enumeration of values.]]>
    /// </summary>
    [XmlType("enum")]
    public class Enum
    {
        /// <summary>
        /// The name of the enumeration (mandatory). This is a string of capitalized, underscore-separated words.
        /// </summary>
        [XmlAttribute(AttributeName = "name")]
        public string Name { get; set; }

        /// <summary>
        /// Indicates the value is a flag.
        /// </summary>
        [XmlAttribute(AttributeName = "bitmask")]
        [DefaultValue(false)]
        public bool Bitmask { get; set; }

        /// <summary>
        /// A string describing the purpose of the enumeration (optional).
        /// </summary>
        [XmlElement(ElementName = "description")]
        public string Description { get; set; }

        /// <summary>
        /// zero or more entries (optional).
        /// </summary>
        [XmlElement(ElementName = "entry")]
        public List<Entry> Entries { get; set; } = new List<Entry>();

        /// <summary>
        /// A tag indicating that the enumeration is deprecated (optional).
        /// </summary>
        [XmlElement(ElementName = "deprecated")]
        public Deprecated Deprecated { get; set; }
    }
}