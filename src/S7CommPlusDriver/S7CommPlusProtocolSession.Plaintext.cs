using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using S7CommPlusDriver.Internal;

namespace S7CommPlusDriver
{
    internal partial class S7CommPlusProtocolSession
    {
        /// <summary>
        /// Connects with unencrypted S7CommPlus and no CSI/Harpo challenge.
        /// Matches TIA Portal against older S7-1200 firmware (for example V2.2) that rejects InitSsl.
        /// </summary>
        private int ConnectPlaintext(S7CommPlusClientOptions options)
        {
            if (options.ConnectTimeoutMilliseconds > 0)
            {
                m_ReadTimeout = Math.Min(
                    options.RequestTimeoutMilliseconds,
                    Math.Max(1000, options.ConnectTimeoutMilliseconds - 1000));
            }

            var elapsed = Environment.TickCount;
            PrepareClient(
                options.Address,
                options.RequestTimeoutMilliseconds,
                options.Port,
                options.LocalTsap,
                options.RemoteTsapBytes);
            var res = m_client.Connect();
            if (res != 0)
            {
                return res;
            }

            options.Logger.LogDebug(
                "Plaintext S7CommPlus COTP connection established to PLC {Address}:{Port}.",
                options.Address,
                options.Port);

            // TIA always probes InitSsl first. Old firmware answers with Error2; stay on plaintext.
            res = ProbeInitSslForPlaintext();
            if (res != 0)
            {
                m_client.Disconnect();
                return res;
            }

            res = SendPlaintextCreateObjectRequest(options.LegacySessionRole);
            if (res != 0)
            {
                m_client.Disconnect();
                return res;
            }

            // TIA sends an empty DT after the CreateObject response; keep the same pacing.
            res = m_client.SendEmptyDtData();
            if (res != 0)
            {
                m_client.Disconnect();
                return res;
            }

            var createObjRes = CreateObjectResponse.DeserializeFromPdu(m_ReceivedPDU);
            if (createObjRes == null || createObjRes.ObjectIds == null || createObjRes.ObjectIds.Count == 0)
            {
                m_client.Disconnect();
                return S7Consts.errIsoInvalidPDU6;
            }

            if (IsCreateObjectError(createObjRes))
            {
                options.Logger.LogWarning(
                    "Plaintext S7CommPlus CreateObject was rejected by PLC {Address}:{Port} with return value 0x{ReturnValue:X}. Another PG/ES session may already be open.",
                    options.Address,
                    options.Port,
                    createObjRes.ReturnValue);
                m_client.Disconnect();
                return S7Consts.errIsoInvalidPDU6;
            }

            res = CompletePlaintextSession(options, createObjRes);
            if (res != 0)
            {
                return res;
            }

            options.Logger.LogInformation(
                "Plaintext S7CommPlus connection to PLC {Address}:{Port} established in {ElapsedMilliseconds} ms.",
                options.Address,
                options.Port,
                Environment.TickCount - elapsed);
            return 0;
        }

        private int SendPlaintextCreateObjectRequest(LegacyServerSessionRole serverSessionRole)
        {
            var request = new CreateObjectRequest(ProtocolVersion.V1, 0, false);
            request.SetTiaServerSessionData(serverSessionRole);
            return SendS7plusFunctionObjectAndWait(request, m_ReadTimeout);
        }

