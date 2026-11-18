using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using S7CommPlusDriver.ClientApi;
using S7CommPlusDriver.Internal;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    /// <summary>
    /// Covers the block-interface XML browse of <see cref="S7CommPlusSymbolBrowser"/>: one level per call, typed array
    /// elements, access sequences and the system-type/enum classification.
    /// </summary>
    public class S7CommPlusSymbolBrowserTests
    {
        private const string INTERFACE_XML = """
            <BlockInterface>
              <Part Kind="DBSource">
                <Payload>
                  <Root>
                    <Member ID="51" Name="flag" RID="0x02000001" StdO="0" LID="9" />
                    <Member ID="52" Name="echo" RID="0x02010001" Type="Array[0..2] of Bool" StdO="16" LID="21" />
                    <Member ID="53" Name="General" RID="0x92010000" SubPartIndex="0" StdO="24" LID="10" />
                    <Member ID="54" Name="valve" RID="0x91000001" Type="&quot;MyUdt&quot;" SubPartIndex="1" StdO="32" LID="11" />
                    <Member ID="55" Name="items" RID="0x9A080000" Type="Array[-1..0] of Struct" SubPartIndex="0" StdO="64" LID="12" />
                    <Member ID="56" Name="texts" RID="0x02010013" Type="Array[1..2] of String[16]" StdO="96" LID="13" />
                    <Member ID="57" Name="modules" RID="0x9A080000" Type="Array[1..&quot;c_i_maxNumberOfModule&quot;] of &quot;st_ModuleConfiguration&quot;" SubPartIndex="0" StdO="128" LID="14" />
                    <Member ID="58" Name="variable" RID="0x9A080000" Type="Array[*] of &quot;st_TestResult&quot;" SubPartIndex="0" StdO="144" LID="15" />
                    <Member ID="59" Name="matrix" RID="0x02010001" Type="Array[*,*] of Bool" StdO="152" LID="16" />
                  </Root>
                </Payload>
                <SubParts>
                  <Part Kind="Structure">
                    <Payload>
                      <Member>
                        <Member ID="61" Name="open_cmd" RID="0x02000001" StdO="0" LID="1" />
                        <Member ID="62" Name="speed" RID="0x02000005" StdO="16" LID="2" />
                      </Member>
                    </Payload>
                  </Part>
                  <Part Kind="DataTypeSource">
                    <Payload>
                      <Root>
                        <Member ID="63" Name="level" RID="0x02000005" StdO="0" LID="3" />
                      </Root>
                    </Payload>
                  </Part>
                </SubParts>
              </Part>
            </BlockInterface>
            """;

        private static readonly S7CommPlusBlockInfo _dbBlock = new()
        {
            Name = "MyDb",
            RelationId = 0x8A0E0005,
            Type = S7CommPlusBlockType.DB
        };

        private const string ENUM_INTERFACE_XML = """
            <BlockInterface>
              <Part Kind="DBSource">
                <Payload>
                  <Root>
                    <Member ID="51" Name="state" RID="0x02000034" Type="_.e_DA_StackState" NVT="1" StdO="0" LID="9" />
                    <Member ID="52" Name="quotedState" RID="0x02000034" Type="&quot;__e_DA_StackState&quot;" NVT="1" StdO="16" LID="10" />
                    <Member ID="53" Name="wideState" RID="0x02000036" Type="_.e_DA_InternalError" NVT="1" StdO="32" LID="11" />
                    <Member ID="54" Name="states" RID="0x02010036" Type="Array[0..1] of _.e_DA_InternalError" StdO="64" LID="12" />
                  </Root>
                </Payload>
              </Part>
            </BlockInterface>
            """;

        private const string SYSTEM_TYPE_INTERFACE_XML = """
            <BlockInterface>
              <Part Kind="DBSource">
                <Payload>
                  <Root>
                    <Member ID="51" Name="conn" RID="0x02000005" Type="TCON_IP_v4" StdO="0" LID="9" />
                    <Member ID="52" Name="plainEnum" RID="0x02000034" Type="e_Plain" NVT="1" StdO="32" LID="10" />
                    <Member ID="53" Name="connWithPart" RID="0x02000005" Type="TCON_IP_v4" SubPartIndex="0" StdO="64" LID="11" />
                  </Root>
                </Payload>
                <SubParts>
                  <Part Kind="DataTypeSource">
                    <Payload>
                      <Root>
                        <Member ID="61" Name="ActiveEstablished" RID="0x02000001" StdO="0" LID="1" />
                      </Root>
                    </Payload>
                  </Part>
                </SubParts>
              </Part>
            </BlockInterface>
            """;

        private static XElement SystemTypeRoot() => XElement.Parse(SYSTEM_TYPE_INTERFACE_XML);

        private static XElement EnumRoot() => XElement.Parse(ENUM_INTERFACE_XML);

        private static XElement Root() => XElement.Parse(INTERFACE_XML);

        private static Task<List<S7CommPlusBrowseNode>> BrowseAsync(params string[] segments) =>
            S7CommPlusBlockInterfaceReader.BrowseInDbAsync(
                Root(),
                _dbBlock.Name,
                _dbBlock.RelationId,
                segments,
                arrayDimensionCache: null,
                resolveTag: null,
                CancellationToken.None);

        [Fact]
        public void FindDbBlockAcceptsUnquotedDottedDbNameAndPrefersLongestMatch()
        {
            var blocks = new List<S7CommPlusBlockInfo>
            {
                new() { Name = "SC", RelationId = 0x8A0E0001, Type = S7CommPlusBlockType.DB },
                new() { Name = "SC.GC.db_AlarmList", RelationId = 0x8A0E0002, Type = S7CommPlusBlockType.DB }
            };

            var segments = S7CommPlusSymbolPath.Split("SC.GC.db_AlarmList.someMember");
            var block = S7CommPlusSymbolBrowser.FindDbBlock(blocks, segments, out var consumed);

            Assert.NotNull(block);
            Assert.Equal("SC.GC.db_AlarmList", block.Name);
            Assert.Equal(3, consumed);
        }

        [Fact]
        public async Task BrowseDbRootReturnsOneLevelWithTypesAndAccessSequences()
        {
            var nodes = await BrowseAsync("MyDb");

            Assert.Equal(
                new[] { "flag", "echo", "General", "valve", "items", "texts", "modules" },
                nodes.Select(node => node.Name).ToArray());

            var flag = nodes.Single(node => node.Name == "flag");
            Assert.Equal("Bool", flag.NativeTypeName);
            Assert.Equal(Softdatatype.S7COMMP_SOFTDATATYPE_BOOL, flag.Softdatatype);
            Assert.False(flag.IsStructure);
            Assert.False(flag.HasChildren);
            Assert.False(flag.IsArray);
            Assert.Equal("8A0E0005.9", flag.AccessSequence);

            var echo = nodes.Single(node => node.Name == "echo");
            Assert.Equal("Array[0..2] of Bool", echo.NativeTypeName);
            Assert.True(echo.HasChildren);
            Assert.True(echo.IsArray);
            Assert.Equal(3, echo.ArrayDimension);
            Assert.Null(echo.AccessSequence);

            var general = nodes.Single(node => node.Name == "General");
            Assert.Equal("Struct", general.NativeTypeName);
            Assert.True(general.IsStructure);
            Assert.True(general.HasChildren);
            Assert.False(general.IsArray);

            var valve = nodes.Single(node => node.Name == "valve");
            Assert.Equal("\"MyUdt\"", valve.NativeTypeName);
            Assert.True(valve.HasChildren);

            var texts = nodes.Single(node => node.Name == "texts");
            Assert.True(texts.IsArray);
            Assert.Equal(16, texts.StringLength);
            Assert.Equal(2, texts.ArrayDimension);

            var items = nodes.Single(node => node.Name == "items");
            Assert.Equal(2, items.ArrayDimension);

            var modules = nodes.Single(node => node.Name == "modules");
            Assert.Equal(
                "Array[1..\"c_i_maxNumberOfModule\"] of \"st_ModuleConfiguration\"",
                modules.NativeTypeName);
            Assert.True(modules.IsArray);
            Assert.True(modules.HasChildren);
            Assert.Null(modules.ArrayDimension);
        }

        [Fact]
        public async Task BrowsePrimitiveArrayReturnsIndexedElements()
        {
            var nodes = await BrowseAsync("MyDb", "echo");

            Assert.Equal(
                new[] { "echo[0]", "echo[1]", "echo[2]" },
                nodes.Select(node => node.Name).ToArray());
            Assert.All(nodes, node =>
            {
                Assert.False(node.HasChildren);
                Assert.Equal("Bool", node.NativeTypeName);
            });
        }

        [Fact]
        public async Task BrowseStringArrayUsesDeclaredLowerBoundAndStringLength()
        {
            var nodes = await BrowseAsync("MyDb", "texts");

            Assert.Equal(
                new[] { "texts[1]", "texts[2]" },
                nodes.Select(node => node.Name).ToArray());
            Assert.All(nodes, node =>
            {
                Assert.Equal(16, node.StringLength);
                Assert.False(node.HasChildren);
            });
        }

        [Fact]
        public async Task BrowseStructReturnsDirectMembersWithAccumulatedAccessSequence()
        {
            var nodes = await BrowseAsync("MyDb", "General");

            Assert.Equal(
                new[] { "open_cmd", "speed" },
                nodes.Select(node => node.Name).ToArray());

            var openCmd = nodes.Single(node => node.Name == "open_cmd");
            Assert.Equal("Bool", openCmd.NativeTypeName);
            Assert.Equal("8A0E0005.A.1", openCmd.AccessSequence);

            var speed = nodes.Single(node => node.Name == "speed");
            Assert.Equal("Int", speed.NativeTypeName);
            Assert.Equal("8A0E0005.A.2", speed.AccessSequence);
        }

        [Fact]
        public async Task BrowseEmbeddedUdtReturnsItsMembers()
        {
            var nodes = await BrowseAsync("MyDb", "valve");

            var level = Assert.Single(nodes);
            Assert.Equal("level", level.Name);
            Assert.Equal("Int", level.NativeTypeName);
            Assert.Equal("8A0E0005.B.3", level.AccessSequence);
        }

        [Fact]
        public async Task BrowseStructArrayReturnsElementsAndElementMembers()
        {
            var elements = await BrowseAsync("MyDb", "items");

            Assert.Equal(
                new[] { "items[-1]", "items[0]" },
                elements.Select(node => node.Name).ToArray());
            Assert.All(elements, node => Assert.True(node.HasChildren));

            var members = await BrowseAsync("MyDb", "items[-1]");

            Assert.Equal(
                new[] { "open_cmd", "speed" },
                members.Select(node => node.Name).ToArray());
            Assert.All(members, node => Assert.Null(node.AccessSequence));
        }

        [Fact]
        public async Task BrowseArrayWithSymbolicBoundsResolvesDimensionsThroughTag()
        {
            var tag = new PlcTagStruct(
                "MyDb.modules", new ItemAddress(0x8A0E0005, 0), Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT);
            tag.SetArrayDimensions(new[] { new S7CommPlusArrayDimension(1, 3) });

            var nodes = await S7CommPlusBlockInterfaceReader.BrowseInDbAsync(
                Root(),
                _dbBlock.Name,
                _dbBlock.RelationId,
                new List<string> { "MyDb", "modules" },
                arrayDimensionCache: null,
                (_, _) => Task.FromResult<PlcTag>(tag),
                CancellationToken.None);

            Assert.Equal(
                new[] { "modules[1]", "modules[2]", "modules[3]" },
                nodes.Select(node => node.Name).ToArray());
            Assert.All(nodes, node =>
            {
                Assert.True(node.HasChildren);
                Assert.Equal("\"st_ModuleConfiguration\"", node.NativeTypeName);
            });
        }

        [Fact]
        public async Task BrowseDbRootWithSymbolicArrayResolvesArrayDimension()
        {
            var tag = new PlcTagStruct(
                "MyDb.modules", new ItemAddress(0x8A0E0005, 0), Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT);
            tag.SetArrayDimensions(new[] { new S7CommPlusArrayDimension(1, 3) });

            var nodes = await S7CommPlusBlockInterfaceReader.BrowseInDbAsync(
                Root(),
                _dbBlock.Name,
                _dbBlock.RelationId,
                new List<string> { "MyDb" },
                arrayDimensionCache: null,
                (_, _) => Task.FromResult<PlcTag>(tag),
                CancellationToken.None);

            Assert.Equal(3, nodes.Single(node => node.Name == "modules").ArrayDimension);
        }

        [Fact]
        public async Task BrowseDbRootExcludesArraysWithWildcardDimensions()
        {
            var nodes = await BrowseAsync("MyDb");

            Assert.DoesNotContain(nodes, node => node.Name == "variable");
            Assert.DoesNotContain(nodes, node => node.Name == "matrix");
        }

        [Fact]
        public async Task BrowseWildcardArrayThrows()
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowseAsync("MyDb", "variable"));

            Assert.Contains("variable-length array declaration", exception.Message);
        }

        [Fact]
        public async Task BrowseMultiDimensionalWildcardArrayThrows()
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowseAsync("MyDb", "matrix"));

            Assert.Contains("variable-length array declaration", exception.Message);
        }

        [Fact]
        public async Task BrowseLeafThrows()
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => S7CommPlusBlockInterfaceReader.BrowseInDbAsync(
                    Root(),
                    _dbBlock.Name,
                    _dbBlock.RelationId,
                    new List<string> { "MyDb", "General", "speed" },
                    arrayDimensionCache: null,
                    resolveTag: null,
                    CancellationToken.None));

            Assert.Contains("is not a struct/DB and has no children", exception.Message);
        }

        [Fact]
        public async Task BrowseSystemTypeWithPrimitiveRidMarksItAsExpandable()
        {
            var nodes = await S7CommPlusBlockInterfaceReader.BrowseInDbAsync(
                SystemTypeRoot(),
                _dbBlock.Name,
                _dbBlock.RelationId,
                new List<string> { "MyDb" },
                arrayDimensionCache: null,
                resolveTag: null,
                CancellationToken.None);

            var conn = nodes.Single(node => node.Name == "conn");
            Assert.True(conn.IsStructure);
            Assert.Equal("TCON_IP_v4", conn.NativeTypeName);
            Assert.True(conn.HasChildren);
            Assert.False(conn.IsArray);

            var connWithPart = nodes.Single(node => node.Name == "connWithPart");
            Assert.True(connWithPart.IsStructure);
            Assert.True(connWithPart.HasChildren);

            // NVT still uses the primitive RID and must not become expandable.
            var plainEnum = nodes.Single(node => node.Name == "plainEnum");
            Assert.False(plainEnum.IsStructure);
            Assert.Equal(Softdatatype.S7COMMP_SOFTDATATYPE_USINT, plainEnum.Softdatatype);
            Assert.False(plainEnum.HasChildren);
        }

        [Fact]
        public async Task BrowseSystemTypeWithSubPartReturnsXmlMembers()
        {
            var nodes = await S7CommPlusBlockInterfaceReader.BrowseInDbAsync(
                SystemTypeRoot(),
                _dbBlock.Name,
                _dbBlock.RelationId,
                new List<string> { "MyDb", "connWithPart" },
                arrayDimensionCache: null,
                resolveTag: null,
                CancellationToken.None);

            var member = Assert.Single(nodes);
            Assert.Equal("ActiveEstablished", member.Name);
            Assert.Equal(Softdatatype.S7COMMP_SOFTDATATYPE_BOOL, member.Softdatatype);
            Assert.False(member.IsStructure);
            Assert.Equal("MyDb.connWithPart.ActiveEstablished", member.SymbolPath);
            Assert.False(member.HasChildren);
        }

        [Fact]
        public async Task BrowseEnumsUseBaseDatatypeInsteadOfStruct()
        {
            var nodes = await S7CommPlusBlockInterfaceReader.BrowseInDbAsync(
                EnumRoot(),
                _dbBlock.Name,
                _dbBlock.RelationId,
                new List<string> { "MyDb" },
                arrayDimensionCache: null,
                resolveTag: null,
                CancellationToken.None);

            var state = nodes.Single(node => node.Name == "state");
            Assert.False(state.IsStructure);
            Assert.Equal(Softdatatype.S7COMMP_SOFTDATATYPE_USINT, state.Softdatatype);
            Assert.Equal("_.e_DA_StackState", state.NativeTypeName);
            Assert.False(state.HasChildren);
            Assert.False(state.IsArray);

            var quotedState = nodes.Single(node => node.Name == "quotedState");
            Assert.False(quotedState.IsStructure);
            Assert.Equal(Softdatatype.S7COMMP_SOFTDATATYPE_USINT, quotedState.Softdatatype);
            Assert.Equal("\"__e_DA_StackState\"", quotedState.NativeTypeName);
            Assert.False(quotedState.HasChildren);

            var wideState = nodes.Single(node => node.Name == "wideState");
            Assert.False(wideState.IsStructure);
            Assert.Equal(Softdatatype.S7COMMP_SOFTDATATYPE_UDINT, wideState.Softdatatype);
            Assert.Equal("_.e_DA_InternalError", wideState.NativeTypeName);
            Assert.False(wideState.HasChildren);
        }

        [Fact]
        public async Task BrowseEnumArrayUsesBaseDatatypeForElements()
        {
            var nodes = await S7CommPlusBlockInterfaceReader.BrowseInDbAsync(
                EnumRoot(),
                _dbBlock.Name,
                _dbBlock.RelationId,
                new List<string> { "MyDb", "states" },
                arrayDimensionCache: null,
                resolveTag: null,
                CancellationToken.None);

            Assert.Equal(
                new[] { "states[0]", "states[1]" },
                nodes.Select(node => node.Name).ToArray());
            Assert.All(nodes, node =>
            {
                Assert.Equal(Softdatatype.S7COMMP_SOFTDATATYPE_UDINT, node.Softdatatype);
                Assert.False(node.IsStructure);
                Assert.Equal("_.e_DA_InternalError", node.NativeTypeName);
                Assert.False(node.HasChildren);
            });
        }
    }
}
