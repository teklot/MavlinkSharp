using System.Xml.Serialization;

namespace MavLinkSharp
{
    /// <summary>
    /// <![CDATA[Represents the <deprecated> tag within a MAVLink XML dialect file, 
    /// providing information about a deprecated message or enum.]]>
    /// </summary>
    public class Deprecated
    {
        /// <summary>
        /// Year/month when deprecation started. Format: YYYY-MM.
        /// </summary>
        [XmlAttribute(AttributeName = "since")]
        public string Since { get; set; }

        /// <summary>
        /// The name of entity that supersedes this item.
        /// </summary>
        [XmlAttribute(AttributeName = "replaced_by")]
        public string ReplacedBy { get; set; }

        /// <summary>
        /// Deprecation description string (tag body).
        /// </summary>
        [XmlElement(ElementName = "description")]
        public string Description { get; set; }

        /// <summary>
        /// Deprecated description string (tag body).
        /// </summary>
        [XmlText]
        public string TagBody { get; set; }
    }
}