#region License
/******************************************************************************
 * S7CommPlusDriver
 * 
 * Copyright (C) 2023 Thomas Wiens, th.wiens@gmx.de
 *
 * This file is part of S7CommPlusDriver.
 *
 * S7CommPlusDriver is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as
 * published by the Free Software Foundation, either version 3 of the
 * License, or (at your option) any later version.
 /****************************************************************************/
#endregion

#if !NET6_0

using System;
using System.Text;
using HarpoS7;
using HarpoS7.Auth;
using HarpoS7.Keys;
using HarpoS7.PublicKeys.Impl;
using HarpoS7.Utilities.Auth;
using HarpoS7.Utilities.Extensions;
using HarpoS7.Extensions;

namespace S7CommPlusDriver.Encryption
{
    /// <summary>
    /// Legacy encryption provider for older PLC firmware that uses Siemens proprietary
    /// challenge-based authentication (pre-TLS, implemented by HarpoS7).
    /// 
    /// With legacy encryption:
    /// - No TLS/SSL is used for data encryption
    /// - Authentication is done via challenge-response using HarpoS7
    /// - Each packet includes an HMAC-SHA256 integrity digest
    /// - The session key is derived from the authentication handshake
    /// </summary>
    public class LegacyEncryptionProvider : IEncryptionProvider
    {
        private byte[] m_sessionKey;
        private byte[] m_publicKey;
        private EPublicKeyFamily m_publicKeyFamily;
        private bool m_authenticated;

        /// <summary>
        /// Legacy encryption does not use the InitSSL protocol step.
        /// </summary>
        public bool RequiresInitSsl => false;

        /// <summary>
        /// No TLS overhead with legacy encryption. Data is sent unencrypted with integrity digests.
        /// </summary>
        public int SecurityOverheadPerPdu => 0;

        /// <summary>
        /// The session key derived from the HarpoS7 authentication handshake.
        /// Available after <see cref="ProcessChallengeResponse"/> completes successfully.
        /// </summary>
        public byte[] SessionKey => m_sessionKey;

        /// <summary>
        /// Whether legacy authentication has been completed successfully.
        /// </summary>
        public bool IsAuthenticated => m_authenticated;

        /// <summary>
        /// The public key family determined from the PLC fingerprint.
        /// </summary>
        public EPublicKeyFamily PublicKeyFamily => m_publicKeyFamily;

        /// <summary>
        /// No-op for legacy encryption. The security channel is established
        /// during the CreateObject response processing via <see cref="ProcessChallengeResponse"/>.
        /// </summary>
        public int ActivateChannel(S7Client client)
        {
            // Legacy encryption doesn't activate TLS - authentication happens later
            return 0;
        }

        /// <summary>
        /// Clears session key material.
        /// </summary>
        public void DeactivateChannel(S7Client client)
        {
            if (m_sessionKey != null)
            {
                Array.Clear(m_sessionKey, 0, m_sessionKey.Length);
                m_sessionKey = null;
            }
            m_authenticated = false;
        }

        /// <summary>
        /// Returns null for legacy encryption. Legacy legitimation uses the HarpoS7
        /// <see cref="LegitimateScheme"/> directly with the session key.
        /// </summary>
        public byte[] GetSecretForLegitimation(S7Client client)
        {
            return null;
        }

