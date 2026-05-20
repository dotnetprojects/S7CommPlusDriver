using HarpoS7.Utilities.Auth;
using S7CommPlusDriver.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public class LegacyChallengeHandshakeTests
    {
        [Fact]
        public void TryParseReadsChallengeFromServerSessionRequestAttribute()
        {
            var challenge = new byte[]
            {
                0xB4, 0x16, 0x81, 0xBB, 0x96, 0x66, 0xDE, 0x00, 0xCF, 0xAF,
                0xC2, 0x7B, 0x4D, 0xB2, 0x01, 0x76, 0x4B, 0xDE, 0xF8, 0x37
            };
            var response = CreateResponse("03:B07654AC9CAA4ACA", challenge);

            Assert.True(LegacyChallengeHandshake.TryParse(Array.Empty<byte>(), response, out var handshake));

            Assert.Equal((uint)0x256, handshake.SessionId);
            Assert.Equal("03:B07654AC9CAA4ACA", handshake.Fingerprint);
            Assert.Equal(EPublicKeyFamily.PlcSim, handshake.KeyFamily);
            Assert.Equal(LegacyOmsPublicKeyFamily.VPlc, handshake.OmsKeyFamily);
            Assert.Equal(challenge, handshake.Challenge);
            Assert.Equal(LegacyAuthenticationFrameKind.Auto, handshake.AuthenticationFrameKind);
        }

        [Fact]
        public void TryParseFindsChallengeByAttributeShapeInsteadOfFixedOffset()
        {
            var pdu = Convert.FromHexString(
                "720100ED32000004CA0000000136B088D0808084D68011028780809F598780809F5AA100000120821F0100" +
                "A3816900151330333A42303736353441433943414134414341" +
                "A3822B00048280808002" +
                "A3822D0015245365727665723A204F4D53505F31362E30302E30302E30335F30312E31352E30302E3031" +
                "A3822F100214B41681BB9666DE00CFAFC27B4DB201764BDEF837" +
                "A3823200170000013A823B00048800823C00048701823D000484818640823E000484818540823F00151A313B364553372053494D2D30313530302D41504C433B53342E31" +
                "824000150A323B313737323031343682410003000300A20000000072010000");
            var response = new CreateObjectResponse(1)
            {
                ObjectIds = new List<uint> { 0x256 },
                ResponseObject = new PObject()
            };

            Assert.True(LegacyChallengeHandshake.TryParse(pdu, response, out var handshake));

            Assert.Equal(new byte[]
            {
                0xB4, 0x16, 0x81, 0xBB, 0x96, 0x66, 0xDE, 0x00, 0xCF, 0xAF,
                0xC2, 0x7B, 0x4D, 0xB2, 0x01, 0x76, 0x4B, 0xDE, 0xF8, 0x37
            }, handshake.Challenge);
        }

        [Fact]
        public void TryParseSelectsV31CompactFrameForHighSessionS71500V3Response()
        {
            var pdu = Convert.FromHexString(
                "720100ED32000004CA0000000136B088D0808084DA8011028780809C508780809C51A100000120821F0100" +
                "A3816900151330303A31383142374230383437443131363934" +
                "A3822B00048280808001" +
                "A3822D0015245365727665723A204F4D53505F31342E30302E30305F36362E30372E30302E3031" +
                "A3822F10021443D26063D1F0BFDB90BAA13BD5974A91CBBF7BDE" +
                "A3823200170000013A823B00048701823C00048701823D000484818540823E000484818540823F00151A313B36455337203531352D32464E30332D304142303B56332E31" +
                "824000150A323B313737323031343682410003000300A20000000072010000");
            var response = new CreateObjectResponse(1)
            {
                ObjectIds = new List<uint> { 0x70000E50 },
                ResponseObject = new PObject()
            };

            Assert.True(LegacyChallengeHandshake.TryParse(pdu, response, out var handshake));

            Assert.Equal(EPublicKeyFamily.S71500, handshake.KeyFamily);
            Assert.Equal(LegacyOmsPublicKeyFamily.Cpu1500, handshake.OmsKeyFamily);
            Assert.Equal(LegacyAuthenticationFrameKind.S71500V31Compact, handshake.AuthenticationFrameKind);
        }

        [Fact]
        public void TryParseSelectsV31CompactFrameFromStructuredServerSessionVersion()
        {
            var challenge = new byte[]
            {
                0x43, 0xD2, 0x60, 0x63, 0xD1, 0xF0, 0xBF, 0xDB, 0x90, 0xBA,
                0xA1, 0x3B, 0xD5, 0x97, 0x4A, 0x91, 0xCB, 0xBF, 0x7B, 0xDE
            };
            var response = CreateResponse(
                "00:181B7B0847D11694",
                challenge,
                objectId: 0x70000E50,
                serverSessionVersion: "1;6ES7 515-2FN03-0AB0;V3.1");

            Assert.True(LegacyChallengeHandshake.TryParse(Array.Empty<byte>(), response, out var handshake));

            Assert.Equal("1;6ES7 515-2FN03-0AB0;V3.1", handshake.ServerSessionVersion);
            Assert.Equal(LegacyAuthenticationFrameKind.S71500V31Compact, handshake.AuthenticationFrameKind);
        }

        [Fact]
        public void CreateAuthenticationRequestSupportsPlcSimFamily()
        {
            var publicKeyId = new byte[] { 0xCA, 0x4A, 0xAA, 0x9C, 0xAC, 0x54, 0x76, 0xB0 };
            var sessionKeyId = new byte[] { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF };
            var keyBlob = new byte[216];
            for (var i = 0; i < keyBlob.Length; i++)
            {
                keyBlob[i] = (byte)i;
            }

            var request = LegacyChallengeHandshake.CreateAuthenticationRequest(
                EPublicKeyFamily.PlcSim,
                0x70000FDB,
                publicKeyId,
                sessionKeyId,
                keyBlob,
                CreateSessionVersion("2;17720146"),
                LegacyServerSessionRole.EngineeringSystem);

            Assert.NotNull(request);
            Assert.Equal(ProtocolVersion.V2, request.ProtocolVersion);
            Assert.Equal(2, request.SequenceNumber);
            Assert.Equal((uint)0x70000FDB, request.SessionId);
            Assert.Equal((uint)0x70000FDB, request.InObjectId);
            Assert.Equal(new uint[] { Ids.SessionKey, Ids.ServerSessionVersion, Ids.ServerSessionRole, Ids.LegacyAuthenticationCompatibilityFlag }, request.AddressList);

            var payload = Serialize(request);
            Assert.True(ContainsSequence(payload, Convert.FromHexString("8E220005D89DCACAE4F2D4CACA")));
            Assert.True(ContainsSequence(payload, Convert.FromHexString("8E2300048610")));
            Assert.True(ContainsSequence(payload, Convert.FromHexString("8E220005F7F3B5B8CB9D8AA301")));
            Assert.True(ContainsSequence(payload, Convert.FromHexString("8E2300048601")));
            Assert.True(ContainsSequence(payload, Convert.FromHexString("8E0D0014008158")));
            Assert.True(ContainsSequence(payload, keyBlob));
            Assert.True(ContainsSequence(payload, Convert.FromHexString("03000401")));
        }

        [Fact]
        public void CreateObjectRequestCanUseTiaServerSessionShape()
        {
            var request = new CreateObjectRequest(ProtocolVersion.V1, 1, false)
            {
                SessionId = Ids.ObjectNullServerSession
            };
            request.SetTiaServerSessionData(LegacyServerSessionRole.EngineeringSystem);
            var hmiRequest = new CreateObjectRequest(ProtocolVersion.V1, 1, false)
            {
                SessionId = Ids.ObjectNullServerSession
            };
            hmiRequest.SetTiaServerSessionData(LegacyServerSessionRole.Hmi);
            using var stream = new MemoryStream();
            using var hmiStream = new MemoryStream();

            request.Serialize(stream);
            hmiRequest.Serialize(hmiStream);

            var payload = stream.ToArray();
            var hmiPayload = hmiStream.ToArray();
            Assert.Equal(Opcode.Request, payload[0]);
            Assert.Equal(0x04CA, (payload[3] << 8) | payload[4]);
            Assert.True(ContainsSequence(payload, Convert.FromHexString("A1000000D3821F0000")));
            Assert.True(ContainsSequence(payload, Convert.FromHexString("A381690015")));
            Assert.True(ContainsSequence(payload, System.Text.Encoding.ASCII.GetBytes("S7CommPlusDriver.TCPIP.1")));
            Assert.True(ContainsSequence(payload, Convert.FromHexString("A3822B0004" + ((byte)LegacyServerSessionRole.EngineeringSystem).ToString("X2"))));
            Assert.True(ContainsSequence(hmiPayload, Convert.FromHexString("A3822B0004" + ((byte)LegacyServerSessionRole.Hmi).ToString("X2"))));
            Assert.True(ContainsSequence(payload, Convert.FromHexString("A3822C0012")));
            Assert.True(ContainsSequence(payload, System.Text.Encoding.ASCII.GetBytes("SubscriptionContainer")));
        }

        [Fact]
        public void SiemensOmsConstantsCaptureLegacySecurityVocabulary()
        {
            Assert.Equal(2, (int)LegacyOmsSecurityType.Csi);
            Assert.Equal(4, (int)LegacyOmsSecurityType.Tls);
            Assert.Equal(299, LegacyOmsConstants.ServerSessionRole);
            Assert.Equal(303, LegacyOmsConstants.ServerSessionChallenge);
            Assert.Equal(LegacyOmsConstants.ServerSessionChallenge, LegacyOmsConstants.SessionServerChallenge);
            Assert.Equal(304, LegacyOmsConstants.ServerSessionResponse);
            Assert.Equal(305, LegacyOmsConstants.ServerSessionRoles);
            Assert.Equal(306, LegacyOmsConstants.ServerSessionClientVersion);
            Assert.Equal(309, LegacyOmsConstants.ClientSessionPassword);
            Assert.Equal(310, LegacyOmsConstants.ClientSessionLegitimated);
            Assert.Equal(311, LegacyOmsConstants.ClientSessionCommunicationFormat);
            Assert.Equal(314, LegacyOmsConstants.LidSessionVersionStruct);
            Assert.Equal(315, LegacyOmsConstants.LidSessionVersionSystemOms);
            Assert.Equal(316, LegacyOmsConstants.LidSessionVersionProjectOms);
            Assert.Equal(317, LegacyOmsConstants.LidSessionVersionSystemPaom);
            Assert.Equal(318, LegacyOmsConstants.LidSessionVersionProjectPaom);
            Assert.Equal(319, LegacyOmsConstants.LidSessionVersionSystemPaomString);
            Assert.Equal(320, LegacyOmsConstants.LidSessionVersionProjectPaomString);
            Assert.Equal(321, LegacyOmsConstants.LidSessionVersionProjectFormat);
            Assert.Equal(1800, LegacyOmsConstants.StructSecurityKey);
            Assert.Equal(1801, LegacyOmsConstants.SecurityKeyVersion);
            Assert.Equal(1802, LegacyOmsConstants.SecurityKeySecurityLevel);
            Assert.Equal(1803, LegacyOmsConstants.SecurityKeyPublicKeyId);
            Assert.Equal(1804, LegacyOmsConstants.SecurityKeySymmetricKeyId);
            Assert.Equal(1805, LegacyOmsConstants.SecurityKeyEncryptedKey);
            Assert.Equal(1811, LegacyOmsConstants.EncryptionData);
            Assert.Equal(1820, LegacyOmsConstants.StructMac);
            Assert.Equal(1821, LegacyOmsConstants.MacAlgorithm);
            Assert.Equal(1822, LegacyOmsConstants.MacEncryptedKey);
            Assert.Equal(1823, LegacyOmsConstants.MacData);
            Assert.Equal(1825, LegacyOmsConstants.SecurityKeyId);
            Assert.Equal(1826, LegacyOmsConstants.SecurityKeyIdValue);
            Assert.Equal(1827, LegacyOmsConstants.SecurityKeyIdFlags);
            Assert.Equal(1828, LegacyOmsConstants.SecurityKeyIdInternalFlags);
            Assert.Equal(1830, LegacyOmsConstants.ServerSessionSessionKey);
            Assert.Equal(LegacyOmsConstants.ServerSessionSessionKey, LegacyOmsConstants.SessionKey);
            Assert.Equal(1842, LegacyOmsConstants.EffectiveProtectionLevel);
            Assert.Equal(LegacyOmsConstants.EffectiveProtectionLevel, LegacyOmsConstants.CurrentPlcSecurityLevel);
            Assert.Equal(1843, LegacyOmsConstants.ActiveProtectionLevel);
            Assert.Equal(1844, LegacyOmsConstants.ExpectedLegitimationLevel);
            Assert.Equal(1845, LegacyOmsConstants.CollaborationToken);
            Assert.Equal(1846, LegacyOmsConstants.Legitimate);
            Assert.Equal(LegacyOmsConstants.Legitimate, LegacyOmsConstants.LegacySessionSecretResponse);
            Assert.Equal(1902, LegacyOmsConstants.LegacyAuthenticationCompatibilityFlag);
            Assert.Equal(LegacyOmsConstants.SessionServerChallenge, Ids.SessionServerChallenge);
            Assert.Equal(LegacyOmsConstants.SecurityKeyPublicKeyId, Ids.SecurityKeyPublicKeyID);
            Assert.Equal(LegacyOmsConstants.SecurityKeyPublicKeyId, Ids.SecurityKeyPublicKeyId);
            Assert.Equal(LegacyOmsConstants.SecurityKeySymmetricKeyId, Ids.SecurityKeySymmetricKeyID);
            Assert.Equal(LegacyOmsConstants.SecurityKeySymmetricKeyId, Ids.SecurityKeySymmetricKeyId);
            Assert.Equal(LegacyOmsConstants.SecurityKeyEncryptedKey, Ids.SecurityKeyEncryptedKey);
            Assert.Equal(LegacyOmsConstants.MacData, Ids.MACData);
            Assert.Equal(LegacyOmsConstants.SecurityKeyId, Ids.SecurityKeyID);
            Assert.Equal(LegacyOmsConstants.SecurityKeyId, Ids.SecurityKeyId);
            Assert.Equal(LegacyOmsConstants.SecurityKeyIdValue, Ids.SecurityKeyIdValue);
            Assert.Equal(LegacyOmsConstants.SecurityKeyIdFlags, Ids.SecurityKeyIdFlags);
            Assert.Equal(LegacyOmsConstants.SecurityKeyIdInternalFlags, Ids.SecurityKeyIdInternalFlags);
            Assert.Equal(LegacyOmsConstants.SessionKey, Ids.SessionKey);
            Assert.Equal(LegacyOmsConstants.CurrentPlcSecurityLevel, Ids.CurrentPlcSecurityLevel);
            Assert.Equal(LegacyOmsConstants.ExpectedLegitimationLevel, Ids.ExpectedLegitimationLevel);
            Assert.Equal(LegacyOmsConstants.LegacySessionSecretResponse, Ids.LegacySessionSecretResponse);
            Assert.Equal(LegacyOmsConstants.LidSessionVersionStruct, Ids.LID_SessionVersionStruct);
            Assert.Equal(LegacyOmsConstants.LidSessionVersionSystemOms, Ids.LID_SessionVersionSystemOMS);
            Assert.Equal(LegacyOmsConstants.LidSessionVersionProjectOms, Ids.LID_SessionVersionProjectOMS);
            Assert.Equal(LegacyOmsConstants.LidSessionVersionSystemPaom, Ids.LID_SessionVersionSystemPAOM);
            Assert.Equal(LegacyOmsConstants.LidSessionVersionProjectPaom, Ids.LID_SessionVersionProjectPAOM);
            Assert.Equal(LegacyOmsConstants.LidSessionVersionSystemPaomString, Ids.LID_SessionVersionSystemPAOMString);
            Assert.Equal(LegacyOmsConstants.LidSessionVersionProjectPaomString, Ids.LID_SessionVersionProjectPAOMString);
            Assert.Equal(LegacyOmsConstants.LidSessionVersionProjectFormat, Ids.LID_SessionVersionProjectFormat);
            Assert.Equal(LegacyOmsConstants.LegacyAuthenticationCompatibilityFlag, Ids.LegacyAuthenticationCompatibilityFlag);
            Assert.Equal(0x0000, (int)LegacyOmsPublicKeyFamily.Cpu1500);
            Assert.Equal(0x0100, (int)LegacyOmsPublicKeyFamily.Cpu1200);
            Assert.Equal(0x0300, (int)LegacyOmsPublicKeyFamily.VPlc);
        }

        [Fact]
        public void HarpoKeyFlagsUseSiemensCsiLayout()
        {
            Assert.Equal(0x01, SiemensCsiKeyFlags.GetSymmetricKeyFlags(EPublicKeyFamily.S71500));
            Assert.Equal(0x101, SiemensCsiKeyFlags.GetSymmetricKeyFlags(EPublicKeyFamily.S71200));
            Assert.Equal(0x301, SiemensCsiKeyFlags.GetSymmetricKeyFlags(EPublicKeyFamily.PlcSim));
            Assert.Equal(0x10, SiemensCsiKeyFlags.GetCommPublicKeyFlags(EPublicKeyFamily.S71500));
            Assert.Equal(0x110, SiemensCsiKeyFlags.GetCommPublicKeyFlags(EPublicKeyFamily.S71200));
            Assert.Equal(0x310, SiemensCsiKeyFlags.GetCommPublicKeyFlags(EPublicKeyFamily.PlcSim));
            Assert.Equal("KeyFamilyCPU1500", SiemensCsiKeyFlags.GetSiemensFamilyName(EPublicKeyFamily.S71500));
        }

        [Fact]
        public void CreateAuthenticationRequestForS71500HighSessionUsesRolesWriteShape()
        {
            var publicKeyId = Convert.FromHexString("9416D147087B1B18");
            var sessionKeyId = Convert.FromHexString("4E0C313B5E08E43B");
            var keyBlob = new byte[180];
            for (var i = 0; i < keyBlob.Length; i++)
            {
                keyBlob[i] = (byte)i;
            }

            var request = LegacyChallengeHandshake.CreateAuthenticationRequest(
                EPublicKeyFamily.S71500,
                0x70000CC5,
                publicKeyId,
                sessionKeyId,
                keyBlob,
                CreateSessionVersion("2;353025"),
                LegacyServerSessionRole.EngineeringSystem);

            Assert.NotNull(request);
            Assert.Equal(new uint[] { Ids.SessionKey, Ids.ServerSessionVersion, Ids.ServerSessionRoles }, request.AddressList);
            var payload = Serialize(request);
            Assert.True(ContainsSequence(payload, Convert.FromHexString("8E2200058C86EFB0C29FA29694")));
            Assert.True(ContainsSequence(payload, Convert.FromHexString("8E23000410")));
            Assert.True(ContainsSequence(payload, Convert.FromHexString("8E2200059DF98185F1ECE28C4E")));
            Assert.True(ContainsSequence(payload, Convert.FromHexString("8E23000401")));
            Assert.True(ContainsSequence(payload, keyBlob));
            Assert.True(ContainsSequence(payload, Convert.FromHexString("032003010002")));
        }

        [Fact]
        public void CreateAuthenticationRequestForLowSessionS71500UsesTwoItemShape()
        {
            var publicKeyId = Convert.FromHexString("9416D147087B1B18");
            var sessionKeyId = Convert.FromHexString("4E0C313B5E08E43B");
            var keyBlob = new byte[180];
            for (var i = 0; i < keyBlob.Length; i++)
            {
                keyBlob[i] = (byte)i;
            }

            var request = LegacyChallengeHandshake.CreateAuthenticationRequest(
                EPublicKeyFamily.S71500,
                0x000003DF,
                publicKeyId,
                sessionKeyId,
                keyBlob,
                CreateSessionVersion("2;17253152"),
                LegacyServerSessionRole.EngineeringSystem);

            Assert.NotNull(request);
            Assert.Equal(new uint[] { Ids.SessionKey, Ids.ServerSessionVersion }, request.AddressList);
            var payload = Serialize(request);
            Assert.True(ContainsSequence(payload, Convert.FromHexString("8E2200058C86EFB0C29FA29694")));
            Assert.True(ContainsSequence(payload, Convert.FromHexString("8E2200059DF98185F1ECE28C4E")));
            Assert.True(ContainsSequence(payload, keyBlob));
        }

        [Fact]
        public void CreateAuthenticationRequestForV31S71500UsesRoleItemShape()
        {
            var publicKeyId = Convert.FromHexString("9416D147087B1B18");
            var sessionKeyId = Convert.FromHexString("4E0C313B5E08E43B");
            var keyBlob = new byte[180];
            for (var i = 0; i < keyBlob.Length; i++)
            {
                keyBlob[i] = (byte)i;
            }

            var request = LegacyChallengeHandshake.CreateAuthenticationRequest(
                EPublicKeyFamily.S71500,
                0x70000E50,
                publicKeyId,
                sessionKeyId,
                keyBlob,
                CreateSessionVersion("2;17720146"),
                LegacyServerSessionRole.EngineeringSystem,
                LegacyAuthenticationFrameKind.S71500V31Compact);

            Assert.NotNull(request);
            Assert.Equal(new uint[] { Ids.SessionKey, Ids.ServerSessionVersion, Ids.ServerSessionRole }, request.AddressList);
            var payload = Serialize(request);
            Assert.True(ContainsSequence(payload, Convert.FromHexString("8E2200058C86EFB0C29FA29694")));
            Assert.True(ContainsSequence(payload, Convert.FromHexString("8E2200059DF98185F1ECE28C4E")));
            Assert.True(ContainsSequence(payload, keyBlob));
            Assert.True(ContainsSequence(payload, Convert.FromHexString("03000401")));
        }

        [Fact]
        public void GetVarSubstreamedRequestUsesZeroUnknownFieldBeforeIntegrityId()
        {
            var request = new GetVarSubstreamedRequest(ProtocolVersion.V3)
            {
                SequenceNumber = 3,
                SessionId = 0x70000CC5,
                IntegrityId = 0,
                InObjectId = 0x70000CC5,
                Address = Ids.EffectiveProtectionLevel
            };
            using var buffer = new MemoryStream();

            request.Serialize(buffer);

            var payload = buffer.ToArray();
            Assert.Equal(
                Convert.FromHexString("31000005860000000370000CC53470000CC52004018E32000004E88969001200000000896A001300896B000400000001000000000000"),
                payload);
        }

        private static CreateObjectResponse CreateResponse(string fingerprint, byte[] challenge, uint objectId = 0x256, string serverSessionVersion = "")
        {
            var response = new CreateObjectResponse(1)
            {
                ObjectIds = new List<uint> { objectId },
                ResponseObject = new PObject()
            };
            response.ResponseObject.AddAttribute(Ids.ObjectVariableTypeName, new ValueWString(fingerprint));
            response.ResponseObject.AddAttribute(Ids.ServerSessionRequest, new ValueUSIntArray(challenge));
            if (!string.IsNullOrEmpty(serverSessionVersion))
            {
                var versionStruct = new ValueStruct(0);
                versionStruct.AddStructElement(Ids.LID_SessionVersionSystemPAOMString, new ValueWString(serverSessionVersion));
                response.ResponseObject.AddAttribute(Ids.ServerSessionVersion, versionStruct);
            }
            return response;
        }

        private static ValueStruct CreateSessionVersion(string serverSessionVersion)
        {
            var versionStruct = new ValueStruct(Ids.LID_SessionVersionStruct);
            versionStruct.AddStructElement(Ids.LID_SessionVersionSystemOMS, new ValueUDInt(1024));
            versionStruct.AddStructElement(Ids.LID_SessionVersionProjectOMS, new ValueUDInt(897));
            versionStruct.AddStructElement(Ids.LID_SessionVersionSystemPAOM, new ValueUDInt(8405696));
            versionStruct.AddStructElement(Ids.LID_SessionVersionProjectPAOM, new ValueUDInt(8405696));
            versionStruct.AddStructElement(Ids.LID_SessionVersionSystemPAOMString, new ValueWString(string.Empty));
            versionStruct.AddStructElement(Ids.LID_SessionVersionProjectPAOMString, new ValueWString(serverSessionVersion));
            versionStruct.AddStructElement(Ids.LID_SessionVersionProjectFormat, new ValueUInt(3));
            return versionStruct;
        }

        private static byte[] Serialize(SetMultiVariablesRequest request)
        {
            using var stream = new MemoryStream();
            request.Serialize(stream);
            return stream.ToArray();
        }

        private static bool ContainsSequence(byte[] haystack, byte[] needle)
        {
            for (var i = 0; i <= haystack.Length - needle.Length; i++)
            {
                if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
