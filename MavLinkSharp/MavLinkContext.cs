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
#if NET10_0_OR_GREATER
                var dialect = LoadDialectAot(dialectPath);
#else
                using var reader = new StreamReader(dialectPath);
                var xmlContent = reader.ReadToEnd();
                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xmlContent));
                var serializer = new XmlSerializer(typeof(MavLink));
                var dialect = (MavLink)serializer.Deserialize(stream);
                TransformMessageExtensions(xmlContent, dialect);
#endif

                foreach (var include in dialect.Includes)
                {
                    Deserialize(include);
                }

                _dialects[dialectFileName] = dialect;
            }

            return _dialects;
        }

#if NET10_0_OR_GREATER
        private MavLink LoadDialectAot(string path)
        {
            var dialect = new MavLink();
            using var reader = XmlReader.Create(path);
            
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;

                switch (reader.Name)
                {
                    case "include":
                        dialect.Includes.Add(reader.ReadElementContentAsString());
                        break;
                    case "version":
                        dialect.Version = reader.ReadElementContentAsString();
                        break;
                    case "dialect":
                        dialect.Dialect = reader.ReadElementContentAsString();
                        break;
                    case "enum":
                        dialect.Enums.Add(ReadEnum(reader));
                        break;
                    case "message":
                        dialect.Messages.Add(ReadMessage(reader));
                        break;
                }
            }
            return dialect;
        }

        private Enum ReadEnum(XmlReader reader)
        {
            var @enum = new Enum { Name = reader.GetAttribute("name") };
            var bitmask = reader.GetAttribute("bitmask");
            if (bool.TryParse(bitmask, out var isBitmask)) @enum.Bitmask = isBitmask;

            if (reader.IsEmptyElement) return @enum;

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "enum") break;
                if (reader.NodeType != XmlNodeType.Element) continue;

                switch (reader.Name)
                {
                    case "description":
                        @enum.Description = reader.ReadElementContentAsString();
                        break;
                    case "entry":
                        @enum.Entries.Add(ReadEntry(reader));
                        break;
                }
            }
            return @enum;
        }

        private Entry ReadEntry(XmlReader reader)
        {
            var entry = new Entry { Name = reader.GetAttribute("name") };
            var valStr = reader.GetAttribute("value");
            if (long.TryParse(valStr, out var val)) entry.Value = val;

            if (reader.IsEmptyElement) return entry;

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "entry") break;
                if (reader.NodeType != XmlNodeType.Element) continue;

                if (reader.Name == "description")
                    entry.Description = reader.ReadElementContentAsString();
            }
            return entry;
        }

        private Message ReadMessage(XmlReader reader)
        {
            var msg = new Message { Name = reader.GetAttribute("name") };
            var idStr = reader.GetAttribute("id");
            if (uint.TryParse(idStr, out var id)) msg.Id = id;

            if (reader.IsEmptyElement) return msg;

            bool inExtensions = false;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "message") break;
                if (reader.NodeType != XmlNodeType.Element) continue;

                switch (reader.Name)
                {
                    case "description":
                        msg.Description = reader.ReadElementContentAsString();
                        break;
                    case "extensions":
                        inExtensions = true;
                        break;
                    case "field":
                        var field = ReadField(reader);
                        field.Extended = inExtensions;
                        msg.Fields.Add(field);
                        break;
                }
            }
            return msg;
        }

        private Field ReadField(XmlReader reader)
        {
            var field = new Field
            {
                Name = reader.GetAttribute("name"),
                Type = reader.GetAttribute("type"),
                Enum = reader.GetAttribute("enum"),
                Units = reader.GetAttribute("units"),
                Display = reader.GetAttribute("display"),
                PrintFormat = reader.GetAttribute("print_format"),
                Default = reader.GetAttribute("default"),
                Invalid = reader.GetAttribute("invalid")
            };
            
            var instance = reader.GetAttribute("instance");
            if (bool.TryParse(instance, out var isInstance)) field.Instance = isInstance;

            if (reader.IsEmptyElement) return field;
            
            // Just move to the end of the field tag without consuming next elements
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "field") break;
                if (reader.NodeType == XmlNodeType.Text) field.TagBody = reader.Value;
            }
            return field;
        }
#endif

#if !NET10_0_OR_GREATER
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
#endif

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
