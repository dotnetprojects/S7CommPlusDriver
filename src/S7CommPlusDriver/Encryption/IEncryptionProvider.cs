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

namespace S7CommPlusDriver.Encryption
{
    /// <summary>
    /// Abstraction for the security/encryption layer used during S7CommPlus communication.
    /// Two implementations exist:
    /// - TlsEncryptionProvider: For newer PLC firmware using TLS 1.3 (OpenSSL)
    /// - LegacyEncryptionProvider: For older PLC firmware using Siemens proprietary encryption (HarpoS7)
    /// </summary>
    public interface IEncryptionProvider
    {
        /// <summary>
        /// Whether the InitSSL request/response protocol step is needed before activation.
        /// TLS requires the InitSSL handshake; legacy encryption does not.
        /// </summary>
        bool RequiresInitSsl { get; }

        /// <summary>
        /// Activate the security channel on the given S7Client.
        /// For TLS: Initializes OpenSSL and performs the TLS 1.3 handshake.
        /// For Legacy: No-op at this stage (authentication happens later during CreateObject processing).
        /// </summary>
        /// <param name="client">The S7Client to activate encryption on</param>
        /// <returns>0 on success, error code on failure</returns>
        int ActivateChannel(S7Client client);

        /// <summary>
        /// Deactivate the security channel and release resources.
        /// For TLS: Deactivates TLS on the S7Client.
        /// For Legacy: Clears session key material.
        /// </summary>
        /// <param name="client">The S7Client to deactivate encryption on</param>
        void DeactivateChannel(S7Client client);

        /// <summary>
        /// Get the secret used for password legitimation.
        /// For TLS: Returns the OMS exporter secret from SSL_export_keying_material.
        /// For Legacy: Returns null (legacy legitimation uses a different mechanism).
        /// </summary>
        /// <param name="client">The S7Client to get the secret from</param>
        /// <returns>The secret bytes, or null if not applicable</returns>
        byte[] GetSecretForLegitimation(S7Client client);

        /// <summary>
        /// Additional overhead per PDU from the security layer, used for fragmentation calculation.
        /// For TLS: 22 bytes (5 byte TLS header + 17 byte GCM authentication tag).
        /// For Legacy: 0 bytes (no TLS encryption overhead).
        /// </summary>
        int SecurityOverheadPerPdu { get; }
    }
}
