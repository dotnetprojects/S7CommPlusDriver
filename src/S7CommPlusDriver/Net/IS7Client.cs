#region License
/******************************************************************************
 * S7CommPlusDriver
 * 
 * Based on Snap7 (Sharp7.cs) by Davide Nardella licensed under LGPL
 *
 /****************************************************************************/
#endregion

using OpenSsl;
using System;
using System.IO;
using System.Threading;

namespace S7CommPlusDriver
{
    public interface IS7Client
    {
        int Connect();
        int Disconnect();
        int SslActivate();
        int SetConnectionParams(string Address, ushort LocalTSAP, byte[] RemoteTSAP);

        void Send(byte[] Buffer);
        _OnDataReceived OnDataReceived { get; set; }

        delegate void _OnDataReceived(byte[] PDU, int len);
    }
}
