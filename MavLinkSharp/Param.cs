using System.Xml.Serialization;

namespace MavLinkSharp
{
    /// <summary>
    /// <![CDATA[Represents a <param> tag within a MAVLink <entry>, providing metadata for a command parameter.]]>
    /// </summary>
    public class Param
    {
        /// <summary>
        /// The parameter number (1 - 7).
        /// </summary>
        [XmlAttribute(AttributeName = "index")]
        public int Index { get; set; }

        /// <summary>
        /// Display name to represent the parameter in a GCS or other UI. All words in label should be capitalized.
        /// </summary>
        [XmlAttribute(AttributeName = "label")]
        public string Label { get; set; }

        /// <summary>
        /// SI units for the value.
        /// </summary>
        [XmlAttribute(AttributeName = "units")]
        public string Units { get; set; }

        /// <summary>
        /// Possible value from enumeration for the parameter (if applicable).
        /// </summary>
        [XmlAttribute(AttributeName = "enum")]
        public string Enum { get; set; }

        /// <summary>
        /// Decimal places to use if the parameter value is displayed.
        /// </summary>
        [XmlAttribute(AttributeName = "decimalPlaces")]
        public int DecimalPlaces { get; set; }

        /// <summary>
        /// Allowed increments for the parameter value.
        /// </summary>
        [XmlAttribute(AttributeName = "increment")]
        public int Increment { get; set; }

        /// <summary>
        /// Minimum value for parameter.
        /// </summary>
        [XmlAttribute(AttributeName = "minValue")]
        public float MinValue { get; set; }

        /// <summary>
        /// Maximum value for parameter.
        /// </summary>
        [XmlAttribute(AttributeName = "maxValue")]
        public float MaxValue { get; set; }

        /// <summary>
        /// Whether the parameter is reserved for future use. If the attributes is not declared, then implicitly reserved="False".
        /// </summary>
        [XmlAttribute(AttributeName = "reserved")]
        public bool Reserved { get; set; }

        /// <summary>
        /// Default value for the parameter (primarily used for reserved parameters where the value is 0 or NaN).
        /// </summary>
        [XmlAttribute(AttributeName = "default")]
        public string Default  { get; set; }

        /// <summary>
        /// Parameter description string (tag body).
        /// </summary>
        [XmlText]
        public string TagBody { get; set; }
    }
}
