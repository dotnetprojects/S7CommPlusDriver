using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using S7CommPlusDriver.Internal;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    /// <summary>
    /// Pins the mapping from DB interface members to the browsable soft-datatype, especially the
    /// transport type for scalar system handles such as HW_ANY and CONN_OUC.
    /// </summary>
    public class S7CommPlusSymbolBrowserTypeMappingTests
    {
        private static readonly S7CommPlusBlockInfo _dbBlock = new()
        {
            Name = "MyDb",
            RelationId = 0x8A0E0005,
            Type = S7CommPlusBlockType.DB
        };

        private const string INTERFACE_XML = """
            <BlockInterface>
              <Part Kind="DBSource">
                <Payload>
                  <Root>
                    <Member ID="51" Name="hwId" RID="0x02000090" Type="HW_ANY" StdO="0" LID="9" />
                    <Member ID="52" Name="connId" RID="0x020000AA" Type="CONN_OUC" StdO="16" LID="10" />
                    <Member ID="53" Name="connRef" RID="0x020000AB" Type="CONN_R_ID" StdO="32" LID="11" />
                    <Member ID="54" Name="speed" RID="0x02000003" Type="Int" StdO="48" LID="12" />
                    <Member ID="55" Name="texts" RID="0x0201000B" Type="Array[1..2] of String[16]" StdO="64" LID="13" />
                    <Member ID="56" Name="state" RID="0x02000034" Type="_.e_DA_StackState" NVT="1" StdO="96" LID="14" />
                  </Root>
                </Payload>
              </Part>
            </BlockInterface>
            """;

        private static XElement Root() => XElement.Parse(INTERFACE_XML);

        private static Task<List<S7CommPlusBrowseNode>> BrowseAsync() =>
            S7CommPlusBlockInterfaceReader.BrowseInDbAsync(
                Root(),
                _dbBlock.Name,
                _dbBlock.RelationId,
                new List<string> { "MyDb" },
                arrayDimensionCache: null,
                resolveTag: null,
                CancellationToken.None);

        [Fact]
        public async Task BrowseSystemHandlesUsesTransportTypes()
        {
            var nodes = await BrowseAsync();

            var hwId = nodes.Single(node => node.Name == "hwId");
            Assert.Equal(Softdatatype.S7COMMP_SOFTDATATYPE_HWANY, hwId.Softdatatype);
            Assert.False(hwId.IsStructure);
            Assert.Equal("HW_ANY", hwId.NativeTypeName);
            Assert.False(hwId.HasChildren);
            Assert.False(hwId.IsArray);

            var connId = nodes.Single(node => node.Name == "connId");
            Assert.Equal(Softdatatype.S7COMMP_SOFTDATATYPE_CONNOUC, connId.Softdatatype);
            Assert.False(connId.IsStructure);
            Assert.Equal("CONN_OUC", connId.NativeTypeName);
            Assert.False(connId.HasChildren);

            var connRef = nodes.Single(node => node.Name == "connRef");
            Assert.Equal(Softdatatype.S7COMMP_SOFTDATATYPE_CONNRID, connRef.Softdatatype);
            Assert.False(connRef.IsStructure);
            Assert.Equal("CONN_R_ID", connRef.NativeTypeName);
            Assert.False(connRef.HasChildren);
        }

        [Fact]
        public async Task BrowseScalarEnumAndArrayUsesDeclarationMetadata()
        {
            var nodes = await BrowseAsync();

            var speed = nodes.Single(node => node.Name == "speed");
            Assert.Equal(Softdatatype.S7COMMP_SOFTDATATYPE_INT, speed.Softdatatype);
            Assert.Equal("Int", speed.NativeTypeName);

            var state = nodes.Single(node => node.Name == "state");
            Assert.Equal(Softdatatype.S7COMMP_SOFTDATATYPE_USINT, state.Softdatatype);
            Assert.False(state.IsStructure);
            Assert.Equal("_.e_DA_StackState", state.NativeTypeName);
            Assert.False(state.HasChildren);

            var texts = nodes.Single(node => node.Name == "texts");
            Assert.Equal(Softdatatype.S7COMMP_SOFTDATATYPE_STRING, texts.Softdatatype);
            Assert.True(texts.IsArray);
            Assert.Equal(2, texts.ArrayDimension);
            Assert.Equal(16, texts.StringLength);
        }
    }
}
