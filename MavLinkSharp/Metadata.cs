using System.Collections.Generic;

namespace MavLinkSharp
{
    /// <summary>
    /// Serves as a repository for MAVLink enumeration, command, and message definitions.
    /// </summary>
    public class Metadata
    {
        /// <summary>
        /// Gets the metadata from the global default context.
        /// </summary>
        public static Metadata Default => MavLinkContext.Default.Metadata;

        /// <summary>
        /// Collection of enumerations defined in given dialects in the default context.
        /// </summary>
        public static Dictionary<string, Enum> Enums => Default.EnumsDictionary;

        /// <summary>
        /// Collection of entries defined in given dialects in the default context.
        /// </summary>
        public static Dictionary<long, Entry> Commands => Default.CommandsDictionary;

        /// <summary>
        /// Collection of messages defined in given dialects in the default context.
        /// </summary>
        public static Dictionary<uint, Message> Messages => Default.MessagesDictionary;

        /// <summary>
        /// Instance collection of enumerations.
        /// </summary>
        public Dictionary<string, Enum> EnumsDictionary { get; } = new Dictionary<string, Enum>();

        /// <summary>
        /// Instance collection of entries.
        /// </summary>
        public Dictionary<long, Entry> CommandsDictionary { get; } = new Dictionary<long, Entry>();

        /// <summary>
        /// Instance collection of messages.
        /// </summary>
        public Dictionary<uint, Message> MessagesDictionary { get; } = new Dictionary<uint, Message>();

        internal void Initialize(Dictionary<string, MavLink> dialects)
        {
            foreach (var (name, dialect) in dialects)
            {
                // Enums
                var enums = dialect.Enums.FindAll(x => x.Name != "MAV_CMD");

                foreach (var @enum in enums)
                {
                    EnumsDictionary[@enum.Name] = @enum;
                }

                // Commands
                var cmds = dialect.Enums.FindAll(x => x.Name == "MAV_CMD");

                foreach (var cmd in cmds)
                {
                    foreach (var entry in cmd.Entries)
                    {
                        CommandsDictionary[entry.Value] = entry;
                    }
                }

                // Messages
                foreach (var message in dialect.Messages)
                {
                    message.PayloadLength = 0;
                    message.MaxPayloadLength = 0;
                    foreach (var field in message.Fields)
                    {
                        field.SetDataType();
                        field.SetOrdinal();
                        field.SetLength();

                        if (!field.Extended)
                        {
                            message.PayloadLength += field.Length;
                        }
                        message.MaxPayloadLength += field.Length;
                    }

                    message.SetOrderedFields();

                    int currentOffset = 0;
                    foreach (var field in message.OrderedFields)
                    {
                        field.Offset = currentOffset;
                        currentOffset += field.Length;
                    }

                    message.SetCrcExtra();

                    MessagesDictionary[message.Id] = message;
                }
            }
        }
    }
}
