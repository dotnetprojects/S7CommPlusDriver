using System.Xml.Linq;
using System.Linq;
using Xunit;

namespace S7CommPlusDriver.Tests;

public class BlockTypeInformationTests
{
    [Fact]
    public void NativeTypeInformationPreservesFlagsAndEscapesSymbolNames()
    {
        var information = new PObject
        {
            RelationId = 0x91fe00e1,
            VartypeList = new PVartypeList { Elements = new() { new() { LID = 9, AttributeFlags = 0x80, BitoffsetinfoFlags = 0x80 } } },
            VarnameList = new PVarnameList { Names = new() { "A&B" } }
        };
        var xml = XElement.Parse(S7CommPlusProtocolSession.SerializeBlockTypeInformation(information));
        Assert.Equal("A&B", xml.Element("VarnameList")!.Element("Name")!.Value);
        Assert.Equal("128", xml.Descendants("AttributeFlags").Single().Value);
        Assert.Equal("128", xml.Descendants("BitoffsetinfoFlags").Single().Value);
    }
}
