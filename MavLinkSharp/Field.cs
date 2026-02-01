using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Xml.Serialization;

namespace MavLinkSharp
{
    /// <summary>
    /// Represents a single data field within a MAVLink message, as defined in the XML dialect.
    /// </summary>
    public class Field
    {
        /// <summary>
        /// Size of the data required to store/represent the data type.
        /// </summary>
        /// <remarks>Fields can be signed/unsigned integers of size 8, 16, 32, 64 bits ({u)int8_t, (u)int16_t, (u)int32_t, (u)int64_t), single/double precision IEEE754 floating point numbers. They can also be arrays of the other types - e.g. uint16_t[10]</remarks>
        [XmlAttribute(AttributeName = "type")]
        public string Type { get; set; }

        /// <summary>
        /// Name of the field (used in code).
        /// </summary>
        [XmlAttribute(AttributeName = "name")]
        public string Name { get; set; }

        /// <summary>
        /// Name of an enumeration defining possible values of the field (e.g. MAV_BATTERY_CHARGE_STATE).
        /// </summary>
        [XmlAttribute(AttributeName = "enum")]
        public string Enum { get; set; }

        /// <summary>
        /// The units for message fields that take numeric values (not enumerations). These are defined in the schema (search on name="SI_Unit")
        /// </summary>
        [XmlAttribute(AttributeName = "units")]
        public string Units { get; set; }

        /// <summary>
        /// This should be set as display="bitmask" for bitmask fields (hint to ground station that enumeration values must be displayed as checkboxes).
        /// </summary>
        [XmlAttribute(AttributeName = "display")]
        public string Display { get; set; }

        /// <summary>
        /// The format string used for displaying the field value (e.g., in a UI).
        /// </summary>
        [XmlAttribute(AttributeName = "print_format")]
        public string PrintFormat { get; set; }

        /// <summary>
        /// The default value for the field.
        /// </summary>
        [XmlAttribute(AttributeName = "default")]
        public string Default { get; set; }

        /// <summary>
        /// If true, this indicates that the message contains the information for a particular sensor or battery (e.g. Battery 1, Battery 2, etc.) and that this field indicates which sensor. Default is false.
        /// </summary>
        [XmlAttribute(AttributeName = "instance")]
        public bool Instance { get; set; }

        /// <summary>
        /// Specifies a value that can be set on a field to indicate that the data is invalid: the recipient should ignore the field if it has this value. For example, BATTERY_STATUS.current_battery specifies invalid="-1", so a battery that does not measure supplied current should set BATTERY_STATUS.current_battery to -1.
        /// </summary>
        /// <remarks>Where possible the value that indicates the field is invalid should be selected to outside the expected/valid range of the field (0 is preferred if it is not an acceptable value for the field). For integers we usually select the largest possible value (i.e. UINT16_MAX, INT16_MAX, UINT8_MAX, UINT8_MAX). For floats we usually select invalid="NaN".</remarks>
        [XmlAttribute(AttributeName = "invalid")]
        public string Invalid { get; set; }

        /// <summary>
        /// Used to indicate that the field applies to MAVLink 2 only.
        /// </summary>
        /// <remarks><![CDATA[Should be used for MAVLink 1 messages only (id < 256) that have been extended in MAVLink 2.]]></remarks>
        /// <remarks>This was NOT part of schema and was added to have the indicator on the field itself.</remarks>
        [XmlAttribute(AttributeName = "extended")]
        public bool Extended { get; set; }

        /// <summary>
        /// Field description string (tag body).
        /// </summary>
        [XmlText]
        public string TagBody { get; set; }

        #region Helpers
        /// <summary>
        /// Standard CSharp data type based on the Type attribute.
        /// </summary>
        [XmlIgnore]
        public Type DataType { get; private set; }

        /// <summary>
        /// Field position in the payload based on the Type attribute.
        /// </summary>
        [XmlIgnore]
        public int Ordinal { get; private set; }

        /// <summary>
        /// Field length in bytes.
        /// </summary>
        [XmlIgnore]
        public int Length { get; private set; }

        /// <summary>
        /// Element count if referred by an array.
        /// </summary>
        [XmlIgnore]
        public int ArrayLength { get; private set; } = 0;

        /// <summary>
        /// Element data type if referred by an array.
        /// </summary>
        [XmlIgnore]
        public Type ElementType => DataType.IsArray ? DataType.GetElementType() : DataType;

