using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using S7CommPlusDriver.ClientApi;
using S7CommPlusDriver.Internal;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class ReadPipelineTests
    {
        [Theory]
        [InlineData(1, 3)]
        [InlineData(2, 1)]
        public async Task ProductionClientUsesBulkContractOnlyWhenPipeliningIsEnabled(int depth, int expectedCalls)
        {
            var session = new BulkReadSession
            {
                CachedCommunicationResources = new S7CommPlusCommunicationResourceSnapshot(100, 100),
                ReadHandler = addresses => (0, addresses.Select(_ => (object?)new ValueBool(true)).ToList(),
                    addresses.Select(_ => 0UL).ToList())
            };
            await using var client = new S7CommPlusClient(new S7CommPlusClientOptions
                { Address = "127.0.0.1", ReadPipelineDepth = depth }, () => session);
            var tags = Enumerable.Range(0, 250).Select(index => (PlcTag)new PlcTagBool(
                $"Tag{index}", new ItemAddress($"8A0E0001.{index:X}"), Softdatatype.S7COMMP_SOFTDATATYPE_BOOL)).ToArray();

            var result = await client.ReadAsync(tags);

            Assert.Equal(expectedCalls, session.ReadCount);
            Assert.Equal(250, result.Items.Count);
            Assert.All(result.Items, item => Assert.True(item.IsSuccess));
            Assert.All(tags, tag => Assert.True(Assert.IsType<PlcTagBool>(tag).Value));
        }

        [Fact]
        public async Task ProductionClientDoesNotPublishPartialTagsAfterBulkTransportFailure()
        {
            var session = new BulkReadSession
            {
                ReadHandler = addresses => (S7Consts.errTCPConnectionReset,
                    addresses.Select(_ => (object?)new ValueBool(true)).ToList(), addresses.Select(_ => 0UL).ToList())
            };
            await using var client = new S7CommPlusClient(new S7CommPlusClientOptions
                { Address = "127.0.0.1", ReadPipelineDepth = 2, AutoReconnect = false }, () => session);
            var tags = Enumerable.Range(0, 250).Select(index => new PlcTagBool(
                $"Tag{index}", new ItemAddress($"8A0E0001.{index:X}"), Softdatatype.S7COMMP_SOFTDATATYPE_BOOL)).ToArray();

            await Assert.ThrowsAsync<S7CommPlusConnectionException>(() => client.ReadAsync(tags));
            Assert.All(tags, tag => Assert.False(tag.Value));
        }

        private sealed class BulkReadSession : FakeS7CommPlusSession, IS7CommPlusBulkReadSession { }

        [Theory]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(2, true)]
        [InlineData(4, true)]
        public void WindowIsBoundedAndOutOfOrderValuesReturnInCallerOrder(int depth, bool reverse)
        {
            var harness = new PipelineHarness { Reverse = reverse, NextSequence = ushort.MaxValue - 1 };
            var result = harness.Execute(107, 10, depth, out var values, out var errors);

            Assert.Equal(0, result);
            Assert.Equal(107, values.Count);
            Assert.Equal(Enumerable.Range(0, 107).Select(i => (uint)i), values.Select(v => Assert.IsType<ValueUDInt>(v).GetValue()));
            Assert.All(errors, error => Assert.Equal(0UL, error));
            Assert.InRange(harness.MaximumPending, 1, depth);
            Assert.Equal(11, harness.RequestSizes.Count);
            Assert.Equal(7, harness.RequestSizes.Last());
            Assert.Empty(harness.Pending);
        }

        [Fact]
        public void PerItemFailuresRemainAtTheOriginalIndex()
        {
            var harness = new PipelineHarness { FailedItem = 12, Reverse = true };
            Assert.Equal(0, harness.Execute(23, 10, 2, out var values, out var errors));
            Assert.Null(values[12]);
            Assert.Equal(0x8000UL, errors[12]);
            Assert.Equal(22, values.Count(v => v != null));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void MissingOrInvalidItemNumbersCannotMasqueradeAsSuccessfulValues(bool invalidIndex)
        {
            var harness = new PipelineHarness { InvalidIndex = invalidIndex, MissingItem = !invalidIndex };
            var result = harness.Execute(23, 10, 2, out var values, out var errors);
            if (invalidIndex) Assert.Equal(S7Consts.errIsoInvalidPDU, result);
            else
            {
                Assert.Equal(0, result);
                Assert.Null(values[0]);
                Assert.Equal(ulong.MaxValue, errors[0]);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void TransportValidationAndIntegrityFailuresAbortTheLogicalRead(int failure)
        {
            var harness = new PipelineHarness { Failure = failure };
            Assert.NotEqual(0, harness.Execute(107, 10, 2, out _, out _));
            Assert.InRange(harness.RequestSizes.Count, 1, 2);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(17)]
        public void PipelineDepthIsValidatedBeforeConnecting(int depth)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new S7CommPlusClient(
                new S7CommPlusClientOptions { Address = "127.0.0.1", ReadPipelineDepth = depth }));
        }

        private sealed class PipelineHarness
        {
            internal readonly List<GetMultiVariablesRequest> Pending = new List<GetMultiVariablesRequest>();
            internal readonly List<int> RequestSizes = new List<int>();
            internal int MaximumPending;
            internal bool Reverse, InvalidIndex, MissingItem;
            internal int FailedItem = -1, Failure;
            internal ushort NextSequence;

            internal int Execute(int count, int limit, int depth, out List<object> values, out List<ulong> errors)
            {
                var addresses = Enumerable.Range(0, count).Select(i => new ItemAddress($"8A0E0001.{i:X}")).ToArray();
                return S7CommPlusReadPipeline.Execute(addresses, limit, depth, Send, Receive,
                    (_, _) => Failure == 2 ? S7Consts.errIsoInvalidPDU5 : 0, out values, out errors);
            }

            private int Send(GetMultiVariablesRequest request)
            {
                request.SequenceNumber = unchecked(++NextSequence);
                request.IntegrityId = request.SequenceNumber;
                Pending.Add(request);
                RequestSizes.Add(request.AddressList.Count);
                MaximumPending = Math.Max(MaximumPending, Pending.Count);
                return 0;
            }

            private (int Error, MemoryStream Pdu) Receive(GetMultiVariablesRequest expected, ISet<ushort> accepted)
            {
                if (Failure == 1) return (S7Consts.errCliJobTimeout, null!);
                var index = Reverse ? Pending.Count - 1 : 0;
                var request = Pending[index];
                Assert.Contains(request.SequenceNumber, accepted);
                Pending.RemoveAt(index);
                var stream = new MemoryStream();
                S7p.EncodeByte(stream, ProtocolVersion.V2);
                S7p.EncodeByte(stream, Opcode.Response);
                S7p.EncodeUInt16(stream, 0);
                S7p.EncodeUInt16(stream, Functioncode.GetMultiVariables);
                S7p.EncodeUInt16(stream, 0);
                S7p.EncodeUInt16(stream, request.SequenceNumber);
                S7p.EncodeByte(stream, S7CommPlusProtocolConstants.RequestWithResponseTransportFlags);
                S7p.EncodeUInt64Vlq(stream, FailedItem >= 0 ? 1UL : 0UL);
                for (var item = 0; item < request.AddressList.Count; item++)
                {
                    var value = request.AddressList[item].GetLocalId(0);
                    if (value == FailedItem || (MissingItem && value == 0)) continue;
                    S7p.EncodeUInt32Vlq(stream, InvalidIndex ? (uint)request.AddressList.Count + 1 : (uint)item + 1);
                    new ValueUDInt(value).Serialize(stream);
                    if (InvalidIndex) break;
                }
                S7p.EncodeUInt32Vlq(stream, 0);
                for (var item = 0; item < request.AddressList.Count; item++)
                    if (request.AddressList[item].GetLocalId(0) == FailedItem)
                    {
                        S7p.EncodeUInt32Vlq(stream, (uint)item + 1);
                        S7p.EncodeUInt64Vlq(stream, 0x8000);
                    }
                S7p.EncodeUInt32Vlq(stream, 0);
                S7p.EncodeUInt32Vlq(stream, Failure == 3 ? 0u : unchecked(request.IntegrityId + request.SequenceNumber));
                stream.Position = 0;
                return (0, stream);
            }
        }
    }
}
