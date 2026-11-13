using System;
using System.Collections.Generic;
using System.IO;
using S7CommPlusDriver.Internal;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class S7CommPlusProtocolRequestsTests
    {
        [Theory]
        [InlineData(0UL, S7Consts.errS7CommPlusMetadataUnavailable)]
        [InlineData(0x8000B10700008011UL, S7Consts.errCliFunctionRefused)]
        public void MissingStructureMetadataReturnsAnErrorInsteadOfIndexingAnEmptyResponse(ulong returnValue, int expected)
        {
            var session = new RespondingProtocolSession
            {
                ResponseFactory = request =>
                {
                    using var stream = CreateResponseHeader(request, Functioncode.Explore);
                    S7p.EncodeUInt64Vlq(stream, returnValue);
                    S7p.EncodeUInt32(stream, ((ExploreRequest)request).ExploreId);
                    S7p.EncodeUInt32Vlq(stream, request.IntegrityId);
                    S7p.EncodeByte(stream, 0);
                    return stream.ToArray();
                }
            };
            var result = new S7CommPlusMetadataService(session).GetPlcStructureXml(out _);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0UL, 0)]
        [InlineData(0x8000B10700008011UL, S7Consts.errCliFunctionRefused)]
        public void CpuIdentificationReadsMlfbAndFirmwareAndPreservesRefusals(ulong returnValue, int expected)
        {
            var session = new RespondingProtocolSession
            {
                ResponseFactory = request =>
                {
                    var get = Assert.IsType<GetVarSubstreamedRequest>(request);
                    PValue value;
                    switch (get.Address)
                    {
                        case 2459: value = new ValueUSIntArray(new byte[] { 20, 0, 0, 0, 1, 0, 0, 0 }); break;
                        case 233: value = new ValueWString("CPU"); break;
                        case 3753:
                            var data = new byte[51];
                            System.Text.Encoding.ASCII.GetBytes("6ES7 515-2AM02-0AB0 ").CopyTo(data, 8);
                            System.Text.Encoding.ASCII.GetBytes("SERIAL").CopyTo(data, 28);
                            data[47] = 3; data[48] = 1; data[49] = 2;
                            value = new ValueBlob(0, data);
                            break;
                        case 8342: value = new ValueWStringArray(new[] { "V20", "", "Project" }); break;
                        default: throw new InvalidOperationException();
                    }
                    return CreateSubstreamedResponse(request, value, returnValue);
                }
            };
            var result = new S7CommPlusMetadataService(session).GetCpuInfo(out var info);
            Assert.Equal(expected, result);
            if (expected != 0) { Assert.Null(info); return; }
            Assert.Equal("6ES7 515-2AM02-0AB0", info.CpuMlfb);
            Assert.Equal(new Version(3, 1, 2, 0), info.CpuFirmware);
            Assert.Equal("SERIAL", info.CpuSerial);
            Assert.Equal("Project", info.ProjectName);
            Assert.Equal(4, session.ResponseValidationCount);
        }

        [Theory]
        [InlineData(2459)]
        [InlineData(3753)]
        public void TruncatedCpuIdentificationReturnsAProtocolError(ushort truncatedAddress)
        {
            var session = new RespondingProtocolSession
            {
                ResponseFactory = request =>
                {
                    var address = ((GetVarSubstreamedRequest)request).Address;
                    PValue value = address == 2459
                        ? new ValueUSIntArray(new byte[truncatedAddress == 2459 ? 7 : 8])
                        : address == 233 ? new ValueWString("CPU") : new ValueBlob(0, new byte[50]);
                    return CreateSubstreamedResponse(request, value, 0);
                }
            };
            Assert.Equal(S7Consts.errIsoInvalidPDU8,
                new S7CommPlusMetadataService(session).GetCpuInfo(out var info));
            Assert.Null(info);
        }

        private static byte[] CreateSubstreamedResponse(IS7pRequest request, PValue value, ulong returnValue)
        {
            using var stream = CreateResponseHeader(request, Functioncode.GetVarSubStreamed);
            S7p.EncodeUInt64Vlq(stream, returnValue);
            S7p.EncodeByte(stream, 0);
            value.Serialize(stream);
            S7p.EncodeUInt32Vlq(stream, request.IntegrityId);
            return stream.ToArray();
        }

        [Fact]
        public void AlarmClassCatalogReadsBlockEngineeringMetadataWithoutCreatingASubscription()
        {
            var session = new RespondingProtocolSession
            {
                ResponseFactory = request =>
                {
                    var explore = Assert.IsType<ExploreRequest>(request);
                    Assert.Equal((uint)Ids.NativeObjects_thePLCProgram_Rid, explore.ExploreId);
                    Assert.Equal((byte)1, explore.ExploreChildsRecursive);
                    Assert.Equal(new uint[] { Ids.Block_AlarmDescription }, explore.AddressList);
                    var block = new PObject(123, Ids.FB_Class_Rid, 0);
                    block.AddAttribute(Ids.Block_AlarmDescription, new ValueBlobSparseArray(
                        new Dictionary<uint, ValueBlobSparseArray.BlobEntry>
                        {
                            [1] = new ValueBlobSparseArray.BlobEntry
                            {
                                value = System.Text.Encoding.UTF8.GetBytes(
                                    "<AlarmClassList><AlarmClass Name='PlcError' Id='37' Ack='false' Prio='10'/></AlarmClassList>")
                            }
                        }));
                    using var stream = CreateResponseHeader(request, Functioncode.Explore);
                    S7p.EncodeUInt64Vlq(stream, 0);
                    S7p.EncodeUInt32(stream, explore.ExploreId);
                    S7p.EncodeUInt32Vlq(stream, request.IntegrityId);
                    block.Serialize(stream);
                    return stream.ToArray();
                }
            };
            var result = new S7CommPlusAlarmBrowseService(session).GetAlarmClasses(out var classes);
            Assert.Equal(0, result);
            Assert.Equal("PlcError", Assert.Single(classes).Name);
            Assert.Single(session.Requests);
        }

        [Fact]
        public void AcknowledgedSetVariableWaitsForAndValidatesTheResponse()
        {
            var session = new RespondingProtocolSession
            {
                ResponseFactory = request => CreateSetVariableResponse(request, 0)
            };
            var requests = new S7CommPlusProtocolRequests(session);

            var result = requests.SetVariableAcknowledged(123, 456, new ValueBool(true));

            Assert.Equal(0, result);
            var request = Assert.IsType<SetVariableRequest>(session.Requests[0]);
            Assert.Equal(S7CommPlusProtocolConstants.RequestWithResponseTransportFlags, request.TransportFlags);
            Assert.Equal((uint)123, request.InObjectId);
            Assert.Equal((uint)456, request.Address);
            Assert.True(Assert.IsType<ValueBool>(request.Value).GetValue());
            Assert.Equal(1, session.ResponseValidationCount);
            Assert.Equal(0, session.FireAndForgetCount);
        }

        [Fact]
        public void AcknowledgedSetVariableReportsAPlcRefusal()
        {
            var session = new RespondingProtocolSession
            {
                ResponseFactory = request => CreateSetVariableResponse(request, 0x8000B10700008011)
            };
            var requests = new S7CommPlusProtocolRequests(session);

            var result = requests.SetVariableAcknowledged(123, 456, new ValueBool(true));

            Assert.Equal(S7Consts.errCliFunctionRefused, result);
        }

        [Fact]
        public void SetMultiVariablesReportsAnItemLevelPlcRefusal()
        {
            var session = new RespondingProtocolSession
            {
                ResponseFactory = request => CreateSetMultiVariablesResponse(request, 0, 0x8000B10700008011)
            };
            var requests = new S7CommPlusProtocolRequests(session);

            var result = requests.SetMultiVariablesRaw(
                123,
                new uint[] { 456 },
                new PValue[] { new ValueBool(true) });

            Assert.Equal(S7Consts.errCliFunctionRefused, result);
        }

        [Fact]
        public void SetMultiVariablesReportsATopLevelPlcRefusal()
        {
            var session = new RespondingProtocolSession
            {
                ResponseFactory = request => CreateSetMultiVariablesResponse(request, 0x8000B10700008011, 0)
            };
            var requests = new S7CommPlusProtocolRequests(session);

            var result = requests.SetMultiVariablesRaw(
                123,
                new uint[] { 456 },
                new PValue[] { new ValueBool(true) });

            Assert.Equal(S7Consts.errCliFunctionRefused, result);
        }

        private static byte[] CreateSetVariableResponse(IS7pRequest request, ulong returnValue)
        {
            using var stream = CreateResponseHeader(request, Functioncode.SetVariable);
            S7p.EncodeUInt64Vlq(stream, returnValue);
            S7p.EncodeUInt32Vlq(stream, request.IntegrityId);
            return stream.ToArray();
        }

        private static byte[] CreateSetMultiVariablesResponse(
            IS7pRequest request,
            ulong returnValue,
            ulong itemReturnValue)
        {
            using var stream = CreateResponseHeader(request, Functioncode.SetMultiVariables);
            S7p.EncodeUInt64Vlq(stream, returnValue);
            if (itemReturnValue != 0)
            {
                S7p.EncodeUInt32Vlq(stream, 1);
                S7p.EncodeUInt64Vlq(stream, itemReturnValue);
            }
            S7p.EncodeUInt32Vlq(stream, 0);
            S7p.EncodeUInt32Vlq(stream, request.IntegrityId);
            return stream.ToArray();
        }

        private static MemoryStream CreateResponseHeader(IS7pRequest request, ushort function)
        {
            var stream = new MemoryStream();
            S7p.EncodeByte(stream, request.ProtocolVersion);
            S7p.EncodeByte(stream, Opcode.Response);
            S7p.EncodeUInt16(stream, 0);
            S7p.EncodeUInt16(stream, function);
            S7p.EncodeUInt16(stream, 0);
            S7p.EncodeUInt16(stream, request.SequenceNumber);
            S7p.EncodeByte(stream, S7CommPlusProtocolConstants.RequestWithResponseTransportFlags);
            return stream;
        }

        private sealed class RespondingProtocolSession : IS7CommPlusProtocolSession
        {
            public Func<IS7pRequest, byte[]> ResponseFactory { get; set; } = null!;
            public List<IS7pRequest> Requests { get; } = new List<IS7pRequest>();
            public int ResponseValidationCount { get; private set; }
            public int FireAndForgetCount { get; private set; }
            public int LastError { get; set; }
            public int ReadTimeout => 1000;
            public uint SessionId => 7;
            public uint SessionId2 => 8;
            public MemoryStream ReceivedPdu { get; } = new MemoryStream();

            public int SendFunction(IS7pRequest request)
            {
                FireAndForgetCount++;
                Requests.Add(request);
                return 0;
            }

            public int SendFunctionAndWait(IS7pRequest request)
            {
                Requests.Add(request);
                var response = ResponseFactory?.Invoke(request) ?? Array.Empty<byte>();
                ReceivedPdu.SetLength(0);
                ReceivedPdu.Write(response, 0, response.Length);
                ReceivedPdu.Position = 0;
                return 0;
            }

            public void WaitForPdu(int timeoutMilliseconds) => throw new NotSupportedException();

            public int WaitForNotification(
                uint subscriptionObjectId,
                int timeoutMilliseconds,
                out Notification notification)
            {
                notification = null!;
                throw new NotSupportedException();
            }

            public int CheckResponse(IS7pRequest request, IS7pResponse response)
            {
                ResponseValidationCount++;
                return 0;
            }

            public int DeleteObject(uint objectId) => throw new NotSupportedException();
            public void DisconnectTransport() => throw new NotSupportedException();
            public void ClearSessionIds() => throw new NotSupportedException();
        }
    }
}