        /// <summary>
        /// Sends InitSsl and accepts either a normal InitSsl response or Error2 (TLS unsupported).
        /// Does not activate TLS in either case.
        /// </summary>
        private int ProbeInitSslForPlaintext()
        {
            var sslReq = new InitSslRequest(ProtocolVersion.V1, 0, 0);
            lock (m_RequestLock)
            lock (m_ReceiveDispatchLock)
            {
                try
                {
                    var result = SendS7plusFunctionObject(sslReq);
                    if (result != 0)
                    {
                        return result;
                    }

                    var deadline = RuntimeCompatibility.TickCount64 + Math.Max(1, m_ReadTimeout);
                    while (RuntimeCompatibility.TickCount64 < deadline)
                    {
                        var remaining = (int)Math.Min(50, Math.Max(1, deadline - RuntimeCompatibility.TickCount64));
                        result = ReceiveNextS7plusPdu(remaining, out var pdu);
                        if (result == S7Consts.errCliJobTimeout || result == S7Consts.errTCPReceiveTimeout)
                        {
                            continue;
                        }
                        if (result != 0)
                        {
                            return result;
                        }

                        if (!TryPeekS7PlusPdu(pdu, out var opcode, out var function, out var sequenceNumber)
                            || opcode != Opcode.Response
                            || sequenceNumber != sslReq.SequenceNumber)
                        {
                            continue;
                        }

                        if (function == Functioncode.InitSsl || function == Functioncode.Error2)
                        {
                            m_ReceivedPDU = pdu;
                            return 0;
                        }
                    }

                    return S7Consts.errCliJobTimeout;
                }
                finally
                {
                    if (ReferenceEquals(m_LastSentRequestForWait, sslReq))
                    {
                        m_LastSentRequestForWait = null;
                    }
                }
            }
        }

        private static bool IsCreateObjectError(CreateObjectResponse response)
        {
            if (response == null)
            {
                return true;
            }

            // Error extension flag used across S7CommPlus responses.
            if ((response.ReturnValue & 0x4000000000000000UL) != 0)
            {
                return true;
            }

            try
            {
                return response.ResponseObject?.GetAttribute(Ids.ServerSessionVersion) == null;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Finishes an already-created unencrypted server session (SetMultiVariables setup and optional password).
        /// Shared by explicit plaintext connects and legacy-challenge fallback when the PLC omits a CSI challenge.
        /// </summary>
        private int CompletePlaintextSession(S7CommPlusClientOptions options, CreateObjectResponse createObjRes)
        {
            m_SessionId = createObjRes.ObjectIds[0];
            m_SessionId2 = createObjRes.ObjectIds.Count > 1 ? createObjRes.ObjectIds[1] : 0;
            m_LegacyDigestActive = false;
            m_LegacySessionKey = null;

            var serverSession = TryGetServerSession(createObjRes);
            m_ServerSessionVersion = serverSession;
            if (serverSession == null)
            {
                m_client.Disconnect();
                return S7Consts.errCliFirmwareNotSupported;
            }

            var setMultiVarReq = new SetMultiVariablesRequest(ProtocolVersion.V2);
            setMultiVarReq.SetSessionSetupData(m_SessionId, serverSession);
            var res = SendS7plusFunctionObjectAndWait(setMultiVarReq, m_ReadTimeout);
            if (res != 0)
            {
                m_client.Disconnect();
                return res;
            }

            var setMultiVarRes = SetMultiVariablesResponse.DeserializeFromPdu(m_ReceivedPDU);
            if (setMultiVarRes == null)
            {
                m_client.Disconnect();
                return S7Consts.errIsoInvalidPDU7;
            }

            // Older firmware often lacks the modern SystemLimits layout. Keep the same conservative
            // defaults used by legacy-challenge sessions instead of risking a disconnecting probe.
            options.Logger.LogDebug(
                "Plaintext S7CommPlus connection to PLC {Address}:{Port} is using default communication limits.",
                options.Address,
                options.Port);

            if (!string.IsNullOrEmpty(options.Password) || !string.IsNullOrEmpty(options.Username))
            {
                res = legitimate(serverSession, options.Password, options.Username, allowOldFirmware: true);
                if (res != 0)
                {
                    m_client.Disconnect();
                    return res;
                }
            }

            Trace.WriteLine(
                "S7CommPlusProtocolSession - ConnectPlaintext: session 0x"
                + m_SessionId.ToString("X")
                + " ready.");
            return 0;
        }
    }
}
