using System.Collections.Generic;
using System.Xml.Serialization;

namespace MavLinkSharp
{
    /// <summary>
    /// <![CDATA[Represents an <entry> tag within a MAVLink <enum>, defining a single value within that enumeration.]]>
    /// </summary>
    [XmlType("entry")]
    public class Entry
    {
        /// <summary>
        /// A tag indicating that the entry is a "work in progress" (optional).
        /// </summary>
        [XmlElement(ElementName = "wip")]
        public Wip Wip { get; set; }

        /// <summary>
        /// A tag indicating that the entry is deprecated (optional).
        /// </summary>
        [XmlElement(ElementName = "deprecated")]
        public Deprecated Deprecated { get; set; }

        /// <summary>
        /// The name of the entry value (mandatory). This is a string of capitalized, underscore-separated words.
        /// </summary>
        [XmlAttribute(AttributeName = "name")]
        public string Name { get; set; }

        /// <summary>
        /// The value for the entry (mandatory).
        /// </summary>
        [XmlAttribute(AttributeName = "value")]
        public long Value { get; set; }

        /// <summary>
        /// A string describing the purpose of the enumeration (optional).
        /// </summary>
        [XmlElement(ElementName = "description")]
        public string Description { get; set; }

        /// <summary>
        /// Up to 7 parameter tags, numbered using an index attribute (optional).
        /// </summary>
        [XmlElement(ElementName = "param")]
        public List<Param> Params { get; set; } = new List<Param>();
    }
}