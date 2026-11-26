using System;
using System.IO;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public class PlaintextCreateObjectResponseTests
    {
        [Fact]
        public void ParsesS71200Fw22CreateObjectResponseFromCapture()
        {
            // Captured CreateObject response from CPU 1212C FW V2.2 (Heizung).
            // Wire frame is 72 | version | length | payload; DeserializeFromPdu expects version + payload.
            var hex =
                "0132000004ca00000002361102872a8725a100000120821f0000a38169001500a3823200170000013a823b00048200823c00048140823d00048480c040823e00048480c040823f00151b313b36455337203231322d31424433302d30584230203b56322e328240001505323b35333682410003000300a200000000";
            var bytes = HexToBytes(hex);
            using var stream = new MemoryStream(bytes);

            var resp = CreateObjectResponse.DeserializeFromPdu(stream);

            Assert.NotNull(resp);
            Assert.Equal(2, resp.ObjectIds.Count);
            var session = Assert.IsType<ValueStruct>(resp.ResponseObject.GetAttribute(Ids.ServerSessionVersion));
            var paom = Assert.IsType<ValueWString>(session.GetStructElement((uint)Ids.LID_SessionVersionSystemPAOMString));
            Assert.Contains("212-1BD30-0XB0", paom.GetValue());
            Assert.Contains("V2.2", paom.GetValue());
        }

        private static byte[] HexToBytes(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            return bytes;
        }
    }
}
