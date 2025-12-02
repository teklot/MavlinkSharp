using System.Collections.Generic;
using System.Linq;

namespace MavLinkSharp
{
    /// <summary>
    /// Serves as a static repository for MAVLink enumeration, command, and message definitions.
    /// These collections are populated during the library's initialization from dialect files.
    /// </summary>
    public class Metadata
    {
        /// <summary>
        /// Collection of enumerations defined in given dialects.
        /// </summary>
        public static Dictionary<string, Enum> Enums { get; } = new Dictionary<string, Enum>();

        /// <summary>
        /// Collection of entries defined in given dialects.
        /// </summary>
        public static Dictionary<long, Entry> Commands { get; } = new Dictionary<long, Entry>();

        /// <summary>
        /// Collection of messages defined in given dialects.
        /// </summary>
        public static Dictionary<uint, Message> Messages { get; } = new Dictionary<uint, Message>();

        /// <summary>
        /// Initializes the static metadata collections (<see cref="Enums"/>, <see cref="Commands"/>, <see cref="Messages"/>)
        /// by processing the provided MAVLink dialects.
        /// </summary>
        /// <param name="dialects">A dictionary of MAVLink dialect names to their parsed <see cref="MavLink"/> objects.</param>
        internal static void Initialize(Dictionary<string, MavLink> dialects)
        {
            foreach (var (name, dialect) in dialects)
            {
                // Enums
                var enums = dialect.Enums.FindAll(x => x.Name != "MAV_CMD");

                foreach (var @enum in enums)
                {
                    Enums[@enum.Name] = @enum;
                }

                // Commands
                var cmds = dialect.Enums.FindAll(x => x.Name == "MAV_CMD");

                foreach (var cmd in cmds)
                {
                    foreach (var entry in cmd.Entries)
                    {
                        Commands[entry.Value] = entry;
                    }
                }

                // Messages
                foreach (var message in dialect.Messages)
                {
                    foreach (var field in message.Fields)
                    {
                        field.SetDataType();
                        field.SetOrdinal();
                        field.SetLength();

                        message.PayloadLength += field.Length;
                    }

                    message.SetOrderedFields();
                    message.SetCrcExtra();

                    Messages[message.Id] = message;
                }
            }
        }
    }
}
