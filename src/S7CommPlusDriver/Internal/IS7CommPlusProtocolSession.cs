using System.IO;

namespace S7CommPlusDriver.Internal
{
    internal interface IS7CommPlusProtocolSession
    {
        int LastError { get; set; }
        int ReadTimeout { get; }
        uint SessionId { get; }
        uint SessionId2 { get; }
        MemoryStream ReceivedPdu { get; }

        int SendFunction(IS7pRequest request);
        void WaitForPdu(int timeoutMilliseconds);
        int CheckResponse(IS7pRequest request, IS7pResponse response);
        int DeleteObject(uint objectId);
        void DisconnectTransport();
        void ClearSessionIds();
    }
}
