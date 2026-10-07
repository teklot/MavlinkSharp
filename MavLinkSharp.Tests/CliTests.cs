using System.Text.Json;
using MavLinkSharp;
using MavLinkSharp.Cli.Cli;
using MavLinkSharp.Enums;

namespace MavLinkSharp.Tests
{
    public class CliTests : IDisposable
    {
        private readonly string _directory;

        public CliTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), "mavlinksharp-cli-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }

            GC.SuppressFinalize(this);
        }

        #region WildcardMatcher

        [Theory]
        [InlineData("*", "ATTITUDE", true)]
        [InlineData("ATTITUDE", "ATTITUDE", true)]
        [InlineData("attitude", "ATTITUDE", true)]
        [InlineData("ATTITUDE", "attitude", true)]
        [InlineData("MSG_*", "MSG_WAYPOINT", true)]
        [InlineData("MSG_*", "ATTITUDE", false)]
        [InlineData("*_STATUS", "BATTERY_STATUS", true)]
        [InlineData("*_STATUS", "SYSTEM_STATUS", true)]
        [InlineData("*STATUS*", "BATTERY_STATUS", true)]
        [InlineData("ATT?TUDE", "ATTITUDE", true)]
        [InlineData("ATT?TUDE", "ATTITUDE_EXTRA", false)]
        [InlineData("ATTITUDE", "ATTITUDE_EXTRA", false)]
        [InlineData("**", "ATTITUDE", true)]
        [InlineData("", "ATTITUDE", true)]
        public void WildcardMatcher_IsMatch_MatchesExpected(string pattern, string value, bool expected)
        {
            Assert.Equal(expected, WildcardMatcher.IsMatch(pattern, value));
        }

        [Fact]
        public void WildcardMatcher_IsMatch_NullValue_OnlyMatchesStar()
        {
            Assert.True(WildcardMatcher.IsMatch("*", null));
            Assert.False(WildcardMatcher.IsMatch("ATTITUDE", null));
        }

        [Theory]
        [InlineData("ATTITUDE", false)]
        [InlineData("MSG_*", true)]
        [InlineData("ATT?TUDE", true)]
        public void WildcardMatcher_ContainsWildcards_DetectsWildcards(string pattern, bool expected)
        {
            Assert.Equal(expected, WildcardMatcher.ContainsWildcards(pattern));
        }

        #endregion

        #region PacketFilter

        [Fact]
        public void PacketFilter_NoExpressions_LetsEverythingThrough()
        {
            var filter = PacketFilter.Parse(null);

            Assert.True(filter.IsEmpty);
            Assert.True(filter.IsMatch(0, "HEARTBEAT"));
        }

        [Fact]
        public void PacketFilter_MessageId_SelectsSingleId()
        {
            var filter = PacketFilter.Parse(new[] { "30" });

            Assert.True(filter.IsMatch(30, "ATTITUDE"));
            Assert.False(filter.IsMatch(0, "HEARTBEAT"));
        }

        [Fact]
        public void PacketFilter_MessageIdRange_SelectsInclusiveRange()
        {
            var filter = PacketFilter.Parse(new[] { "20-30" });

            Assert.True(filter.IsMatch(20, "PARAM_REQUEST_READ"));
            Assert.True(filter.IsMatch(30, "ATTITUDE"));
            Assert.False(filter.IsMatch(19, "HEARTBEAT"));
            Assert.False(filter.IsMatch(31, "ATTITUDE"));
        }

        [Fact]
        public void PacketFilter_ReversedRange_CollapsesToSingleId()
        {
            var filter = PacketFilter.Parse(new[] { "30-20" });

            Assert.True(filter.IsMatch(30, "ATTITUDE"));
            Assert.False(filter.IsMatch(25, "ATTITUDE"));
        }

        [Fact]
        public void PacketFilter_WildcardName_SelectsByName()
        {
            var filter = PacketFilter.Parse(new[] { "PARAM_*" });

            Assert.True(filter.IsMatch(20, "PARAM_REQUEST_READ"));
            Assert.False(filter.IsMatch(0, "HEARTBEAT"));
        }

        [Fact]
        public void PacketFilter_NegatedTerm_WinsOverPositiveTerm()
        {
            var filter = PacketFilter.Parse(new[] { "*", "!PARAM_*" });

            Assert.True(filter.IsMatch(0, "HEARTBEAT"));
            Assert.False(filter.IsMatch(20, "PARAM_REQUEST_READ"));
        }

        [Fact]
        public void PacketFilter_ExcludePrefix_IsNegated()
        {
            var filter = PacketFilter.Parse(new[] { "exclude:HEARTBEAT" });

            Assert.False(filter.IsMatch(0, "HEARTBEAT"));
            Assert.True(filter.IsMatch(30, "ATTITUDE"));
        }

        [Fact]
        public void PacketFilter_MultipleTerms_RequireOnePositiveMatch()
        {
            var filter = PacketFilter.Parse(new[] { "HEARTBEAT", "ATTITUDE" });

            Assert.True(filter.IsMatch(0, "HEARTBEAT"));
            Assert.True(filter.IsMatch(30, "ATTITUDE"));
            Assert.False(filter.IsMatch(1, "SYS_STATUS"));
        }

        [Fact]
        public void PacketFilter_InvalidExpression_Throws()
        {
            Assert.Throws<ArgumentException>(() => PacketFilter.Parse(new[] { "!" }));
        }

        #endregion

        #region DialectResolver

        [Theory]
        [InlineData(null, "common")]
        [InlineData("", "common")]
        [InlineData("  ", "common")]
        [InlineData("ardupilotmega", "ardupilotmega")]
        [InlineData(" common ", "common")]
        public void DialectResolver_Normalize_AppliesDefaultAndTrim(string? dialect, string expected)
        {
            Assert.Equal(expected, DialectResolver.Normalize(dialect));
        }

        [Fact]
        public void DialectResolver_TryResolveDialectType_MapsNamesButRejectsNumbers()
        {
            Assert.True(DialectResolver.TryResolveDialectType("ardupilotmega", out var dialectType));
            Assert.Equal(DialectType.Ardupilotmega, dialectType);

            Assert.False(DialectResolver.TryResolveDialectType("0", out _));
            Assert.False(DialectResolver.TryResolveDialectType("not-a-dialect", out _));
        }

        [Fact]
        public void DialectResolver_Resolve_ReturnsInitializedContext()
        {
            MavLinkContext context = DialectResolver.Resolve("common");

            Assert.NotNull(context.Metadata.MessagesDictionary[0]);
            Assert.Equal("HEARTBEAT", context.Metadata.MessagesDictionary[0].Name);
        }

        [Fact]
        public void DialectResolver_GetBuiltInDialects_IsSortedAndNonEmpty()
        {
            string[] dialects = DialectResolver.GetBuiltInDialects();

            Assert.NotEmpty(dialects);
            Assert.Equal(dialects.OrderBy(name => name, StringComparer.Ordinal).ToArray(), dialects);
        }

        #endregion

        #region PacketFormatter

        [Fact]
        public void FormatValue_NegativeZero_RendersWithoutSign()
        {
            Assert.Equal("0", PacketFormatter.FormatValue(-0.0f));
            Assert.Equal("0", PacketFormatter.FormatValue(-0.0d));
        }

        [Fact]
        public void FormatValue_Integers_UseInvariantFormatting()
        {
            Assert.Equal("42", PacketFormatter.FormatValue(42));
            Assert.Equal("4294967295", PacketFormatter.FormatValue(uint.MaxValue));
        }

        [Fact]
        public void FormatValue_ByteArray_RendersAsHex()
        {
            Assert.Equal("0A0B0C", PacketFormatter.FormatValue(new byte[] { 10, 11, 12 }));
        }

        #endregion

        #region Trace round-trips

        [Fact]
        public void JsonLines_RoundTrip_PreservesFieldsAndRawBytes()
        {
            string path = Path.Combine(_directory, "trace.jsonl");
            MavLinkContext context = DialectResolver.Resolve("common");

            byte[] raw = CreateAttitude(context, 0);

            using (TraceWriter writer = TraceWriter.Create(path, "common", context, includeRaw: true))
            {
                Assert.False(writer.IsRaw);
                Assert.True(writer.IncludeRaw);

                for (byte sequence = 0; sequence < 3; sequence++)
                {
                    writer.Write(CreateRecord(context, sequence, raw));
                }
            }

            Assert.Equal("common", TraceReader.ReadDialect(path));

            List<PacketRecord> records = TraceReader.ReadJsonLines(path, context);

            Assert.Equal(3, records.Count);

            for (var i = 0; i < records.Count; i++)
            {
                Assert.Equal((uint)30, records[i].MessageId);
                Assert.Equal("ATTITUDE", records[i].Name);
                Assert.Equal((byte)i, records[i].Sequence);
                Assert.Equal(2, records[i].Version);
                Assert.NotNull(records[i].Raw);
                Assert.Equal(raw, records[i].Raw);
                Assert.Equal(1000u, records[i].Fields["time_boot_ms"]);
            }
        }

        [Fact]
        public void JsonLines_WithoutRawBytes_LeavesRawNull()
        {
            string path = Path.Combine(_directory, "no-raw.jsonl");
            MavLinkContext context = DialectResolver.Resolve("common");

            using (TraceWriter writer = TraceWriter.Create(path, "common", context, includeRaw: false))
            {
                writer.Write(CreateRecord(context, 0, raw: null));
            }

            List<PacketRecord> records = TraceReader.ReadJsonLines(path, context);

            Assert.Single(records);
            Assert.Null(records[0].Raw);
        }

        [Fact]
        public void RawTrace_RoundTrip_WritesPacketBytesOnly()
        {
            string path = Path.Combine(_directory, "trace.raw");
            MavLinkContext context = DialectResolver.Resolve("common");

            byte[] first = CreateHeartbeat(context, 0);
            byte[] second = CreateHeartbeat(context, 1);

            using (TraceWriter writer = TraceWriter.Create(path, "common", context, includeRaw: false))
            {
                Assert.True(writer.IsRaw);
                Assert.True(writer.IncludeRaw);

                writer.Write(CreateRecord(context, 0, first));
                writer.Write(CreateRecord(context, 1, second));
            }

            byte[] expected = first.Concat(second).ToArray();
            Assert.Equal(expected, File.ReadAllBytes(path));
        }

        [Fact]
        public void ReadRaw_CleanTrace_DecodesEveryFrameInOrder()
        {
            string path = Path.Combine(_directory, "clean.raw");
            MavLinkContext context = DialectResolver.Resolve("common");

            var packets = new List<byte[]>();
            for (byte sequence = 0; sequence < 4; sequence++)
            {
                packets.Add(sequence % 2 == 0 ? CreateHeartbeat(context, sequence) : CreateAttitude(context, sequence));
            }

            File.WriteAllBytes(path, packets.SelectMany(p => p).ToArray());

            var names = new List<string>();
            var errors = new List<RawTraceError>();

            long frames = TraceReader.ReadRaw(path, context, frame =>
            {
                names.Add(Assert.IsType<Message>(frame.Message).Name);
                return true;
            }, errors.Add, TestContext.Current.CancellationToken);

            Assert.Equal(4, frames);
            Assert.Equal(new[] { "HEARTBEAT", "ATTITUDE", "HEARTBEAT", "ATTITUDE" }, names);
            Assert.Empty(errors);
        }

        [Fact]
        public void ReadRaw_UndecodableFrame_ReportsOneRegionWithExactBounds()
        {
            string path = Path.Combine(_directory, "corrupt.raw");
            MavLinkContext context = DialectResolver.Resolve("common");

            byte[] heartbeat = CreateHeartbeat(context, 0);
            byte[] corrupt = CreateAttitude(context, 1);
            byte[] following = CreateHeartbeat(context, 2);

            // Break the message id so the frame cannot decode while its advertised length stays plausible.
            corrupt[Protocol.V2.OffsetMessageId + 2] = 0xAA;

            var data = new List<byte>();
            data.AddRange(heartbeat);
            var corruptStart = data.Count;
            data.AddRange(corrupt);
            data.AddRange(following);

            File.WriteAllBytes(path, data.ToArray());

            var names = new List<string>();
            var errors = new List<RawTraceError>();

            long frames = TraceReader.ReadRaw(path, context, frame =>
            {
                names.Add(Assert.IsType<Message>(frame.Message).Name);
                return true;
            }, errors.Add, TestContext.Current.CancellationToken);

            Assert.Equal(2, frames);
            Assert.Equal(new[] { "HEARTBEAT", "HEARTBEAT" }, names);

            var error = Assert.Single(errors);
            Assert.Equal(ErrorReason.MessageNotFound, error.Reason);
            Assert.Equal(corruptStart, error.Offset);
            Assert.Equal(corrupt.Length, error.Skipped);
        }

        [Fact]
        public void ReadRaw_TruncatedTail_ReportsRemainderAndStops()
        {
            string path = Path.Combine(_directory, "truncated.raw");
            MavLinkContext context = DialectResolver.Resolve("common");

            byte[] heartbeat = CreateHeartbeat(context, 0);
            byte[] truncated = CreateAttitude(context, 1).Take(20).ToArray();

            File.WriteAllBytes(path, heartbeat.Concat(truncated).ToArray());

            var errors = new List<RawTraceError>();
            long frames = TraceReader.ReadRaw(path, context, _ => true, errors.Add, TestContext.Current.CancellationToken);

            Assert.Equal(1, frames);

            var error = Assert.Single(errors);
            Assert.Equal(ErrorReason.PayloadLengthInvalid, error.Reason);
            Assert.Equal(heartbeat.Length, error.Offset);
        }

        [Fact]
        public void ReadRaw_StopsWhenCallbackReturnsFalse()
        {
            string path = Path.Combine(_directory, "stop.raw");
            MavLinkContext context = DialectResolver.Resolve("common");

            byte[] first = CreateHeartbeat(context, 0);
            byte[] second = CreateHeartbeat(context, 1);
            byte[] third = CreateHeartbeat(context, 2);

            File.WriteAllBytes(path, first.Concat(second).Concat(third).ToArray());

            var seen = 0;

            long frames = TraceReader.ReadRaw(path, context, _ =>
            {
                seen++;
                return seen < 2;
            }, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(2, frames);
        }

        [Fact]
        public void ReadRaw_SignedFrame_ConsumesSignature()
        {
            string path = Path.Combine(_directory, "signed.raw");
            MavLinkContext context = DialectResolver.Resolve("common");

            byte[] signed = CreateSignedHeartbeat(context);
            byte[] heartbeat = CreateHeartbeat(context, 4);

            File.WriteAllBytes(path, signed.Concat(heartbeat).ToArray());

            var names = new List<string>();
            var errors = new List<RawTraceError>();

            long frames = TraceReader.ReadRaw(path, context, frame =>
            {
                names.Add(Assert.IsType<Message>(frame.Message).Name);
                return true;
            }, errors.Add, TestContext.Current.CancellationToken);

            Assert.Equal(2, frames);
            Assert.Equal(new[] { "HEARTBEAT", "HEARTBEAT" }, names);
            Assert.Empty(errors);
        }

        #endregion

        #region PacketRecord

        [Fact]
        public void PacketRecord_FromFrame_CopiesIdentityAndFields()
        {
            MavLinkContext context = DialectResolver.Resolve("common");

            using var frame = new Frame { Context = context, StartMarker = Protocol.V2.StartMarker };
            frame.Message = context.Metadata.MessagesDictionary[30];
            frame.MessageId = frame.Message.Id;
            frame.SystemId = 1;
            frame.ComponentId = 1;
            frame.PacketSequence = 7;
            frame.SetFields(new Dictionary<string, object>
            {
                { "time_boot_ms", (uint)1234 },
                { "roll", 1.5f },
                { "pitch", 0f },
                { "yaw", 0f },
                { "rollspeed", 0f },
                { "pitchspeed", 0f },
                { "yawspeed", 0f }
            });

            PacketRecord record = PacketRecord.FromFrame(frame, includeRaw: false);

            Assert.Equal((uint)30, record.MessageId);
            Assert.Equal("ATTITUDE", record.Name);
            Assert.Equal((byte)7, record.Sequence);
            Assert.Equal((byte)1, record.SystemId);
            Assert.Equal(2, record.Version);
            Assert.Null(record.Raw);
            Assert.Equal(1234u, record.Fields["time_boot_ms"]);

            record.Fields["roll"] = 99f;
            Assert.NotEqual(99f, frame.Fields["roll"]);
        }

        [Fact]
        public void PacketRecord_FromFrame_WithRaw_ProducesParseableBytes()
        {
            MavLinkContext context = DialectResolver.Resolve("common");

            using var frame = new Frame { Context = context, StartMarker = Protocol.V2.StartMarker };
            frame.Message = context.Metadata.MessagesDictionary[30];
            frame.MessageId = frame.Message.Id;
            frame.SetFields(new Dictionary<string, object>
            {
                { "time_boot_ms", (uint)99 },
                { "roll", 0f },
                { "pitch", 0f },
                { "yaw", 0f },
                { "rollspeed", 0f },
                { "pitchspeed", 0f },
                { "yawspeed", 0f }
            });

            PacketRecord record = PacketRecord.FromFrame(frame, includeRaw: true);

            Assert.NotNull(record.Raw);

            using var reparsed = new Frame { Context = context };
            Assert.True(reparsed.TryParse(record.Raw.AsSpan()));
            Assert.Equal((uint)30, reparsed.MessageId);
        }

        #endregion

        #region Header parsing

        [Fact]
        public void ParseRecord_HeaderLine_ReturnsNull()
        {
            Assert.Null(TraceReader.ParseRecord(
                "{\"record\":\"header\",\"formatVersion\":1}",
                DialectResolver.Resolve("common")));
        }
        [Fact]
        public void ParseRecord_MissingOptionalValues_FallsBackToDefaults()
        {
            PacketRecord? record = TraceReader.ParseRecord("{\"messageId\":0}", DialectResolver.Resolve("common"));

            Assert.NotNull(record);
            Assert.Equal(0u, record.MessageId);
            Assert.Equal("HEARTBEAT", record.Name);
            Assert.Equal(default, record.TimestampUtc);
            Assert.Null(record.Raw);
        }

        #endregion

        #region Helpers

        private static PacketRecord CreateRecord(MavLinkContext context, byte sequence, byte[]? raw)
        {
            uint messageId = raw is null ? 0u : 30u;

            var fields = raw is null
                ? new Dictionary<string, object>(StringComparer.Ordinal)
                : new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "time_boot_ms", 1000u },
                    { "roll", 0f },
                    { "pitch", 0f },
                    { "yaw", 0.5f },
                    { "rollspeed", 0f },
                    { "pitchspeed", 0f },
                    { "yawspeed", 0f }
                };

            return new PacketRecord
            {
                TimestampUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(sequence),
                Version = 2,
                MessageId = messageId,
                Name = messageId == 0 ? "HEARTBEAT" : "ATTITUDE",
                Sequence = sequence,
                SystemId = 1,
                ComponentId = 1,
                PayloadLength = 9,
                Checksum = 0x1234,
                Fields = fields,
                Raw = raw
            };
        }

        private static byte[] CreateHeartbeat(MavLinkContext context, byte sequence)
        {
            using var frame = new Frame { Context = context, StartMarker = Protocol.V2.StartMarker };
            frame.Message = context.Metadata.MessagesDictionary[0];
            frame.MessageId = frame.Message.Id;
            frame.SystemId = 1;
            frame.ComponentId = 1;
            frame.PacketSequence = sequence;
            frame.SetFields(new Dictionary<string, object>
            {
                { "type", (byte)8 },
                { "mavlink_version", (byte)3 }
            });

            return frame.ToBytes();
        }

        private static byte[] CreateAttitude(MavLinkContext context, byte sequence)
        {
            using var frame = new Frame { Context = context, StartMarker = Protocol.V2.StartMarker };
            frame.Message = context.Metadata.MessagesDictionary[30];
            frame.MessageId = frame.Message.Id;
            frame.SystemId = 1;
            frame.ComponentId = 1;
            frame.PacketSequence = sequence;
            frame.SetFields(new Dictionary<string, object>
            {
                { "time_boot_ms", (uint)1000 },
                { "roll", 0f },
                { "pitch", 0f },
                { "yaw", 0.5f },
                { "rollspeed", 0f },
                { "pitchspeed", 0f },
                { "yawspeed", 0f }
            });

            return frame.ToBytes();
        }

        private static byte[] CreateSignedHeartbeat(MavLinkContext context)
        {
            using var frame = new Frame
            {
                Context = context,
                StartMarker = Protocol.V2.StartMarker,
                Signing = new MavLinkSigning("0123456789abcdef0123456789abcdef"),
                SystemId = 1,
                ComponentId = 1,
                PacketSequence = 3
            };

            frame.Message = context.Metadata.MessagesDictionary[0];
            frame.MessageId = frame.Message.Id;
            frame.SetFields(new Dictionary<string, object>
            {
                { "type", (byte)8 },
                { "mavlink_version", (byte)3 }
            });

            return frame.ToBytes();
        }

        #endregion
    }
}