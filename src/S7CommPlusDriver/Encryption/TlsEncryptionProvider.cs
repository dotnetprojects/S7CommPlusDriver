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
    /// TLS 1.3 encryption provider for newer PLC firmware.
    /// This wraps the existing OpenSSL/TLS behavior and is the default provider.
    /// </summary>
    public class TlsEncryptionProvider : IEncryptionProvider
    {
        /// <summary>
        /// TLS requires the InitSSL request/response handshake step.
        /// </summary>
        public bool RequiresInitSsl => true;

        /// <summary>
        /// TLS overhead per PDU: 5 byte TLS header + 17 byte GCM authentication tag.
        /// </summary>
        public int SecurityOverheadPerPdu => 5 + 17;

        /// <summary>
        /// Activates TLS 1.3 on the S7Client via OpenSSL.
        /// </summary>
        public int ActivateChannel(S7Client client)
        {
            return client.SslActivate();
        }

        /// <summary>
        /// Deactivates TLS on the S7Client.
        /// </summary>
        public void DeactivateChannel(S7Client client)
        {
            client.SslDeactivate();
        }

        /// <summary>
        /// Returns the OMS exporter secret derived from TLS key material export.
        /// This is used for password legitimation on newer firmware.
        /// </summary>
        public byte[] GetSecretForLegitimation(S7Client client)
        {
            return client.getOMSExporterSecret();
        }
    }
}
