using MavLinkSharp.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
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

        private static bool _isInitialized = false;

        private static Dictionary<string, MavLink> _dialects = new Dictionary<string, MavLink>();

        /// <summary>
        /// Throws an exception if the Initialize method has not been called.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public static void ThrowIfNotInitialized()
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("MavLink.Initialize() must be called before using the library.");
            }
        }
        
        /// <summary>
        /// Initializes the message metadata and internal objects using the given dialect type.
        /// </summary>
        /// <param name="dialectType">The type of the dialect to initialize.</param>
        /// <param name="messageIds">Optional. A list of message IDs to include for parsing. If empty, all messages from the dialect are included.</param>
        public static void Initialize(DialectType dialectType, params uint[] messageIds)
        {
            string dialectPath = dialectType switch
            {
                DialectType.All => "all.xml",
                DialectType.Ardupilotmega => "ardupilotmega.xml",
                DialectType.ASLUAV => "ASLUAV.xml",
                DialectType.AVSSUAS => "AVSSUAS.xml",
                DialectType.Common => "common.xml",
                DialectType.Cubepilot => "cubepilot.xml",
                DialectType.Development => "development.xml",
                DialectType.Icarous => "icarous.xml",
                DialectType.Matrixpilot => "matrixpilot.xml",
                DialectType.Minimal => "minimal.xml",
                DialectType.Paparazzi => "paparazzi.xml",
                DialectType.PythonArrayTest => "python_array_test.xml",
                DialectType.Standard => "standard.xml",
                DialectType.Storm32 => "storm32.xml",
                DialectType.Test => "test.xml",
                DialectType.Ualberta => "ualberta.xml",
                DialectType.UAvionix => "uAvionix.xml",
                _ => throw new ArgumentOutOfRangeException(nameof(dialectType), dialectType, null),
            };

            Initialize(dialectPath, messageIds);
        }

        /// <summary>
        /// Initializes the message metadata and internal objects using the given dialect.
        /// </summary>
        /// <param name="dialectPath">
        /// The path to the main dialect file. 
        /// If a full path is not provided, the file is assumed to be in a 'Dialects' folder relative to the application's base directory (e.g., 'YourApp/bin/Debug/netX.X/Dialects/all.xml').
        /// The default value is "common.xml".
        /// </param>
        /// <param name="messageIds">Optional. A list of message IDs to include for parsing. If empty, all messages from the dialect are included.</param>
        public static void Initialize(string dialectPath = "common.xml", params uint[] messageIds)
        {
            var dialects = MavLink.Deserialize(dialectPath);

            #region Serialize the dialects to verify their contents
            //var serializer = new XmlSerializer(typeof(MavLink));

            //foreach (var (name, dialect) in dialects)
            //{
            //    if (!Directory.Exists(@"C:\Temp\Dialects"))
            //    {
            //        Directory.CreateDirectory(@"C:\Temp\Dialects");
            //    }

            //    using var sw = new StreamWriter(Path.Combine(@"C:\Temp\Dialects", name));

            //    serializer.Serialize(sw, dialect);
            //}
            #endregion

            Metadata.Initialize(dialects);

            IncludeMessages(messageIds);

            _isInitialized = true;
        }

        private static Dictionary<string, MavLink> Deserialize(string dialectPath)
        {
            if (!File.Exists(dialectPath))
            {
                // If file does not exists search in the default directory
                dialectPath = Path.Combine("Dialects", dialectPath);

                if (!File.Exists(dialectPath))
                {
                    throw new FileNotFoundException($"{dialectPath} not found.");
                }
            }

            var dialectFileName = Path.GetFileName(dialectPath);

            if (!_dialects.ContainsKey(dialectFileName))
            {
                using var reader = new StreamReader(dialectPath);
                 
                var xmlContent = reader.ReadToEnd();

                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xmlContent));

                var serializer = new XmlSerializer(typeof(MavLink));

                var dialect = (MavLink)serializer.Deserialize(stream);

                foreach (var include in dialect.Includes)
                {
                    Deserialize(include);
                }

                TransformMessageExtensions(xmlContent, dialect);

                _dialects[dialectFileName] = dialect;
            }

            return _dialects;
        }

        private static void TransformMessageExtensions(string xmlContent, MavLink dialect)
        {
            var xmldoc = new XmlDocument();

            xmldoc.LoadXml(xmlContent);

            var messageNodes = xmldoc.GetElementsByTagName("message");

            foreach(XmlNode messageNode in messageNodes)
            {
                var extensionElementFound = false;

                for (var i = 0; i < messageNode.ChildNodes.Count; i++)
                {
                    if (extensionElementFound)
                    {
                        var messageName = messageNode.Attributes["name"].Value;

                        var message = dialect.Messages.Single(x => x.Name == messageName);

                        var extendedFieldName = messageNode.ChildNodes[i].Attributes["name"].Value;

                        var field = message.Fields.Single(x => x.Name == extendedFieldName);

                        field.Extended = true;
                    }
                    else if (messageNode.ChildNodes[i].Name == "extensions")
                    {
                        extensionElementFound= true;
                    }
                }
            }
        }

        /// <summary>
        /// Include the message IDs for parsing.
        /// </summary>
        /// <param name="messageIds">An array of unsigned integers representing the message IDs to be included.</param>
        /// <exception cref="ArgumentException">Thrown when one or more message ID(s) not valid.</exception>
        /// <seealso cref="ExcludeMessages(uint[])"/>
        public static void IncludeMessages(params uint[] messageIds)
        {
            var invalidIds = messageIds.Where(id => !Metadata.Messages.ContainsKey(id));

            if (invalidIds.Any())
            {
                throw new ArgumentException($"Invalid message ID(s): {string.Join(", ", invalidIds)}.");
            }

            if (messageIds.Length == 0)
            {
                foreach (var (_, message) in Metadata.Messages)
                {
                    message.Include();
                }
            }
            else
            {
                foreach (var messageId in messageIds)
                {
                    Metadata.Messages[messageId].Include();
                }
            }
        }

        /// <summary>
        /// Exclude the message IDs from parsing.
        /// </summary>
        /// <param name="messageIds">An array of unsigned integers representing the message IDs to be excluded.</param>
        /// <exception cref="ArgumentException">Thrown when one or more message ID(s) not valid.</exception>
        /// <seealso cref="IncludeMessages(uint[])"/>
        public static void ExcludeMessages(params uint[] messageIds)
        {
            var invalidIds = messageIds.Where(id => !Metadata.Messages.ContainsKey(id));

            if (invalidIds.Any())
            {
                throw new ArgumentException($"Invalid message ID(s): {string.Join(", ", invalidIds)}.");
            }

            foreach (var messageId in messageIds)
            {
                Metadata.Messages[messageId].Exclude();
            }
        }
    }
}