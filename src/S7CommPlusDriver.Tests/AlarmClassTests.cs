using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using S7CommPlusDriver.Alarming;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class AlarmClassTests
    {
        [Fact]
        public void ReadsDownloadedClassNamesAndMergesTranslationsAcrossBlocks()
        {
            var parent = new PObject(3, Ids.PLCProgram_Class_Rid, 0);
            parent.AddObject(Block(1, "<AlarmClassList><AlarmClass Name='PlcError' Id='37' Ack='false' Prio='10'>" +
                "<DisplayName LCID='1031' Culture='de-DE'>Fehler</DisplayName>" +
                "<ShortName LCID='1031'>F</ShortName></AlarmClass></AlarmClassList>"));
            parent.AddObject(Block(2, "<AlarmClassList><AlarmClass Name='PlcError' Id='37' Ack='false' Prio='10'>" +
                "<DisplayName LCID='1033'>Error</DisplayName></AlarmClass>" +
                "<AlarmClass Name='PlcWarning' Id='35' Ack='true' /></AlarmClassList>"));

            Assert.Equal(0, S7CommPlusAlarmClassParser.Parse(new[] { parent }, out var classes));
            Assert.Equal(new ushort[] { 35, 37 }, classes.ConvertAll(item => item.Id));
            Assert.Null(classes[0].Priority);
            Assert.True(classes[0].AcknowledgementRequired);
            var error = classes[1];
            Assert.Equal("PlcError", error.Name);
            Assert.Equal(10, error.Priority);
            Assert.False(error.AcknowledgementRequired);
            Assert.Equal("Fehler", error.DisplayNames[1031]);
            Assert.Equal("Error", error.DisplayNames[1033]);
            Assert.Equal("F", error.ShortNames[1031]);
            Assert.Throws<NotSupportedException>(() => ((IDictionary<int, string>)error.DisplayNames).Add(0, "changed"));
        }

        [Fact]
        public void EmptyAndMissingMetadataDoNotInventSystemClasses()
        {
            var empty = Block(1, "<AlarmClassList />");
            var absent = new PObject(2, Ids.DB_Class_Rid, 0);
            Assert.Equal(0, S7CommPlusAlarmClassParser.Parse(new[] { absent, empty }, out var classes));
            Assert.Empty(classes);
        }

        [Theory]
        [InlineData("<AlarmClassList><AlarmClass Name='PlcError' Id='37' Prio='10' /></AlarmClassList>")]
        [InlineData("<AlarmClassList><AlarmClass Name='PlcError' Id='37' Prio='10' Ack='false' /></AlarmClassList>")]
        public void MissingFieldsRemainUnknownAndMatchingDefinitionsAreDeduplicated(string xml)
        {
            Assert.Equal(0, S7CommPlusAlarmClassParser.Parse(new[] { Block(1, xml), Block(2, xml) }, out var classes));
            Assert.Single(classes);
        }

        [Theory]
        [InlineData("<AlarmClassList>")]
        [InlineData("<Alarm />")]
        [InlineData("<!DOCTYPE AlarmClassList [<!ENTITY x 'injected'>]><AlarmClassList>&x;</AlarmClassList>")]
        [InlineData("<AlarmClassList><AlarmClass Name='Error' Id='65536'/></AlarmClassList>")]
        [InlineData("<AlarmClassList><AlarmClass Name='Error' Id='37' Prio='-1'/></AlarmClassList>")]
        [InlineData("<AlarmClassList><AlarmClass Id='37'/></AlarmClassList>")]
        [InlineData("<AlarmClassList><AlarmClass Name='Error' Id='37'><DisplayName LCID='bad'>Error</DisplayName></AlarmClass></AlarmClassList>")]
        public void InvalidMetadataFailsWithoutReturningAPartialCatalog(string xml)
        {
            var valid = Block(1, "<AlarmClassList><AlarmClass Name='Valid' Id='35'/></AlarmClassList>");
            Assert.Equal(S7Consts.errIsoInvalidPDU,
                S7CommPlusAlarmClassParser.Parse(new[] { Block(2, xml), valid }, out var classes));
            Assert.Empty(classes);
        }

        [Theory]
        [InlineData("Name='Other' Id='37' Prio='10' Ack='false'")]
        [InlineData("Name='PlcError' Id='37' Prio='8' Ack='false'")]
        [InlineData("Name='PlcError' Id='37' Prio='10' Ack='true'")]
        public void ConflictingDefinitionsAreNotSilentlyChosen(string attributes)
        {
            Assert.Equal(S7Consts.errIsoInvalidPDU, S7CommPlusAlarmClassParser.Parse(new[]
            {
                Block(1, "<AlarmClassList><AlarmClass Name='PlcError' Id='37' Prio='10' Ack='false'/></AlarmClassList>"),
                Block(2, "<AlarmClassList><AlarmClass " + attributes + "/></AlarmClassList>")
            }, out var classes));
            Assert.Empty(classes);
        }

        [Fact]
        public async Task PublicApiConnectsReadOnlyAndReportsProtocolErrors()
        {
            var session = new FakeS7CommPlusSession();
            await using var client = new S7CommPlusClient(new S7CommPlusClientOptions
            {
                Address = "127.0.0.1", AutoReconnect = false, WriteEnabled = false
            }, () => session);
            Assert.Empty(await client.GetAlarmClassesAsync());
            Assert.True(client.IsConnected);
            session.AlarmClassesHandler = () => (S7Consts.errIsoInvalidPDU, new List<S7CommPlusAlarmClass>());
            var error = await Assert.ThrowsAnyAsync<S7CommPlusException>(() => client.GetAlarmClassesAsync());
            Assert.Equal("GetAlarmClasses", error.Operation);
            Assert.Equal(S7Consts.errIsoInvalidPDU, error.ErrorCode);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public async Task SubscriptionResolvesNamesAndKeepsReceivingWhenMetadataFails(int failure)
        {
            var reads = 0;
            var fake = new FakeS7CommPlusSession
            {
                AlarmClassesHandler = () =>
                {
                    reads++;
                    if (failure == 3) throw new InvalidOperationException("metadata unavailable");
                    return (failure == 1 ? S7Consts.errIsoInvalidPDU : failure == 2 ? S7Consts.errTCPReceiveTimeout : 0,
                        new List<S7CommPlusAlarmClass> { ErrorClass() });
                },
                WaitForAlarmSubscriptionHandler = (_, _) =>
                {
                    Thread.Sleep(2);
                    return (S7Consts.errCliJobTimeout, new List<Notification>());
                }
            };
            await using var client = new S7CommPlusClient(new S7CommPlusClientOptions { Address = "127.0.0.1" }, () => fake);
            await using var subscription = await client.SubscribeAlarmsAsync(1031);
            // Publish before a handler is attached to cover buffered notifications as well.
            subscription.Publish(new Notification(2)
            {
                P2Objects = new List<PObject> { AlarmObject(37), AlarmObject(34), AlarmObject(99), AlarmObject(33) },
                P2ReturnValue = (byte)S7CommPlusNotificationReturnCode.AlarmObject
            });
            var received = new TaskCompletionSource<S7CommPlusAlarmNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
            subscription.NotificationReceived += (_, e) => received.TrySetResult(e.Notification);
            var notification = await RuntimeCompatibility.WaitAsync(received.Task, TimeSpan.FromSeconds(2));
            await subscription.StopAsync();

            Assert.Equal(failure == 0 ? "PlcError" : string.Empty, notification.Alarms[0].AlarmClassName);
            Assert.Equal(failure == 0 ? "No Acknowledgement" : string.Empty, notification.Alarms[1].AlarmClassName);
            Assert.Empty(notification.Alarms[2].AlarmClassName);
            Assert.Equal(failure == 0 ? "Acknowledgement" : string.Empty, notification.Alarms[3].AlarmClassName);
            Assert.Equal((byte)0, notification.Alarms[0].HmiInfo.Priority);
            Assert.Equal(1, reads);
            Assert.Equal(1, fake.AlarmSubscriptionCreateCount);
            Assert.Equal(1, fake.AlarmSubscriptionDeleteCount);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task InitialSnapshotUsesTheSameMetadataAsTheSubscription(bool failSecondRead)
        {
            var reads = 0;
            var fake = new FakeS7CommPlusSession
            {
                AlarmClassesHandler = () => (++reads == 2 && failSecondRead ? S7Consts.errIsoInvalidPDU : 0,
                    new List<S7CommPlusAlarmClass> { ErrorClass() })
            };
            var snapshotFake = new FakeS7CommPlusSession
            {
                ActiveAlarmsHandler = () => (0, new List<S7CommPlusAlarm>
                {
                    new S7CommPlusAlarm { HmiInfo = new S7CommPlusAlarmHmiInfo { AlarmClass = 37 } },
                    new S7CommPlusAlarm { HmiInfo = new S7CommPlusAlarmHmiInfo { AlarmClass = 34 } }
                })
            };
            await using var client = new S7CommPlusClient(new S7CommPlusClientOptions { Address = "127.0.0.1" }, () => fake);
            await using var snapshotClient = new S7CommPlusClient(new S7CommPlusClientOptions { Address = "127.0.0.1" }, () => snapshotFake);
            await using (var first = await client.SubscribeAlarmsWithSnapshotAsync(snapshotClient, 1031))
            {
                Assert.Equal("PlcError", first.ActiveAlarms[0].AlarmClassName);
                Assert.Equal("No Acknowledgement", first.ActiveAlarms[1].AlarmClassName);
                await first.Subscription.StopAsync();
            }
            await using var second = await client.SubscribeAlarmsWithSnapshotAsync(snapshotClient, 1031);
            Assert.Equal(failSecondRead ? string.Empty : "PlcError", second.ActiveAlarms[0].AlarmClassName);
            Assert.Equal(failSecondRead ? string.Empty : "No Acknowledgement", second.ActiveAlarms[1].AlarmClassName);
            Assert.Equal(2, reads);
            await second.Subscription.StopAsync();
        }

        [Fact]
        public async Task CancellationDuringMetadataReadDoesNotCreateASubscription()
        {
            using var cancellation = new CancellationTokenSource();
            var fake = new FakeS7CommPlusSession
            {
                AlarmClassesHandler = () =>
                {
                    cancellation.Cancel();
                    return (0, new List<S7CommPlusAlarmClass> { ErrorClass() });
                }
            };
            await using var client = new S7CommPlusClient(new S7CommPlusClientOptions { Address = "127.0.0.1" }, () => fake);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SubscribeAlarmsAsync(1031, cancellationToken: cancellation.Token));
            Assert.Equal(0, fake.AlarmSubscriptionCreateCount);
            Assert.True(client.IsConnected);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task StandardFallbacksRequireASuccessfulReadAndPreserveReturnedDefinitions(bool hasDefinitions)
        {
            var definitions = new List<S7CommPlusAlarmClass>();
            if (hasDefinitions)
                definitions.Add(new S7CommPlusAlarmClass(34, "Name returned by PLC", 0, false,
                    new Dictionary<int, string>(), new Dictionary<int, string>()));
            var fake = new FakeS7CommPlusSession { AlarmClassesHandler = () => (0, definitions) };
            var snapshotFake = new FakeS7CommPlusSession
            {
                ActiveAlarmsHandler = () => (0, new List<S7CommPlusAlarm>
                {
                    new S7CommPlusAlarm { HmiInfo = new S7CommPlusAlarmHmiInfo { AlarmClass = 33 } },
                    new S7CommPlusAlarm { HmiInfo = new S7CommPlusAlarmHmiInfo { AlarmClass = 34 } }
                })
            };
            await using var client = new S7CommPlusClient(new S7CommPlusClientOptions { Address = "127.0.0.1" }, () => fake);
            await using var snapshotClient = new S7CommPlusClient(new S7CommPlusClientOptions { Address = "127.0.0.1" }, () => snapshotFake);
            await using var result = await client.SubscribeAlarmsWithSnapshotAsync(snapshotClient, 1031);
            Assert.Equal("Acknowledgement", result.ActiveAlarms[0].AlarmClassName);
            Assert.Equal(hasDefinitions ? "Name returned by PLC" : "No Acknowledgement", result.ActiveAlarms[1].AlarmClassName);
            await result.Subscription.StopAsync();
        }

        private static S7CommPlusAlarmClass ErrorClass() => new S7CommPlusAlarmClass(37, "PlcError", 10, false,
            new Dictionary<int, string>(), new Dictionary<int, string>());

        private static PObject AlarmObject(ushort classId)
        {
            var obj = new PObject();
            obj.AddAttribute(Ids.ObjectVariableTypeName, new ValueWString("ExplDai_2"));
            obj.AddAttribute(Ids.DAI_CPUAlarmID, new ValueLWord(1));
            obj.AddAttribute(Ids.DAI_AllStatesInfo, new ValueUSInt(0));
            obj.AddAttribute(Ids.DAI_AlarmDomain, new ValueUInt(256));
            obj.AddAttribute(Ids.DAI_MessageType, new ValueDInt(2));
            obj.AddAttribute(Ids.DAI_SequenceCounter, new ValueUDInt(1));
            var hmi = new byte[17];
            hmi[0] = 1;
            hmi[1] = 2;
            hmi[12] = (byte)(classId >> 8);
            hmi[13] = (byte)classId;
            obj.AddAttribute(Ids.DAI_HmiInfo, new ValueBlob(0, hmi));
            obj.AddAttribute(Ids.DAI_AlarmTexts_Rid, new ValueBlobSparseArray(new Dictionary<uint, ValueBlobSparseArray.BlobEntry>()));
            var coming = new ValueStruct(Ids.AS_CGS_Class_Rid);
            coming.AddStructElement(Ids.AS_CGS_AllStatesInfo, new ValueUSInt(0));
            coming.AddStructElement(Ids.AS_CGS_Timestamp, new ValueTimestamp(0));
            coming.AddStructElement(Ids.AS_CGS_AckTimestamp, new ValueTimestamp(0));
            coming.AddStructElement(Ids.AS_CGS_AssociatedValues, new ValueBlobArray(new ValueBlob[0]));
            obj.AddAttribute(Ids.DAI_Coming, coming);
            return obj;
        }

        private static PObject Block(uint relationId, string xml)
        {
            var block = new PObject(relationId, Ids.FB_Class_Rid, 0);
            var blobs = new ValueBlobSparseArray(new Dictionary<uint, ValueBlobSparseArray.BlobEntry>
            {
                [1] = new ValueBlobSparseArray.BlobEntry { value = Encoding.UTF8.GetBytes(xml) },
                // Alarm entries share the attribute but must never be parsed as class definitions.
                [514] = new ValueBlobSparseArray.BlobEntry { value = Encoding.UTF8.GetBytes("not class XML") }
            });
            using var wire = new MemoryStream();
            blobs.Serialize(wire);
            wire.Position = 0;
            block.AddAttribute(Ids.Block_AlarmDescription, PValue.Deserialize(wire));
            return block;
        }
    }
}