        /// <summary>
        /// Processes the challenge received from the PLC during the CreateObject response.
        /// Performs the HarpoS7 authentication and produces the encrypted key blob
        /// and session key.
        /// </summary>
        /// <param name="challenge">The 20-byte challenge received from the PLC</param>
        /// <param name="fingerprint">The public key fingerprint string from the PLC (e.g., "00:181B7B0847D11694")</param>
        /// <param name="keyBlob">Output: The encrypted key blob to send back to the PLC</param>
        /// <returns>0 on success, error code on failure</returns>
        public int ProcessChallengeResponse(byte[] challenge, string fingerprint, out byte[] keyBlob)
        {
            keyBlob = null;

            try
            {
                // Determine public key family from fingerprint
                m_publicKeyFamily = fingerprint.ToPublicKeyFamily();

                // Look up the public key from the default key store
                var store = new DefaultPublicKeyStore();
                m_publicKey = new byte[store.GetPublicKeyLength(fingerprint)];
                store.ReadPublicKey(m_publicKey.AsSpan(), fingerprint);

                // Determine blob length based on family
                int blobLength = (m_publicKeyFamily == EPublicKeyFamily.PlcSim)
                    ? CommonConstants.EncryptedBlobLengthPlcSim
                    : CommonConstants.EncryptedBlobLengthRealPlc;

                keyBlob = new byte[blobLength];
                m_sessionKey = new byte[Constants.SessionKeyLength];

                // Perform HarpoS7 authentication
                LegacyAuthenticationScheme.Authenticate(
                    keyBlob.AsSpan(),
                    m_sessionKey.AsSpan(),
                    challenge.AsSpan(),
                    m_publicKey.AsSpan(),
                    m_publicKeyFamily);

                m_authenticated = true;
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("LegacyEncryptionProvider - ProcessChallengeResponse: Error: " + ex.Message);
                return S7Consts.errCliAccessDenied;
            }
        }

        /// <summary>
        /// Derives the key ID from the public key for the SetMultiVariables authentication request.
        /// </summary>
        /// <returns>8-byte public key ID</returns>
        public byte[] GetPublicKeyId()
        {
            if (m_publicKey == null) return null;
            var pubKeyId = new byte[Constants.KeyIdLength];
            m_publicKey.DeriveKeyId(pubKeyId);
            return pubKeyId;
        }

        /// <summary>
        /// Derives the key ID from the session key for the SetMultiVariables authentication request.
        /// </summary>
        /// <returns>8-byte session key ID</returns>
        public byte[] GetSessionKeyId()
        {
            if (m_sessionKey == null) return null;
            var sessionKeyId = new byte[Constants.KeyIdLength];
            m_sessionKey.DeriveKeyId(sessionKeyId);
            return sessionKeyId;
        }

        /// <summary>
        /// Calculates the HMAC-SHA256 packet integrity digest for a given packet payload.
        /// This must be included with every packet in legacy authentication mode.
        /// </summary>
        /// <param name="packetData">The S7CommPlus packet data (without header/trailer)</param>
        /// <returns>32-byte HMAC-SHA256 digest</returns>
        public byte[] CalculatePacketDigest(byte[] packetData)
        {
            if (m_sessionKey == null)
                throw new InvalidOperationException("Session key not available. Authenticate first.");

            var digest = new byte[HarpoS7.Integrity.HarpoPacketDigest.DigestLength];
            HarpoS7.Integrity.HarpoPacketDigest.CalculateDigest(
                digest.AsSpan(),
                packetData.AsSpan(),
                m_sessionKey.AsSpan());
            return digest;
        }

        /// <summary>
        /// Solves the legitimation challenge for password-protected PLCs in legacy mode.
        /// </summary>
        /// <param name="challenge">The legitimation challenge from the PLC (20 bytes)</param>
        /// <param name="password">The access password</param>
        /// <returns>The solved legitimation blob to send back to the PLC</returns>
        public byte[] SolveLegitimationChallenge(byte[] challenge, string password)
        {
            if (m_sessionKey == null || m_publicKey == null)
                throw new InvalidOperationException("Session not authenticated. Call ProcessChallengeResponse first.");

            if (m_publicKeyFamily == EPublicKeyFamily.PlcSim)
            {
                var blobData = new byte[CommonConstants.EncryptedLegitimationBlobLengthPlcSim];
                LegitimateScheme.SolveLegitimateChallengePlcSim(
                    blobData.AsSpan(),
                    challenge.AsSpan(),
                    m_publicKey.AsSpan(),
                    m_sessionKey.AsSpan(),
                    password);
                return blobData;
            }
            else
            {
                var blobData = new byte[CommonConstants.EncryptedLegitimationBlobLengthRealPlc];
                LegitimateScheme.SolveLegitimateChallengeRealPlc(
                    blobData.AsSpan(),
                    challenge.AsSpan(),
                    m_publicKey.AsSpan(),
                    m_publicKeyFamily,
                    m_sessionKey.AsSpan(),
                    password);
                return blobData;
            }
        }
    }
}

#endif
