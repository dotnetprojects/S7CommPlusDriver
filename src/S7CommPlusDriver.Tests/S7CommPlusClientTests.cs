using S7CommPlusDriver.ClientApi;
using S7CommPlusDriver.Alarming;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class S7CommPlusClientTests
    {
        [Fact]
        public async Task ConnectBrowseAndReadSucceeds()
        {
            var fake = new FakeS7CommPlusSession
            {
                BrowseVariablesHandler = () => (0, new List<VarInfo> { new VarInfo { Name = "DB.Value" } })
            };
            var client = CreateClient(fake);

            await client.ConnectAsync();
            var vars = await client.BrowseAsync();
            var read = await client.ReadAsync(new[] { new ItemAddress("8A0E0001.F") });

            Assert.Equal(S7CommPlusConnectionState.Connected, client.State);
            Assert.Single(vars);
            Assert.Single(read.Items);
            Assert.True(read.Items[0].IsSuccess);
        }

        [Fact]
        public async Task DisconnectIsIdempotent()
        {
            var fake = new FakeS7CommPlusSession();
            var client = CreateClient(fake);

            await client.DisconnectAsync();
            await client.ConnectAsync();
            await client.DisconnectAsync();
            await client.DisconnectAsync();

            Assert.Equal(S7CommPlusConnectionState.Disconnected, client.State);
            Assert.Equal(1, fake.DisconnectCount);
        }

        [Fact]
        public async Task RequestTimeoutIsTypedFailure()
        {
            var fake = new FakeS7CommPlusSession
            {
                BrowseVariablesHandler = () =>
                {
                    Task.Delay(200).Wait();
                    return (0, new List<VarInfo>());
                }
            };
            var client = CreateClient(fake, requestTimeoutMs: 20);

            var ex = await Assert.ThrowsAsync<S7CommPlusTimeoutException>(() => client.BrowseAsync());

            Assert.Equal("Browse", ex.Operation);
            Assert.True(ex.IsTransient);
        }

        [Fact]
        public async Task ReadReconnectsOnceAfterTransientDisconnect()
        {
            var fake = new FakeS7CommPlusSession();
            fake.ReadHandler = _ =>
            {
                if (fake.ReadCount == 1)
                {
                    return (S7Consts.errTCPDataReceive, new List<object?>(), new List<ulong>());
                }
                return (0, new List<object?> { new ValueInt(7) }, new List<ulong> { 0 });
            };
            var client = CreateClient(fake);

            var result = await client.ReadAsync(new[] { new ItemAddress("8A0E0001.F") });

            Assert.Equal(2, fake.ConnectCount);
            Assert.Equal(2, fake.ReadCount);
            Assert.True(result.Items[0].IsSuccess);
        }

        [Fact]
        public async Task ProtocolErrorDoesNotReconnect()
        {
            var fake = new FakeS7CommPlusSession
            {
                ReadHandler = _ => (S7Consts.errIsoInvalidPDU3, new List<object?>(), new List<ulong>())
            };
            var client = CreateClient(fake);

            var ex = await Assert.ThrowsAsync<S7CommPlusConnectionException>(() => client.ReadAsync(new[] { new ItemAddress("8A0E0001.F") }));

            Assert.Equal(S7Consts.errIsoInvalidPDU3, ex.ErrorCode);
            Assert.False(ex.IsTransient);
            Assert.Equal(1, fake.ConnectCount);
        }

        [Fact]
        public async Task PartialBatchReadReturnsPerItemErrors()
        {
            var fake = new FakeS7CommPlusSession
            {
                ReadHandler = addresses => (0,
                    new List<object?> { new ValueInt(1), null },
                    new List<ulong> { 0, 0xDEAD })
            };
            var client = CreateClient(fake);

            var result = await client.ReadAsync(new[] { new ItemAddress("8A0E0001.F"), new ItemAddress("8A0E0001.10") });

            Assert.True(result.Items[0].IsSuccess);
            Assert.False(result.Items[1].IsSuccess);
            Assert.Equal((ulong)0xDEAD, result.Items[1].ItemError);
        }

        [Fact]
        public async Task EmptyReadsReturnEmptyResultsWithoutConnecting()
        {
            var fake = new FakeS7CommPlusSession();
            var client = CreateClient(fake);

            var addressRead = await client.ReadAsync(Array.Empty<ItemAddress>());
            var tagRead = await client.ReadAsync(Array.Empty<PlcTag>());

            Assert.Empty(addressRead.Items);
            Assert.Empty(tagRead.Items);
            Assert.Equal(0, fake.ConnectCount);
        }

        [Fact]
        public async Task EmptyWritesReturnEmptyResultsWithoutConnecting()
        {
            var fake = new FakeS7CommPlusSession();
            var client = CreateClient(fake);

            var addressWrite = await client.WriteAsync(Array.Empty<ItemAddress>(), Array.Empty<PValue>());
            var tagWrite = await client.WriteAsync(Array.Empty<PlcTag>());

            Assert.Empty(addressWrite.Items);
            Assert.Empty(tagWrite.Items);
            Assert.Equal(0, fake.ConnectCount);
        }

        [Fact]
        public void ItemAddressRejectsMalformedAccessStrings()
        {
            Assert.Throws<ArgumentException>(() => new ItemAddress(""));
            Assert.Throws<ArgumentException>(() => new ItemAddress("8A0E0001"));
            Assert.Throws<ArgumentException>(() => new ItemAddress("8A0E0001.NOTHEX"));
        }

        [Fact]
        public void PlcTagCharRejectsCharactersThatDoNotFitOnePlcByte()
        {
            var tag = new PlcTagChar("CharValue", new ItemAddress("8A0E0001.F"), Softdatatype.S7COMMP_SOFTDATATYPE_CHAR);

            tag.Value = 'A';

            Assert.Equal('A', tag.Value);
            Assert.Throws<ArgumentOutOfRangeException>(() => tag.Value = '\u20AC');
        }

        [Fact]
        public void VlqDecodersRejectTruncatedValues()
        {
            Assert.Throws<InvalidDataException>(() => S7p.DecodeUInt32Vlq(new MemoryStream(new byte[] { 0x80 }), out _));
            Assert.Throws<InvalidDataException>(() => S7p.DecodeUInt64Vlq(new MemoryStream(new byte[] { 0x80 }), out _));
            Assert.Throws<InvalidDataException>(() => S7p.DecodeInt32Vlq(new MemoryStream(new byte[] { 0x80 }), out _));
            Assert.Throws<InvalidDataException>(() => S7p.DecodeInt64Vlq(new MemoryStream(new byte[] { 0x80 }), out _));
        }

        [Fact]
        public void BlobDecompressorReturnsPayloadWhenOnlyZlibTrailerIsMissing()
        {
            const string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><PlcContentInfo><Entity Id=\"Target\" /></PlcContentInfo>";
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            using (var writer = new StreamWriter(zlib))
            {
                writer.Write(xml);
            }

            var truncated = compressed.ToArray()[..^4];
            var decompressor = new BlobDecompressor();

            var result = decompressor.decompress(truncated, 0);

            Assert.Equal(xml, result);
        }

        [Fact]
        public async Task BrowseBlockStructureParsesPlcStructureXml()
        {
            const string xml = """
                <?xml version="1.0" encoding="utf-8"?>
                <PlcContentInfo>
                  <Entity Id="Target">
                    <GroupStructureV2>
                      <Unit Name="Software unit">
                        <ProgramBlocks>
                          <Group Name="Main folder">
                            <OnlineId>123</OnlineId>
                          </Group>
                        </ProgramBlocks>
                        <PlcTagTables>
                          <Group Name="Tag folders">
                            <OnlineId>Default tag table</OnlineId>
                          </Group>
                        </PlcTagTables>
                        <PlcDataTypes />
                      </Unit>
                    </GroupStructureV2>
                  </Entity>
                  <Entity Rid="123">
                    <Header Name="MainBlock" Number="1" Type="FB" SubType="FB" ProgrammingLanguage="SCL" LastModified="2026-05-22T00:00:00Z" />
                  </Entity>
                  <Entity>
                    <TagTables>
                      <TagTables>
                        <Container>
                          <TagTable Name="Default tag table" LoadRelevantModifiedTime="2026-05-22T00:01:00Z" />
                        </Container>
                      </TagTables>
                    </TagTables>
                  </Entity>
                </PlcContentInfo>
                """;
            var fake = new FakeS7CommPlusSession
            {
                PlcStructureXmlHandler = () => (0, xml)
            };
            var client = CreateClient(fake);

            var snapshot = await client.GetPlcStructureXmlAsync();
            var structure = await client.BrowseBlockStructureAsync();

            Assert.Equal(xml, snapshot.Xml);
            Assert.Equal(ExpectedSha256Hex(xml), snapshot.ProgramChangeMarker.StructureHash);
            Assert.Equal(new DateTime(2026, 5, 22, 0, 1, 0, DateTimeKind.Utc), snapshot.ProgramChangeMarker.LastModified);
            Assert.Equal(1, snapshot.ProgramChangeMarker.BlockCount);
            Assert.Equal(1, snapshot.ProgramChangeMarker.TagTableCount);
            var unit = Assert.Single(structure);
            Assert.Equal(S7CommPlusPlcStructureNodeKind.Unit, unit.Kind);
            Assert.Equal("Software unit", unit.Name);
            var programBlocks = unit.Children.Single(child => child.Name == "Program blocks");
            var mainFolder = Assert.Single(programBlocks.Children);
            var block = Assert.Single(mainFolder.Children);
            Assert.Equal(S7CommPlusPlcStructureNodeKind.Block, block.Kind);
            Assert.Equal((uint)123, block.RelationId);
            Assert.Equal("MainBlock", block.Name);
            Assert.Equal(1, block.Number);
            Assert.Equal("FB", block.BlockType);
            Assert.Equal("SCL", block.BlockLanguage);
            var tagTable = Assert.Single(unit.Children.Single(child => child.Name == "PLC tags").Children.Single().Children);
            Assert.Equal(S7CommPlusPlcStructureNodeKind.Item, tagTable.Kind);
            Assert.Equal("Default tag table", tagTable.Name);
            Assert.Equal("TagTable", tagTable.BlockType);
        }

        private static string ExpectedSha256Hex(string value)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", string.Empty);
        }

        [Fact]
        public async Task ConcurrentReadsAreSerialized()
        {
            var fake = new FakeS7CommPlusSession
            {
                ReadHandler = _ =>
                {
                    Task.Delay(50).Wait();
                    return (0, new List<object?> { new ValueInt(1) }, new List<ulong> { 0 });
                }
            };
            var client = CreateClient(fake);
            var addresses = new[] { new ItemAddress("8A0E0001.F") };

            await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.ReadAsync(addresses)));

            Assert.Equal(1, fake.MaxConcurrentReads);
            Assert.Equal(5, fake.ReadCount);
        }

        [Fact]
        public async Task WritesAreBlockedByDefault()
        {
            var fake = new FakeS7CommPlusSession();
            var client = CreateClient(fake);

            await Assert.ThrowsAsync<S7CommPlusWriteDisabledException>(() =>
                client.WriteAsync(new[] { new ItemAddress("8A0E0001.F") }, new PValue[] { new ValueInt(1) }));
        }

        [Fact]
        public async Task CpuStartStopAreBlockedByDefault()
        {
            var fake = new FakeS7CommPlusSession();
            var client = CreateClient(fake);

            await Assert.ThrowsAsync<S7CommPlusWriteDisabledException>(() => client.StopCpuAsync());
            await Assert.ThrowsAsync<S7CommPlusWriteDisabledException>(() => client.StartCpuAsync());

            Assert.Equal(0, fake.CpuOperatingStateWriteCount);
        }

        [Fact]
        public async Task CpuStartStopUseOperatingStateRequests()
        {
            var fake = new FakeS7CommPlusSession();
            var client = CreateClient(fake, writeEnabled: true);

            await client.StopCpuAsync();
            await client.StartCpuAsync();

            Assert.Equal(new[] { 1, 3 }, fake.CpuOperatingStateRequests);
            Assert.Equal(S7CommPlusConnectionState.Connected, client.State);
        }

        [Fact]
        public async Task SecurityModeIsPassedToConnectionAndNegotiatedModeIsExposed()
        {
            var fake = new FakeS7CommPlusSession
            {
                ConnectHandler = options =>
                {
                    Assert.Equal(S7CommPlusSecurityMode.LegacyChallenge, options.SecurityMode);
                    Assert.Equal(S7CommPlusTlsBackend.BouncyCastle, options.TlsBackend);
                    options.NegotiatedSecurityMode = S7CommPlusSecurityMode.LegacyChallenge;
                    return 0;
                }
            };
            var client = new S7CommPlusClient(
                new S7CommPlusClientOptions
                {
                    Address = "127.0.0.1",
                    SecurityMode = S7CommPlusSecurityMode.LegacyChallenge,
                    TlsBackend = S7CommPlusTlsBackend.BouncyCastle,
                    ConnectTimeout = TimeSpan.FromMilliseconds(500),
                    RequestTimeout = TimeSpan.FromMilliseconds(500),
                    DisconnectTimeout = TimeSpan.FromMilliseconds(100)
                },
                () => fake);

            await client.ConnectAsync();

            Assert.Equal(S7CommPlusSecurityMode.LegacyChallenge, client.Options.NegotiatedSecurityMode);
            Assert.Equal(S7CommPlusTlsBackend.BouncyCastle, client.Options.TlsBackend);
        }

        [Fact]
        public async Task AutoSecurityModeAllowsOneConnectTimeoutPerSecurityAttempt()
        {
            var fake = new FakeS7CommPlusSession
            {
                ConnectHandler = options =>
                {
                    Assert.Equal(TimeSpan.FromMilliseconds(200), options.ConnectTimeout);
                    Thread.Sleep(300);
                    return 0;
                }
            };
            var client = new S7CommPlusClient(
                new S7CommPlusClientOptions
                {
                    Address = "127.0.0.1",
                    SecurityMode = S7CommPlusSecurityMode.Auto,
                    ConnectTimeout = TimeSpan.FromMilliseconds(200),
                    RequestTimeout = TimeSpan.FromMilliseconds(500),
                    DisconnectTimeout = TimeSpan.FromMilliseconds(100)
                },
                () => fake);

            await client.ConnectAsync();

            Assert.True(client.IsConnected);
        }

        [Fact]
        public void BouncyCastleIsDefaultTlsBackend()
        {
            var options = new S7CommPlusClientOptions();

            Assert.Equal(S7CommPlusTlsBackend.BouncyCastle, options.TlsBackend);
        }

        [Fact]
        public async Task ConnectErrorIncludesSessionErrorDetail()
        {
            var fake = new FakeS7CommPlusSession
            {
                LastErrorDetail = "Could not load native OpenSSL dependency 'runtimes/win-arm64/native/libcrypto-3-arm64.dll'."
            };
            fake.ConnectHandler = _ => S7Consts.errOpenSSL;
            var client = CreateClient(fake);

            var ex = await Assert.ThrowsAsync<S7CommPlusConnectionException>(() => client.ConnectAsync());

            Assert.Contains("OPENSSL", ex.Message);
            Assert.Contains("libcrypto-3-arm64.dll", ex.Message);
        }

        [Fact]
        public void RemoteTsapMustBeAscii()
        {
            var ex = Assert.Throws<ArgumentException>(() => new S7CommPlusClient(
                new S7CommPlusClientOptions
                {
                    Address = "127.0.0.1",
                    RemoteTsap = "SIMATIC-ROOT-HMI-\u00C4"
                },
                () => new FakeS7CommPlusSession()));

            Assert.Equal("RemoteTsap", ex.ParamName);
        }

        [Fact]
        public void RemoteTsapMustFitCotpParameterLength()
        {
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new S7CommPlusClient(
                new S7CommPlusClientOptions
                {
                    Address = "127.0.0.1",
                    RemoteTsap = new string('A', 256)
                },
                () => new FakeS7CommPlusSession()));

            Assert.Equal("RemoteTsap", ex.ParamName);
        }

        [Fact]
        public void SiemensTsapDefaultsAreAvailable()
        {
            Assert.Equal(102, S7CommPlusDefaults.IsoTcpPort);
            Assert.Equal((ushort)0x0600, S7CommPlusDefaults.LocalTsap);
            Assert.Equal("SIMATIC-ROOT-HMI", S7CommPlusDefaults.RemoteTsapHmi);
            Assert.Equal("SIMATIC-ROOT-ES", S7CommPlusDefaults.RemoteTsapEs);
        }

        [Fact]
        public async Task LegitimateAsyncUsesTypedLegitimationErrorAndDoesNotRequireWriteEnablement()
        {
            var fake = new FakeS7CommPlusSession
            {
                LegitimateHandler = (password, username) =>
                {
                    Assert.Equal("secret", password);
                    Assert.Equal("operator", username);
                    return S7Consts.errCliAccessDenied;
                }
            };
            var client = CreateClient(fake);

            var ex = await Assert.ThrowsAsync<S7CommPlusLegitimationException>(() =>
                client.LegitimateAsync("secret", "operator"));

            Assert.Equal("Legitimate", ex.Operation);
            Assert.False(ex.IsTransient);
        }

        [Fact]
        public async Task GetCommunicationResourcesUsesReadPipeline()
        {
            var fake = new FakeS7CommPlusSession();
            var client = CreateClient(fake);

            var resources = await client.GetCommunicationResourcesAsync();

            Assert.Equal(20, resources.TagsPerReadRequestMax);
            Assert.Equal(20, resources.TagsPerWriteRequestMax);
            Assert.Equal(S7CommPlusConnectionState.Connected, client.State);
        }

        [Fact]
        public async Task GetCpuStateUsesReadPipeline()
        {
            var fake = new FakeS7CommPlusSession
            {
                CpuStateHandler = () => (0, new S7CommPlusCpuState(5, S7CommPlusCpuOperatingState.Run, 2, "Run"))
            };
            var client = CreateClient(fake);

            var cpuState = await client.GetCpuStateAsync();

            Assert.Equal(5, cpuState.RawOperatingState);
            Assert.Equal(S7CommPlusCpuOperatingState.Run, cpuState.OperatingState);
            Assert.Equal(2, cpuState.RawStateSwitch);
            Assert.Equal("Run", cpuState.StateSwitch);
            Assert.True(cpuState.IsRun);
            Assert.False(cpuState.IsStop);
            Assert.Equal(S7CommPlusConnectionState.Connected, client.State);
        }

        [Fact]
        public async Task GetCpuCycleTimeUsesReadPipeline()
        {
            var fake = new FakeS7CommPlusSession
            {
                CpuCycleTimeHandler = () => (0, new S7CommPlusCpuCycleTime(0, 150, 50.007, 50.012, 50.654))
            };
            var client = CreateClient(fake);

            var cycleTime = await client.GetCpuCycleTimeAsync();

            Assert.Equal(0, cycleTime.ConfiguredMinimumMilliseconds);
            Assert.Equal(150, cycleTime.ConfiguredMaximumMilliseconds);
            Assert.Equal(50.007, cycleTime.ShortestMilliseconds);
            Assert.Equal(50.012, cycleTime.CurrentMilliseconds);
            Assert.Equal(50.654, cycleTime.LongestMilliseconds);
            Assert.Equal(S7CommPlusConnectionState.Connected, client.State);
        }

        [Fact]
        public async Task GetCpuMemoryUsageUsesReadPipeline()
        {
            var fake = new FakeS7CommPlusSession
            {
                CpuMemoryUsageHandler = () => (0, new S7CommPlusCpuMemoryUsage(new[]
                {
                    new S7CommPlusCpuMemoryArea("load", "Load memory", 1000, 120),
                    new S7CommPlusCpuMemoryArea("retain", "Retain memory", 500, 25)
                }))
            };
            var client = CreateClient(fake);

            var memoryUsage = await client.GetCpuMemoryUsageAsync();

            Assert.Collection(
                memoryUsage.Areas,
                area =>
                {
                    Assert.Equal("load", area.Key);
                    Assert.Equal("Load memory", area.Name);
                    Assert.Equal(1000, area.TotalBytes);
                    Assert.Equal(120, area.UsedBytes);
                    Assert.Equal(880, area.FreeBytes);
                    Assert.Equal(12, area.UsedPercent);
                    Assert.Equal(88, area.FreePercent);
                },
                area =>
                {
                    Assert.Equal("retain", area.Key);
                    Assert.Equal(500, area.TotalBytes);
                    Assert.Equal(25, area.UsedBytes);
                });
            Assert.Equal(S7CommPlusConnectionState.Connected, client.State);
        }

        [Fact]
        public async Task GetCpuCultureInfoUsesReadPipeline()
        {
            var fake = new FakeS7CommPlusSession
            {
                CpuCultureInfoHandler = () => (0, new S7CommPlusCpuCultureInfo(new[] { 1031, 1033 }))
            };
            var client = CreateClient(fake);

            var cultureInfo = await client.GetCpuCultureInfoAsync();

            Assert.Equal(new[] { 1031, 1033 }, cultureInfo.LanguageIds);
            Assert.Equal("de-DE", cultureInfo.PrimaryCulture.Name);
            Assert.Contains(cultureInfo.Cultures, culture => culture.Name == "en-US");
            Assert.Equal(S7CommPlusConnectionState.Connected, client.State);
        }

        [Fact]
        public void CpuCultureInfoPreservesUnresolvedLanguageIds()
        {
            var cultureInfo = new S7CommPlusCpuCultureInfo(new[] { 1031, 0xffff });

            Assert.Equal(new[] { 1031, 0xffff }, cultureInfo.LanguageIds);
            Assert.Equal(new[] { 0xffff }, cultureInfo.UnresolvedLanguageIds);
            Assert.Equal("de-DE", cultureInfo.PrimaryCulture.Name);
        }

        [Fact]
        public async Task GetActiveAlarmsUsesReadPipeline()
        {
            var fake = new FakeS7CommPlusSession
            {
                ActiveAlarmsHandler = () => (0, new List<S7CommPlusAlarm>())
            };
            var client = CreateClient(fake);

            var alarms = await client.GetActiveAlarmsAsync(languageId: 1033);

            Assert.Empty(alarms);
            Assert.Equal(1033, fake.LastActiveAlarmsLanguageId);
            Assert.Equal(S7CommPlusConnectionState.Connected, client.State);
        }

        [Fact]
        public async Task GetActiveAlarmsWithoutLanguageIdRequestsAllLanguages()
        {
            var fake = new FakeS7CommPlusSession
            {
                ActiveAlarmsHandler = () => (0, new List<S7CommPlusAlarm>())
            };
            var client = CreateClient(fake);

            var alarms = await client.GetActiveAlarmsAsync();

            Assert.Empty(alarms);
            Assert.Equal(0, fake.LastActiveAlarmsLanguageId);
        }

        [Fact]
        public void AlarmExposesSourceRelationAndAlarmIds()
        {
            var alarm = new S7CommPlusAlarm
            {
                CpuAlarmId = ((ulong)0x12345678 << 32) | ((ulong)0x9abc << 16)
            };

            Assert.Equal((uint)0x12345678, alarm.SourceRelationId);
            Assert.Equal((ushort)0x9abc, alarm.SourceAlarmId);
        }

        [Fact]
        public void AlarmTextsCanReadAllNotificationLanguages()
        {
            var blob = new ValueBlobSparseArray(new Dictionary<uint, ValueBlobSparseArray.BlobEntry>
            {
                { ((uint)1031 << 16) | 2, BlobText("Alarm deutsch") },
                { ((uint)1033 << 16) | 2, BlobText("Alarm english") },
                { ((uint)1033 << 16) | 1, BlobText("Info english") }
            });

            var texts = S7CommPlusAlarmTexts.FromNotificationBlobAllLanguages(blob);

            Assert.Equal("Alarm deutsch", texts[1031].AlarmText);
            Assert.Equal("Alarm english", texts[1033].AlarmText);
            Assert.Equal("Info english", texts[1033].Infotext);
        }

        [Fact]
        public async Task TagSubscriptionPublishesValuesAndDeletesOnDispose()
        {
            var allowNotifications = new ManualResetEventSlim(false);
            var fake = new FakeS7CommPlusSession();
            fake.WaitForTagSubscriptionHandler = (_, _) =>
            {
                allowNotifications.Wait(TimeSpan.FromSeconds(1));
                if (fake.TagSubscriptionWaitCount == 1)
                {
                    return (0, new List<Notification>
                    {
                        new Notification(2)
                        {
                            Add1Timestamp = DateTime.UtcNow,
                            NotificationCreditTick = 1,
                            NotificationSequenceNumber = 7,
                            Values = { [1] = new ValueInt(42) }
                        }
                    });
                }

                Thread.Sleep(10);
                return (S7Consts.errCliJobTimeout, new List<Notification>());
            };
            var client = CreateClient(fake);
            var tag = PlcTags.TagFactory("DB.Value", new ItemAddress("8A0E0001.F"), Softdatatype.S7COMMP_SOFTDATATYPE_INT);
            var received = new TaskCompletionSource<S7CommPlusTagNotification>(TaskCreationOptions.RunContinuationsAsynchronously);

            await using var subscription = await client.SubscribeTagsAsync(new[] { tag }, FastSubscriptionOptions());
            subscription.NotificationReceived += (_, args) => received.TrySetResult(args.Notification);
            allowNotifications.Set();

            var notification = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await subscription.StopAsync();

            Assert.Equal((uint)7, notification.SequenceNumber);
            Assert.Single(notification.Items);
            Assert.True(notification.Items[0].IsSuccess);
            Assert.Same(tag, notification.Items[0].Tag);
            Assert.Equal(1, fake.TagSubscriptionCreateCount);
            Assert.Equal(1, fake.TagSubscriptionDeleteCount);
        }

        [Fact]
        public void TisResultParserUsesRequestModelOffsets()
        {
            var model = new S7CommPlusTisResultModel();
            var watchPoint = new S7CommPlusTisWatchPointModel
            {
                NetworkId = "cu-1",
                Uid = "21",
                Pin = "operand",
                RloOffset = 20
            };
            watchPoint.Values.Add(new S7CommPlusTisValueModel
            {
                NetworkId = "cu-1",
                Uid = "21",
                Pin = "operand",
                ValueOffset = 30,
                ByteLength = 1,
                ValidityOffset = 31,
                ValidCountOffset = 32
            });
            model.WatchPoints.Add(watchPoint);
            var result = new byte[40];
            result[20] = 0x80;
            result[23] = 0x05;
            result[30] = 1;
            result[31] = 0x7f;
            result[33] = 2;

            var parsed = S7CommPlusTisResultParser.Parse(result, model);

            var parsedWatchPoint = Assert.Single(parsed);
            Assert.True(parsedWatchPoint.Rlo);
            Assert.Equal((uint)5, parsedWatchPoint.ExecutionCount);
            var parsedValue = Assert.Single(parsedWatchPoint.Values);
            Assert.True(parsedValue.BoolValue);
            Assert.Equal((byte)0x7f, parsedValue.Validity);
            Assert.Equal((ushort)2, parsedValue.ValidCount);
            Assert.Equal("TRUE", parsedValue.DisplayValue);
        }

        [Fact]
        public void NotificationParsesCapturedOnlineStatusTableItems()
        {
            using var stream = CreateNotificationBody();
            stream.WriteByte((byte)S7CommPlusNotificationReturnCode.OnlineStatusTable);
            stream.WriteByte(0x74);
            stream.WriteByte(0x00);
            stream.WriteByte(0x08);
            stream.WriteByte(0x02);
            stream.WriteByte((byte)S7CommPlusNotificationReturnCode.OnlineStatusTable);
            stream.WriteByte(0x70);
            stream.WriteByte(0x00);
            stream.WriteByte(0x08);
            stream.WriteByte(0x01);
            stream.WriteByte((byte)S7CommPlusNotificationReturnCode.EndOfList);
            stream.WriteByte(0);
            stream.Position = 0;

            var notification = new Notification(2);
            notification.Deserialize(stream);

            Assert.Equal(0x74000802u, notification.OnlineStatusTableValues[0x74]);
            Assert.Equal(0x70000801u, notification.OnlineStatusTableValues[0x70]);
            Assert.Empty(notification.Values);
        }

        [Fact]
        public void NotificationParsesCapturedTisResultAsBlobValue()
        {
            var result = new byte[116];
            result[104] = 0x80;
            result[107] = 0x01;
            result[108] = 0x00;
            result[111] = 0x02;
            result[112] = 0x80;
            result[115] = 0x03;
            using var stream = CreateNotificationBody();
            stream.WriteByte((byte)S7CommPlusNotificationReturnCode.ValueWithUInt32Reference);
            WriteUInt32(stream, 9);
            new ValueBlob(0, result).Serialize(stream);
            stream.WriteByte((byte)S7CommPlusNotificationReturnCode.ValueWithUInt32Reference);
            WriteUInt32(stream, 10);
            new ValueUSInt(1).Serialize(stream);
            stream.WriteByte((byte)S7CommPlusNotificationReturnCode.ValueWithUInt32Reference);
            WriteUInt32(stream, 11);
            new ValueBool(true).Serialize(stream);
            stream.WriteByte((byte)S7CommPlusNotificationReturnCode.EndOfList);
            stream.WriteByte(0);
            stream.Position = 0;

            var notification = new Notification(2);
            notification.Deserialize(stream);
            var model = new S7CommPlusTisResultModel();
            model.WatchPoints.Add(new S7CommPlusTisWatchPointModel { RloOffset = 104 });
            model.WatchPoints.Add(new S7CommPlusTisWatchPointModel { RloOffset = 108 });
            model.WatchPoints.Add(new S7CommPlusTisWatchPointModel { RloOffset = 112 });

            var blob = Assert.IsType<ValueBlob>(notification.Values[9]);
            var parsed = S7CommPlusTisResultParser.Parse(blob.GetValue(), model);

            Assert.True(parsed[0].Rlo);
            Assert.False(parsed[1].Rlo);
            Assert.True(parsed[2].Rlo);
            Assert.Equal((byte)1, Assert.IsType<ValueUSInt>(notification.Values[10]).GetValue());
            Assert.True(Assert.IsType<ValueBool>(notification.Values[11]).GetValue());
        }

        [Fact]
        public async Task TisWatchSubscriptionPublishesUpdatesAndDeletesOnDispose()
        {
            var allowNotifications = new ManualResetEventSlim(false);
            var fake = new FakeS7CommPlusSession();
            var notification = new S7CommPlusTisWatchNotification(
                DateTime.UtcNow,
                123,
                1,
                true,
                1,
                new byte[] { 1, 2, 3 },
                new[]
                {
                    new S7CommPlusTisWatchPointResult("cu-1", "21", "operand", true, 0x80000001, 1, Array.Empty<S7CommPlusTisValueResult>())
                });
            fake.WaitForTisWatchSubscriptionHandler = _ =>
            {
                allowNotifications.Wait(TimeSpan.FromSeconds(1));
                if (fake.TisWatchSubscriptionWaitCount == 1)
                {
                    return (0, new List<S7CommPlusTisWatchNotification> { notification });
                }

                Thread.Sleep(10);
                return (S7Consts.errCliJobTimeout, new List<S7CommPlusTisWatchNotification>());
            };
            var client = CreateClient(fake);
            var received = new TaskCompletionSource<S7CommPlusTisWatchNotification>(TaskCreationOptions.RunContinuationsAsynchronously);

            await using var subscription = await client.OpenBlockOnlineViewAsync(new S7CommPlusTisWatchRequest
            {
                RequestBlob = new byte[] { 0x40, 0x80, 0x32, 0x06 },
                TriggerBlob = new byte[] { 0x10, 0x06 },
            }, FastSubscriptionOptions());
            subscription.NotificationReceived += (_, args) => received.TrySetResult(args.Notification);
            allowNotifications.Set();

            var update = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await subscription.StopAsync();

            Assert.Equal((uint)123, update.SequenceNumber);
            Assert.Single(update.WatchPoints);
            Assert.True(update.WatchPoints[0].Rlo);
            Assert.Equal(1, fake.TisWatchSubscriptionCreateCount);
            Assert.Equal(1, fake.TisWatchSubscriptionDeleteCount);
        }

        [Fact]
        public async Task MultipleTisWatchSubscriptionsUseOneConnectionAndDistinctHandles()
        {
            var fake = new FakeS7CommPlusSession
            {
                WaitForTisWatchSubscriptionByIdHandler = (_, _) =>
                {
                    Thread.Sleep(5);
                    return (S7Consts.errCliJobTimeout, new List<S7CommPlusTisWatchNotification>());
                }
            };
            var client = CreateClient(fake);
            var options = FastSubscriptionOptions();

            await using var first = await client.OpenBlockOnlineViewAsync(CreateTisWatchRequest(1), options);
            await using var second = await client.OpenBlockOnlineViewAsync(CreateTisWatchRequest(2), options);
            await using var third = await client.OpenBlockOnlineViewAsync(CreateTisWatchRequest(3), options);

            await first.StopAsync();
            await second.StopAsync();
            await third.StopAsync();

            Assert.Equal(1, fake.ConnectCount);
            Assert.Equal(3, fake.TisWatchSubscriptionCreateCount);
            Assert.Equal(3, fake.TisWatchSubscriptionDeleteCount);
            Assert.Equal(3, fake.CreatedTisWatchSubscriptionIds.Distinct().Count());
            Assert.Equal(
                fake.CreatedTisWatchSubscriptionIds.OrderBy(id => id),
                fake.DeletedTisWatchSubscriptionIds.OrderBy(id => id));
        }

        [Fact]
        public async Task TagSubscriptionPublishesPerItemErrors()
        {
            var allowNotifications = new ManualResetEventSlim(false);
            var fake = new FakeS7CommPlusSession();
            fake.WaitForTagSubscriptionHandler = (_, _) =>
            {
                allowNotifications.Wait(TimeSpan.FromSeconds(1));
                if (fake.TagSubscriptionWaitCount == 1)
                {
                    return (0, new List<Notification>
                    {
                        new Notification(2)
                        {
                            ReturnValues = { [1] = 0x13 }
                        }
                    });
                }

                Thread.Sleep(10);
                return (S7Consts.errCliJobTimeout, new List<Notification>());
            };
            var client = CreateClient(fake);
            var tag = PlcTags.TagFactory("DB.Value", new ItemAddress("8A0E0001.F"), Softdatatype.S7COMMP_SOFTDATATYPE_INT);
            var received = new TaskCompletionSource<S7CommPlusTagNotification>(TaskCreationOptions.RunContinuationsAsynchronously);

            await using var subscription = await client.SubscribeTagsAsync(new[] { tag }, FastSubscriptionOptions());
            subscription.NotificationReceived += (_, args) => received.TrySetResult(args.Notification);
            allowNotifications.Set();

            var notification = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await subscription.StopAsync();

            Assert.Single(notification.Items);
            Assert.False(notification.Items[0].IsSuccess);
            Assert.Equal((ulong)0x13, notification.Items[0].ItemError);
            Assert.Equal(1, fake.TagSubscriptionDeleteCount);
        }

        [Fact]
        public async Task SubscriptionIdleTimeoutDoesNotFaultByDefault()
        {
            var fake = new FakeS7CommPlusSession
            {
                WaitForTagSubscriptionHandler = (_, _) =>
                {
                    Thread.Sleep(5);
                    return (S7Consts.errCliJobTimeout, new List<Notification>());
                }
            };
            var client = CreateClient(fake);
            var tag = PlcTags.TagFactory("DB.Value", new ItemAddress("8A0E0001.F"), Softdatatype.S7COMMP_SOFTDATATYPE_INT);

            await using var subscription = await client.SubscribeTagsAsync(new[] { tag }, FastSubscriptionOptions());
            await Task.Delay(40);
            await subscription.StopAsync();

            Assert.Equal(S7CommPlusSubscriptionState.Stopped, subscription.State);
            Assert.Equal(1, fake.TagSubscriptionDeleteCount);
        }

        [Fact]
        public async Task ReadCanRunWhileSubscriptionIsActive()
        {
            var fake = new FakeS7CommPlusSession
            {
                WaitForTagSubscriptionHandler = (_, _) =>
                {
                    Thread.Sleep(10);
                    return (S7Consts.errCliJobTimeout, new List<Notification>());
                }
            };
            var client = CreateClient(fake);
            var tag = PlcTags.TagFactory("DB.Value", new ItemAddress("8A0E0001.F"), Softdatatype.S7COMMP_SOFTDATATYPE_INT);

            await using var subscription = await client.SubscribeTagsAsync(new[] { tag }, FastSubscriptionOptions());
            var readTask = client.ReadAsync(new[] { tag });
            await Task.Delay(40);

            var read = await readTask.WaitAsync(TimeSpan.FromSeconds(2));
            await subscription.StopAsync();

            Assert.Single(read.Items);
            Assert.Equal(1, fake.ReadCount);
        }

        [Fact]
        public async Task AlarmAndTisSubscriptionsCanBeCreatedOnSameClientConnection()
        {
            var fake = new FakeS7CommPlusSession
            {
                WaitForAlarmSubscriptionHandler = (_, _) =>
                {
                    Thread.Sleep(5);
                    return (S7Consts.errCliJobTimeout, new List<Notification>());
                },
                WaitForTisWatchSubscriptionHandler = _ =>
                {
                    Thread.Sleep(5);
                    return (S7Consts.errCliJobTimeout, new List<S7CommPlusTisWatchNotification>());
                }
            };
            var client = CreateClient(fake);

            await using var alarmSubscription = await client.SubscribeAlarmsAsync(
                options: new S7CommPlusSubscriptionOptions
                {
                    NotificationTimeout = TimeSpan.FromMilliseconds(20)
                });
            await using var tisSubscription = await client.OpenBlockOnlineViewAsync(new S7CommPlusTisWatchRequest
            {
                RequestBlob = new byte[] { 0x40, 0x80, 0x32, 0x06 },
                TriggerBlob = new byte[] { 0x10, 0x06 },
            }, FastSubscriptionOptions());

            await alarmSubscription.StopAsync();
            await tisSubscription.StopAsync();

            Assert.Equal(1, fake.ConnectCount);
            Assert.Equal(1, fake.AlarmSubscriptionCreateCount);
            Assert.Equal(1, fake.TisWatchSubscriptionCreateCount);
            Assert.Equal(1, fake.AlarmSubscriptionDeleteCount);
            Assert.Equal(1, fake.TisWatchSubscriptionDeleteCount);
        }

        [Fact]
        public async Task ActiveAlarmsCanBeReadAfterAlarmSubscriptionStartsOnSameConnection()
        {
            var fake = new FakeS7CommPlusSession
            {
                ActiveAlarmsHandler = () => (0, new List<S7CommPlusAlarm>()),
                WaitForAlarmSubscriptionHandler = (_, _) =>
                {
                    Thread.Sleep(5);
                    return (S7Consts.errCliJobTimeout, new List<Notification>());
                }
            };
            var client = CreateClient(fake);

            await using var subscription = await client.SubscribeAlarmsAsync(
                1033,
                new S7CommPlusSubscriptionOptions
                {
                    NotificationTimeout = TimeSpan.FromMilliseconds(20)
                });

            var activeAlarms = await client.GetActiveAlarmsAsync(1033);
            await subscription.StopAsync();

            Assert.Empty(activeAlarms);
            Assert.Equal(1, fake.ConnectCount);
            Assert.Equal(1, fake.AlarmSubscriptionCreateCount);
            Assert.Equal(1, fake.AlarmSubscriptionDeleteCount);
            Assert.Equal(1033, fake.LastActiveAlarmsLanguageId);
            Assert.Equal(S7CommPlusConnectionState.Connected, client.State);
        }

        [Fact]
        public async Task SubscriptionCommunicationFailureFaultsSubscriptionAndClient()
        {
            var fake = new FakeS7CommPlusSession
            {
                WaitForTagSubscriptionHandler = (_, _) => (S7Consts.errTCPDataReceive, new List<Notification>())
            };
            var client = CreateClient(fake);
            var tag = PlcTags.TagFactory("DB.Value", new ItemAddress("8A0E0001.F"), Softdatatype.S7COMMP_SOFTDATATYPE_INT);
            var error = new TaskCompletionSource<S7CommPlusException>(TaskCreationOptions.RunContinuationsAsynchronously);

            await using var subscription = await client.SubscribeTagsAsync(new[] { tag }, FastSubscriptionOptions());
            subscription.CommunicationError += (_, args) => error.TrySetResult(args.Exception);

            var ex = await error.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal("WaitForTagSubscriptionNotifications", ex.Operation);
            Assert.Equal(S7CommPlusSubscriptionState.Faulted, subscription.State);
            Assert.Equal(S7CommPlusConnectionState.Faulted, client.State);
            await Assert.ThrowsAsync<S7CommPlusConnectionException>(() => subscription.Completion);
            Assert.Equal(1, fake.TagSubscriptionDeleteCount);
        }

        [Fact]
        public async Task AlarmSubscriptionCreatesWithLanguagesAndDeletesOnDispose()
        {
            uint[]? capturedLanguages = null;
            var fake = new FakeS7CommPlusSession
            {
                CreateAlarmSubscriptionHandler = (languages, creditLimit) =>
                {
                    capturedLanguages = languages;
                    Assert.Equal((short)3, creditLimit);
                    return 0;
                },
                WaitForAlarmSubscriptionHandler = (_, _) =>
                {
                    Thread.Sleep(5);
                    return (S7Consts.errCliJobTimeout, new List<Notification>());
                }
            };
            var client = CreateClient(fake);

            await using var subscription = await client.SubscribeAlarmsAsync(
                new[] { 1031, 1033 },
                alarmTextLanguageId: 1033,
                options: new S7CommPlusSubscriptionOptions
                {
                    InitialCreditLimit = 3,
                    NotificationTimeout = TimeSpan.FromMilliseconds(20)
                });
            await subscription.StopAsync();

            Assert.Equal(new uint[] { 1031, 1033 }, capturedLanguages);
            Assert.Equal(1033, subscription.AlarmTextLanguageId);
            Assert.Equal(1, fake.AlarmSubscriptionCreateCount);
            Assert.Equal(1, fake.AlarmSubscriptionDeleteCount);
            Assert.Equal(0, fake.DisconnectCount);
            Assert.Equal(S7CommPlusConnectionState.Connected, client.State);
        }

        [Fact]
        public async Task AlarmSubscriptionWithoutLanguageIdRequestsAllLanguages()
        {
            uint[]? capturedLanguages = null;
            var fake = new FakeS7CommPlusSession
            {
                CreateAlarmSubscriptionHandler = (languages, _) =>
                {
                    capturedLanguages = languages;
                    return 0;
                },
                WaitForAlarmSubscriptionHandler = (_, _) =>
                {
                    Thread.Sleep(5);
                    return (S7Consts.errCliJobTimeout, new List<Notification>());
                }
            };
            var client = CreateClient(fake);

            await using var subscription = await client.SubscribeAlarmsAsync(
                options: new S7CommPlusSubscriptionOptions
                {
                    NotificationTimeout = TimeSpan.FromMilliseconds(20)
                });
            await subscription.StopAsync();

            Assert.Empty(capturedLanguages!);
            Assert.True(subscription.ReceivesAllAlarmTextLanguages);
            Assert.Equal(0, subscription.AlarmTextLanguageId);
        }

        [Fact]
        public async Task AlarmSubscriptionConnectionLossSkipsProtocolDelete()
        {
            var fake = new FakeS7CommPlusSession
            {
                WaitForAlarmSubscriptionHandler = (_, _) => (S7Consts.errTCPNotConnected, new List<Notification>())
            };
            var client = CreateClient(fake);
            var error = new TaskCompletionSource<S7CommPlusException>(TaskCreationOptions.RunContinuationsAsynchronously);

            await using var subscription = await client.SubscribeAlarmsAsync(
                languageId: 1031,
                options: new S7CommPlusSubscriptionOptions
                {
                    NotificationTimeout = TimeSpan.FromMilliseconds(20)
                });
            subscription.CommunicationError += (_, args) => error.TrySetResult(args.Exception);

            var ex = await error.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal("WaitForAlarmNotifications", ex.Operation);
            Assert.Equal(S7Consts.errTCPNotConnected, ex.ErrorCode);
            Assert.Equal(S7CommPlusSubscriptionState.Faulted, subscription.State);
            Assert.Equal(S7CommPlusConnectionState.Faulted, client.State);
            await Assert.ThrowsAsync<S7CommPlusConnectionException>(() => subscription.Completion);
            Assert.Equal(1, fake.AlarmSubscriptionCreateCount);
            Assert.Equal(0, fake.AlarmSubscriptionDeleteCount);
        }

        [Fact]
        public async Task AlarmSnapshotSubscriptionSubscribesBeforeReadingActiveAlarms()
        {
            var order = new List<string>();
            var subscriptionFake = new FakeS7CommPlusSession
            {
                CreateAlarmSubscriptionHandler = (_, _) =>
                {
                    order.Add("subscribe");
                    return 0;
                },
                WaitForAlarmSubscriptionHandler = (_, _) => (S7Consts.errCliJobTimeout, new List<Notification>())
            };
            var snapshotFake = new FakeS7CommPlusSession
            {
                ActiveAlarmsHandler = () =>
                {
                    order.Add("snapshot");
                    return (0, new List<S7CommPlusAlarm>());
                }
            };
            var subscriptionClient = CreateClient(subscriptionFake);
            var snapshotClient = CreateClient(snapshotFake);

            await using var result = await subscriptionClient.SubscribeAlarmsWithSnapshotAsync(
                snapshotClient,
                languageId: 1031,
                options: new S7CommPlusSubscriptionOptions
                {
                    NotificationTimeout = TimeSpan.FromMilliseconds(20)
                });
            await result.Subscription.StopAsync();

            Assert.Same(result.Subscription, result.Subscription);
            Assert.Empty(result.ActiveAlarms);
            Assert.Equal(new[] { "subscribe", "snapshot" }, order);
            Assert.Equal(1031, snapshotFake.LastActiveAlarmsLanguageId);
            Assert.Equal(1, subscriptionFake.AlarmSubscriptionCreateCount);
            Assert.Equal(1, snapshotFake.ConnectCount);
        }

        [Fact]
        public async Task AlarmSnapshotWithoutLanguageIdRequestsAllLanguages()
        {
            uint[]? capturedLanguages = null;
            var subscriptionFake = new FakeS7CommPlusSession
            {
                CreateAlarmSubscriptionHandler = (languages, _) =>
                {
                    capturedLanguages = languages;
                    return 0;
                },
                WaitForAlarmSubscriptionHandler = (_, _) => (S7Consts.errCliJobTimeout, new List<Notification>())
            };
            var snapshotFake = new FakeS7CommPlusSession
            {
                ActiveAlarmsHandler = () => (0, new List<S7CommPlusAlarm>())
            };
            var subscriptionClient = CreateClient(subscriptionFake);
            var snapshotClient = CreateClient(snapshotFake);

            await using var result = await subscriptionClient.SubscribeAlarmsWithSnapshotAsync(
                snapshotClient,
                options: new S7CommPlusSubscriptionOptions
                {
                    NotificationTimeout = TimeSpan.FromMilliseconds(20)
                });
            await result.Subscription.StopAsync();

            Assert.Empty(capturedLanguages!);
            Assert.Equal(0, snapshotFake.LastActiveAlarmsLanguageId);
            Assert.True(result.Subscription.ReceivesAllAlarmTextLanguages);
        }

        private static S7CommPlusClient CreateClient(FakeS7CommPlusSession fake, int requestTimeoutMs = 5000, bool writeEnabled = false)
        {
            return new S7CommPlusClient(
                new S7CommPlusClientOptions
                {
                    Address = "127.0.0.1",
                    WriteEnabled = writeEnabled,
                    RequestTimeout = TimeSpan.FromMilliseconds(requestTimeoutMs),
                    ConnectTimeout = TimeSpan.FromMilliseconds(500),
                    DisconnectTimeout = TimeSpan.FromMilliseconds(100)
                },
                () => fake);
        }

        private static S7CommPlusSubscriptionOptions FastSubscriptionOptions()
        {
            return new S7CommPlusSubscriptionOptions
            {
                CycleTimeMilliseconds = 100,
                NotificationTimeout = TimeSpan.FromMilliseconds(20)
            };
        }

        private static S7CommPlusTisWatchRequest CreateTisWatchRequest(byte seed)
        {
            return new S7CommPlusTisWatchRequest
            {
                RequestBlob = new byte[] { 0x40, 0x80, 0x32, seed },
                TriggerBlob = new byte[] { 0x10, seed },
            };
        }

        private static MemoryStream CreateNotificationBody()
        {
            var stream = new MemoryStream();
            WriteUInt32(stream, 0x70000FFB);
            WriteUInt16(stream, 0);
            WriteUInt16(stream, 0);
            WriteUInt16(stream, 0);
            stream.WriteByte(1);
            stream.WriteByte(1);
            stream.WriteByte(1);
            return stream;
        }

        private static void WriteUInt16(Stream stream, ushort value)
        {
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        private static void WriteUInt32(Stream stream, uint value)
        {
            stream.WriteByte((byte)(value >> 24));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        private static ValueBlobSparseArray.BlobEntry BlobText(string text)
        {
            return new ValueBlobSparseArray.BlobEntry
            {
                blobRootId = 0,
                value = Encoding.UTF8.GetBytes(text)
            };
        }
    }
}