        /// <summary>
        /// Sets the field data type based on the Type attribute.
        /// </summary>
        /// <exception cref="Exception"></exception>
        public void SetDataType()
        {
            var array = Type.Contains('[') && Type.Contains(']');

            if (Type.StartsWith("char"))
            {
                DataType = array ? typeof(Char[]) : typeof(Char);
            }
            else if (Type.StartsWith("int8_t"))
            {
                DataType = array ? typeof(SByte[]) : typeof(SByte);
            }
            else if (Type.StartsWith("uint8_t"))
            {
                DataType = array ? typeof(Byte[]) : typeof(Byte);
            }
            else if (Type.StartsWith("int16_t"))
            {
                DataType = array ? typeof(Int16[]) : typeof(Int16);
            }
            else if (Type.StartsWith("uint16_t"))
            {
                DataType = array ? typeof(UInt16[]) : typeof(UInt16);
            }
            else if (Type.StartsWith("int32_t"))
            {
                DataType = array ? typeof(Int32[]) : typeof(Int32);
            }
            else if (Type.StartsWith("uint32_t"))
            {
                DataType = array ? typeof(UInt32[]) : typeof(UInt32);
            }
            else if (Type.StartsWith("float"))
            {
                DataType = array ? typeof(Single[]) : typeof(Single);
            }
            else if (Type.StartsWith("int64_t"))
            {
                DataType = array ? typeof(Int64[]) : typeof(Int64);
            }
            else if (Type.StartsWith("uint64_t"))
            {
                DataType = array ? typeof(UInt64[]) : typeof(UInt64);
            }
            else if (Type.StartsWith("double"))
            {
                DataType = array ? typeof(Double[]) : typeof(Double);
            }
            else
            {
                throw new Exception($"Invalid Type:{Type}.");
            }
        }

        /// <summary>
        /// Sets the field length based on the Type attribute.
        /// </summary>
        /// <remarks>Fields can be signed/unsigned integers of size 8, 16, 32, 64 bits
        /// (u)int8_t, (u)int16_t, (u)int32_t, (u)int64_t), single/double precision 
        /// IEEE754 floating point numbers. They can also be arrays of the other 
        /// types - e.g. uint16_t[10].</remarks>
        internal void SetLength()
        {
            if (DataType.IsArray)
            {
                var startIndex = Type.IndexOf('[');
                var endIndex = Type.LastIndexOf(']');

                if (startIndex == -1 || endIndex == -1 || endIndex <= startIndex)
                {
                    throw new Exception($"Invalid array type format: {Type}");
                }

                var arrayLengthString = Type.Substring(startIndex + 1, endIndex - startIndex - 1);

                if (!int.TryParse(arrayLengthString, out var arrayLength))
                {
                    throw new Exception($"Invalid array length: {arrayLengthString}");
                }

                ArrayLength = arrayLength;

                Length = ArrayLength * Marshal.SizeOf(ElementType);
            }
            else
            {
                Length = Marshal.SizeOf(ElementType);
            }
        }

        /// <summary>
        /// Sets the internal ordinal (byte-size based order) of the field for parsing purposes.
        /// </summary>
        internal void SetOrdinal()
        {
            var type = DataType.IsArray ? DataType.GetElementType() : DataType;

            Ordinal = Marshal.SizeOf(type);
        }

        /// <summary>
        /// Reads the field's value (or array of values) from the provided <see cref="ReadOnlySpan{Byte}"/>.
        /// </summary>
        /// <param name="span">The <see cref="ReadOnlySpan{Byte}"/> to read the field data from.</param>
        /// <returns>The deserialized value of the field, or an array of values if the field is an array type.</returns>
        internal object GetValue(ref ReadOnlySpan<byte> span)
        {
            if (DataType.IsArray)
            {
                var values = Array.CreateInstance(ElementType, ArrayLength);
                
                for (var i = 0; i < ArrayLength; i++)
                {
                    values.SetValue(ReadValue(ElementType, ref span), i);
                }

                return values;
            }

            return ReadValue(ElementType, ref span);
        }

        private static object ReadValue(Type type, ref ReadOnlySpan<byte> span)
        {
            object result;

            if (type == typeof(char))
            {
                result = (char)span[0];
                span = span.Slice(1);
            }
            else if (type == typeof(sbyte))
            {
                result = (sbyte)span[0];
                span = span.Slice(1);
            }
            else if (type == typeof(byte))
            {
                result = span[0];
                span = span.Slice(1);
            }
            else if (type == typeof(short))
            {
                result = BinaryPrimitives.ReadInt16LittleEndian(span);
                span = span.Slice(2);
            }
            else if (type == typeof(ushort))
            {
                result = BinaryPrimitives.ReadUInt16LittleEndian(span);
                span = span.Slice(2);
            }
            else if (type == typeof(int))
            {
                result = BinaryPrimitives.ReadInt32LittleEndian(span);
                span = span.Slice(4);
            }
            else if (type == typeof(uint))
            {
                result = BinaryPrimitives.ReadUInt32LittleEndian(span);
                span = span.Slice(4);
            }
            else if (type == typeof(float))
            {
                result = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(span));
                span = span.Slice(4);
            }
            else if (type == typeof(long))
            {
                result = BinaryPrimitives.ReadInt64LittleEndian(span);
                span = span.Slice(8);
            }
            else if (type == typeof(ulong))
            {
                result = BinaryPrimitives.ReadUInt64LittleEndian(span);
                span = span.Slice(8);
            }
            else if (type == typeof(double))
            {
                result = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(span));
                span = span.Slice(8);
            }
            else
            {
                throw new Exception($"Unknown type: {type}");
            }

            return result;
        }
        #endregion
    }
}