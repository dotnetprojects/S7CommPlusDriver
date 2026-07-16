#if NET8_0_OR_GREATER
using HarpoS7;
using HarpoS7.Utilities.Auth;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace S7CommPlusDriver.Internal
{
    internal sealed class LegacyChallengeHandshake
    {
        private LegacyChallengeHandshake(uint sessionId, string fingerprint, byte[] challenge, EPublicKeyFamily harpoKeyFamily, LegacyOmsPublicKeyFamily omsKeyFamily, string serverSessionVersion, LegacyAuthenticationFrameKind authenticationFrameKind)
        {
            SessionId = sessionId;
            Fingerprint = fingerprint;
            Challenge = challenge;
            KeyFamily = harpoKeyFamily;
            OmsKeyFamily = omsKeyFamily;
            ServerSessionVersion = serverSessionVersion;
            AuthenticationFrameKind = authenticationFrameKind;
        }

        public uint SessionId { get; }
        public string Fingerprint { get; }
        public byte[] Challenge { get; }
        public EPublicKeyFamily KeyFamily { get; }
        public LegacyOmsPublicKeyFamily OmsKeyFamily { get; }
        public string ServerSessionVersion { get; }
        public LegacyAuthenticationFrameKind AuthenticationFrameKind { get; }

        public static bool TryParse(byte[] createObjectPdu, CreateObjectResponse createObjectResponse, out LegacyChallengeHandshake handshake)
        {
            handshake = null;
            if (!TryGetFingerprint(createObjectResponse, out var fingerprint)
                && !TryFindFingerprint(createObjectPdu, out fingerprint))
            {
                return false;
            }

            if (!TryParseFamily(fingerprint, out var harpoKeyFamily, out var omsKeyFamily))
            {
                return false;
            }

            if (!TryGetChallenge(createObjectResponse, out var challenge)
                && !TryFindChallenge(createObjectPdu, out challenge))
            {
                return false;
            }

            var sessionId = createObjectResponse.ObjectIds[0];
            TryGetServerSessionVersion(createObjectResponse, out var serverSessionVersion);

            var frameKind = SelectAuthenticationFrameKind(createObjectPdu, serverSessionVersion, harpoKeyFamily, sessionId);
            handshake = new LegacyChallengeHandshake(sessionId, fingerprint, challenge, harpoKeyFamily, omsKeyFamily, serverSessionVersion, frameKind);
            return true;
        }

        private const byte AddressArrayValueFlag = 0x20;

        public static SetMultiVariablesRequest CreateAuthenticationRequest(
            EPublicKeyFamily keyFamily,
            uint sessionId,
            ReadOnlySpan<byte> publicKeyId,
            ReadOnlySpan<byte> sessionKeyId,
            ReadOnlySpan<byte> keyBlob,
            ValueStruct serverSessionVersion,
            LegacyServerSessionRole serverSessionRole,
            LegacyAuthenticationFrameKind frameKind = LegacyAuthenticationFrameKind.Auto)
        {
            if (serverSessionVersion == null
                || publicKeyId.Length != Constants.KeyIdLength
                || sessionKeyId.Length != Constants.KeyIdLength
                || keyBlob.Length == 0)
            {
                return null;
            }

            var resolvedFrameKind = ResolveAuthenticationFrameKind(keyFamily, sessionId, frameKind);
            var request = new SetMultiVariablesRequest(ProtocolVersion.V2)
            {
                SessionId = sessionId,
                SequenceNumber = 2,
                InObjectId = sessionId,
                WithIntegrityId = false
            };

            request.AddressList.Add(Ids.SessionKey);
            request.ValueList.Add(CreateSecurityKey(keyFamily, publicKeyId, sessionKeyId, keyBlob));
            request.AddressList.Add(Ids.ServerSessionVersion);
            request.ValueList.Add(serverSessionVersion);

            if (resolvedFrameKind == LegacyAuthenticationFrameKind.S71500HighSession
                || resolvedFrameKind == LegacyAuthenticationFrameKind.PlcSimHighSession)
            {
                request.AddressList.Add(Ids.ServerSessionRoles);
                request.ValueList.Add(new ValueUIntArray(new ushort[] { (ushort)LegacyServerSessionRole.Hmi }, AddressArrayValueFlag));
            }
            else if (resolvedFrameKind == LegacyAuthenticationFrameKind.S71500V31Compact)
            {
                request.AddressList.Add(Ids.ServerSessionRole);
                request.ValueList.Add(new ValueUDInt((uint)serverSessionRole));
            }
            else if (keyFamily == EPublicKeyFamily.PlcSim)
            {
                request.AddressList.Add(Ids.ServerSessionRole);
                request.ValueList.Add(new ValueUDInt((uint)serverSessionRole));
                request.AddressList.Add(Ids.LegacyAuthenticationCompatibilityFlag);
                request.ValueList.Add(new ValueUDInt(1));
            }

            return request;
        }

        private static ValueStruct CreateSecurityKey(EPublicKeyFamily keyFamily, ReadOnlySpan<byte> publicKeyId, ReadOnlySpan<byte> sessionKeyId, ReadOnlySpan<byte> keyBlob)
        {
            var securityKey = new ValueStruct(Ids.StructSecurityKey);
            securityKey.AddStructElement(Ids.SecurityKeyVersion, new ValueUDInt(0));
            securityKey.AddStructElement(Ids.SecurityKeySecurityLevel, new ValueUSInt(0));
            securityKey.AddStructElement(Ids.SecurityKeyPublicKeyId, CreateSecurityKeyId(publicKeyId, (uint)SiemensCsiKeyFlags.GetCommPublicKeyFlags(keyFamily)));
            securityKey.AddStructElement(Ids.SecurityKeySymmetricKeyId, CreateSecurityKeyId(sessionKeyId, (uint)SiemensCsiKeyFlags.GetSymmetricKeyFlags(keyFamily)));
            securityKey.AddStructElement(Ids.SecurityKeyEncryptedKey, new ValueBlob(0, keyBlob.ToArray()));
            return securityKey;
        }

        internal static SetVariableRequest CreateSessionKeyRenewalRequest(
            EPublicKeyFamily keyFamily,
            uint sessionId,
            ReadOnlySpan<byte> publicKeyId,
            ReadOnlySpan<byte> sessionKeyId,
            ReadOnlySpan<byte> keyBlob)
        {
            if (publicKeyId.Length != Constants.KeyIdLength
                || sessionKeyId.Length != Constants.KeyIdLength
                || keyBlob.Length == 0)
            {
                return null;
            }

            return new SetVariableRequest(ProtocolVersion.V2)
            {
                InObjectId = sessionId,
                Address = Ids.SessionKey,
                Value = CreateSecurityKey(keyFamily, publicKeyId, sessionKeyId, keyBlob)
            };
        }

        private static ValueStruct CreateSecurityKeyId(ReadOnlySpan<byte> keyId, uint flags)
        {
            var key = new ValueStruct(Ids.SecurityKeyId);
            key.AddStructElement(Ids.SecurityKeyIdValue, new ValueULInt(BinaryPrimitives.ReadUInt64LittleEndian(keyId)));
            key.AddStructElement(Ids.SecurityKeyIdFlags, new ValueUDInt(flags));
            key.AddStructElement(Ids.SecurityKeyIdInternalFlags, new ValueUDInt(0));
            return key;
        }

        private static LegacyAuthenticationFrameKind SelectAuthenticationFrameKind(byte[] createObjectPdu, string serverSessionVersion, EPublicKeyFamily keyFamily, uint sessionId)
        {
            if (keyFamily == EPublicKeyFamily.PlcSim
                && sessionId >= LegacyOmsConstants.HighSessionIdStart)
            {
                return LegacyAuthenticationFrameKind.PlcSimHighSession;
            }

            if (keyFamily != EPublicKeyFamily.S71500)
            {
                return LegacyAuthenticationFrameKind.Auto;
            }

            if (sessionId < LegacyOmsConstants.HighSessionIdStart)
            {
                return LegacyAuthenticationFrameKind.S71500LowSessionCompact;
            }

            if (UsesV31CompactAuthentication(serverSessionVersion)
                || (string.IsNullOrEmpty(serverSessionVersion) && ContainsAscii(createObjectPdu, ";V3.")))
            {
                return LegacyAuthenticationFrameKind.S71500V31Compact;
            }

            return LegacyAuthenticationFrameKind.S71500HighSession;
        }

        private static bool UsesV31CompactAuthentication(string serverSessionVersion)
        {
            if (string.IsNullOrEmpty(serverSessionVersion))
            {
                return false;
            }

            var versionMarker = serverSessionVersion.LastIndexOf(";V", StringComparison.OrdinalIgnoreCase);
            if (versionMarker < 0 || versionMarker + 2 >= serverSessionVersion.Length)
            {
                return false;
            }

            var version = serverSessionVersion[(versionMarker + 2)..];
            var dot = version.IndexOf('.');
            if (dot <= 0 || !int.TryParse(version[..dot], out var major))
            {
                return false;
            }

            return major >= 3 && major < 10;
        }

        private static LegacyAuthenticationFrameKind ResolveAuthenticationFrameKind(EPublicKeyFamily keyFamily, uint sessionId, LegacyAuthenticationFrameKind frameKind)
        {
            if (frameKind != LegacyAuthenticationFrameKind.Auto)
            {
                return frameKind;
            }

            if (keyFamily == EPublicKeyFamily.S71500 && sessionId < LegacyOmsConstants.HighSessionIdStart)
            {
                return LegacyAuthenticationFrameKind.S71500LowSessionCompact;
            }

            if (keyFamily == EPublicKeyFamily.PlcSim
                && sessionId >= LegacyOmsConstants.HighSessionIdStart)
            {
                return LegacyAuthenticationFrameKind.PlcSimHighSession;
            }

            if (keyFamily != EPublicKeyFamily.S71500)
            {
                return LegacyAuthenticationFrameKind.Auto;
            }

            return LegacyAuthenticationFrameKind.S71500HighSession;
        }

        private static bool ContainsAscii(byte[] data, string value)
        {
            var needle = Encoding.ASCII.GetBytes(value);
            return data.AsSpan().IndexOf(needle) >= 0;
        }

        private static int FindSequence(byte[] haystack, byte[] needle)
        {
            for (var i = 0; i <= haystack.Length - needle.Length; i++)
            {
                if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool TryGetFingerprint(CreateObjectResponse response, out string fingerprint)
        {
            fingerprint = null;
            if (response?.ResponseObject?.Attributes == null
                || !response.ResponseObject.Attributes.TryGetValue(Ids.ObjectVariableTypeName, out var value)
                || value is not ValueWString name
                || string.IsNullOrEmpty(name.GetValue()))
            {
                return false;
            }

            fingerprint = name.GetValue();
            return true;
        }

        private static bool TryGetChallenge(CreateObjectResponse response, out byte[] challenge)
        {
            challenge = null;
            if (response?.ResponseObject?.Attributes == null
                || !response.ResponseObject.Attributes.TryGetValue(Ids.ServerSessionChallenge, out var value)
                || value is not ValueUSIntArray array)
            {
                return false;
            }

            var bytes = array.GetValue();
            if (bytes == null || bytes.Length != LegacyOmsConstants.ChallengeLength)
            {
                return false;
            }

            challenge = new byte[bytes.Length];
            Array.Copy(bytes, challenge, bytes.Length);
            return true;
        }

        private static bool TryGetServerSessionVersion(CreateObjectResponse response, out string serverSessionVersion)
        {
            serverSessionVersion = null;
            if (response?.ResponseObject?.Attributes == null
                || !response.ResponseObject.Attributes.TryGetValue(Ids.ServerSessionVersion, out var value)
                || value is not ValueStruct versionStruct)
            {
                return false;
            }

            try
            {
                if (versionStruct.GetStructElement(Ids.LID_SessionVersionSystemPAOMString) is ValueWString versionString
                    && !string.IsNullOrWhiteSpace(versionString.GetValue()))
                {
                    serverSessionVersion = versionString.GetValue();
                    return true;
                }
            }
            catch (KeyNotFoundException)
            {
            }

            return false;
        }

        private static bool TryFindChallenge(byte[] data, out byte[] challenge)
        {
            challenge = null;
            for (var i = 0; i < data.Length - 6; i++)
            {
                if (data[i] != ElementID.Attribute)
                {
                    continue;
                }

                using var stream = new MemoryStream(data, i + 1, data.Length - i - 1);
                S7p.DecodeUInt32Vlq(stream, out var attributeId);
                if (attributeId != Ids.ServerSessionChallenge)
                {
                    continue;
                }

                var valueOffset = i + 1 + (int)stream.Position;
                if (valueOffset + 3 + LegacyOmsConstants.ChallengeLength > data.Length
                    || data[valueOffset] != 0x10
                    || data[valueOffset + 1] != Datatype.USInt
                    || data[valueOffset + 2] != LegacyOmsConstants.ChallengeLength)
                {
                    continue;
                }

                challenge = new byte[LegacyOmsConstants.ChallengeLength];
                Array.Copy(data, valueOffset + 3, challenge, 0, challenge.Length);
                return true;
            }

            return false;
        }

        private static bool TryFindFingerprint(byte[] data, out string fingerprint)
        {
            fingerprint = null;
            for (var i = 0; i <= data.Length - 19; i++)
            {
                if (!IsDigit(data[i]) || !IsDigit(data[i + 1]) || data[i + 2] != (byte)':')
                {
                    continue;
                }

                var length = 3;
                while (i + length < data.Length && length < 24 && IsHex(data[i + length]))
                {
                    length++;
                }

                if (length >= 19)
                {
                    fingerprint = Encoding.ASCII.GetString(data, i, length);
                    return true;
                }
            }
            return false;
        }

        private static bool TryParseFamily(string fingerprint, out EPublicKeyFamily harpoKeyFamily, out LegacyOmsPublicKeyFamily omsKeyFamily)
        {
            harpoKeyFamily = default;
            omsKeyFamily = default;
            if (fingerprint.Length < 2 || !int.TryParse(fingerprint.Substring(0, 2), out var family))
            {
                return false;
            }
            if (!Enum.IsDefined(typeof(EPublicKeyFamily), family))
            {
                return false;
            }
            harpoKeyFamily = (EPublicKeyFamily)family;
            if (!TryMapHarpoFamilyToOmsFamily(harpoKeyFamily, out omsKeyFamily))
            {
                return false;
            }

            return true;
        }

        private static bool TryMapHarpoFamilyToOmsFamily(EPublicKeyFamily harpoKeyFamily, out LegacyOmsPublicKeyFamily omsKeyFamily)
        {
            switch (harpoKeyFamily)
            {
                case EPublicKeyFamily.S71500:
                    omsKeyFamily = LegacyOmsPublicKeyFamily.Cpu1500;
                    return true;
                case EPublicKeyFamily.S71200:
                    omsKeyFamily = LegacyOmsPublicKeyFamily.Cpu1200;
                    return true;
                case EPublicKeyFamily.PlcSim:
                    omsKeyFamily = LegacyOmsPublicKeyFamily.VPlc;
                    return true;
                default:
                    omsKeyFamily = default;
                    return false;
            }
        }

        private static bool IsDigit(byte value)
        {
            return value >= (byte)'0' && value <= (byte)'9';
        }

        private static bool IsHex(byte value)
        {
            return (value >= (byte)'0' && value <= (byte)'9')
                || (value >= (byte)'A' && value <= (byte)'F')
                || (value >= (byte)'a' && value <= (byte)'f');
        }
    }
}
#endif
