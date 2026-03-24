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
    /// Represents a MAVLink dialect context, holding message and enum metadata.
    /// Use multiple contexts to handle different dialects simultaneously.
    /// </summary>
    public class MavLinkContext
    {
        private readonly Dictionary<string, MavLink> _dialects = new Dictionary<string, MavLink>();
        
        /// <summary>
        /// The message and enum metadata for this context.
        /// </summary>
        public Metadata Metadata { get; } = new Metadata();

        /// <summary>
        /// Indicates whether this context has been initialized.
        /// </summary>
        public bool IsInitialized { get; private set; }

        /// <summary>
        /// The global default context for backward compatibility.
        /// </summary>
        public static MavLinkContext Default { get; } = new MavLinkContext();

        /// <summary>
        /// Throws an exception if the context has not been initialized.
        /// </summary>
        public void ThrowIfNotInitialized()
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException("MavLink context must be initialized before use.");
            }
        }

        /// <summary>
        /// Initializes the context using the given dialect type.
        /// </summary>
        /// <param name="dialectType">The type of the dialect to initialize.</param>
        /// <param name="messageIds">Optional. A list of message IDs to include for parsing. If empty, all messages from the dialect are included.</param>
        public void Initialize(DialectType dialectType, params uint[] messageIds)
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
        /// Initializes the context using the given dialect path.
        /// </summary>
        /// <param name="dialectPath">The path to the main dialect file.</param>
        /// <param name="messageIds">Optional. A list of message IDs to include for parsing. If empty, all messages from the dialect are included.</param>
        public void Initialize(string dialectPath = "common.xml", params uint[] messageIds)
        {
            var dialects = Deserialize(dialectPath);
            Metadata.Initialize(dialects);
            IncludeMessages(messageIds);
            IsInitialized = true;
        }

        private Dictionary<string, MavLink> Deserialize(string dialectPath)
        {
            if (!File.Exists(dialectPath))
            {
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

        private void TransformMessageExtensions(string xmlContent, MavLink dialect)
        {
            var xmldoc = new XmlDocument();
            xmldoc.LoadXml(xmlContent);
            var messageNodes = xmldoc.GetElementsByTagName("message");

            foreach (XmlNode messageNode in messageNodes)
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
                        extensionElementFound = true;
                    }
                }
            }
        }

        /// <summary>
        /// Include specific message IDs for parsing in this context.
        /// </summary>
        public void IncludeMessages(params uint[] messageIds)
        {
            var invalidIds = messageIds.Where(id => !Metadata.MessagesDictionary.ContainsKey(id));
            if (invalidIds.Any())
            {
                throw new ArgumentException($"Invalid message ID(s): {string.Join(", ", invalidIds)}.");
            }

            if (messageIds.Length == 0)
            {
                foreach (var (_, message) in Metadata.MessagesDictionary)
                {
                    message.Include();
                }
            }
            else
            {
                foreach (var messageId in messageIds)
                {
                    Metadata.MessagesDictionary[messageId].Include();
                }
            }
        }

        /// <summary>
        /// Exclude specific message IDs from parsing in this context.
        /// </summary>
        public void ExcludeMessages(params uint[] messageIds)
        {
            var invalidIds = messageIds.Where(id => !Metadata.MessagesDictionary.ContainsKey(id));
            if (invalidIds.Any())
            {
                throw new ArgumentException($"Invalid message ID(s): {string.Join(", ", invalidIds)}.");
            }

            foreach (var messageId in messageIds)
            {
                Metadata.MessagesDictionary[messageId].Exclude();
            }
        }
    }
}
