using System;
using System.IO;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public class GvsOldFwFrameTests
    {
        [Fact]
        public void OldFirmwareGetVarSubstreamedMatchesTiaTrailer()
        {
            var req = new GetVarSubstreamedRequest(ProtocolVersion.V2)
            {
                SequenceNumber = 5,
                SessionId = 0x3AA,
                IntegrityId = 0,
                WithIntegrityId = true,
                UseOldFirmwareV2IntegrityFraming = true,
                InObjectId = 49,
                Address = 3753
            };
            using var ms = new MemoryStream();
            req.Serialize(ms);
            var p = ms.ToArray();
            // From heizung-tia-final GetVarSubstreamed #2 (CPUProxy / 3753), without 72 02 length header.
            var tia = Hex(
                "31 00 00 05 86 00 00 00 05 00 00 03 AA 34 00 00 00 31 20 04 01 9D 29 " +
                "00 00 04 E8 89 69 00 12 00 00 00 00 89 6A 00 13 00 89 6B 00 04 00 00 00 01 00 00 00 00");
            Assert.True(
                p.AsSpan().SequenceEqual(tia),
                "ours=" + BitConverter.ToString(p) + Environment.NewLine + "tia =" + BitConverter.ToString(tia));
        }

        private static byte[] Hex(string text)
        {
            var parts = text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var bytes = new byte[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                bytes[i] = Convert.ToByte(parts[i], 16);
            }
            return bytes;
        }
    }
}
