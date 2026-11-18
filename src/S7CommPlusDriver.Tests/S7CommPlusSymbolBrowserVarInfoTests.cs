using System;
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
    /// Covers the system-type browse fallback: IEC timers, R_TRIG and other library types are referenced by the DB source
    /// XML without an embedded SubPart, so their members come from the driver's flat symbol snapshot instead.
    /// </summary>
    public class S7CommPlusSymbolBrowserVarInfoTests
    {
        private static readonly S7CommPlusBlockInfo _dbBlock = new()
        {
            Name = "MyDb",
            RelationId = 0x8A0E0005,
            Type = S7CommPlusBlockType.DB
        };

        private static XElement SystemNodeRoot(string name, string type, uint rid) => XElement.Parse($"""
            <BlockInterface>
              <Part Kind="DBSource">
                <Payload>
                  <Root>
                    <Member ID="51" Name="{name}" RID="0x{rid:X8}" Type="{type}" StdO="0" LID="9" />
                  </Root>
                </Payload>
              </Part>
            </BlockInterface>
            """);

        private static List<VarInfo> RTrigVarInfos() =>
        [
            new()
            {
                Name = "MyDb.trig.CLK",
                AccessSequence = "8A0E0005.B5.2B.60.1",
                Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_BOOL
            },
            new()
            {
                Name = "MyDb.trig.Q",
                AccessSequence = "8A0E0005.B5.2B.60.2",
                Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_BOOL
            },
            new()
            {
                Name = "MyDb.trig.Stat_Bit",
                AccessSequence = "8A0E0005.B5.2B.60.3",
                Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_BOOL
            }
        ];

        private static List<VarInfo> TonVarInfos() =>
        [
            new()
            {
                Name = "MyDb.timer.ST",
                AccessSequence = "8A0E0005.B5.2B.61.1",
                Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_TIME
            },
            new()
            {
                Name = "MyDb.timer.PT",
                AccessSequence = "8A0E0005.B5.2B.61.2",
                Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_TIME
            },
            new()
            {
                Name = "MyDb.timer.ET",
                AccessSequence = "8A0E0005.B5.2B.61.3",
                Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_TIME
            },
            new()
            {
                Name = "MyDb.timer.RU",
                AccessSequence = "8A0E0005.B5.2B.61.4",
                Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_BOOL
            },
            new()
            {
                Name = "MyDb.timer.IN",
                AccessSequence = "8A0E0005.B5.2B.61.5",
                Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_BOOL
            },
            new()
            {
                Name = "MyDb.timer.Q",
                AccessSequence = "8A0E0005.B5.2B.61.6",
                Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_BOOL
            }
        ];

        private static List<VarInfo> TconVarInfos() =>
        [
            new()
            {
                Name = "MyDb.conn.InterfaceId",
                AccessSequence = "8A0E0005.B8.25.1",
                Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_HWANY
            },
            new()
            {
                Name = "MyDb.conn.ID",
                AccessSequence = "8A0E0005.B8.25.2",
                Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_CONNOUC
            },
            new()
            {
                Name = "MyDb.conn.ActiveEstablished",
                AccessSequence = "8A0E0005.B8.25.3",
                Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_BOOL
            }
        ];

        private static Task<List<S7CommPlusBrowseNode>> BrowseAsync(
            XElement root,
            List<string> segments,
            Func<CancellationToken, Task<IReadOnlyList<S7CommPlusBrowseNode>>>? fallback = null) =>
            S7CommPlusBlockInterfaceReader.BrowseInDbAsync(
                root,
                _dbBlock.Name,
                _dbBlock.RelationId,
                segments,
                arrayDimensionCache: null,
                resolveTag: null,
                CancellationToken.None,
                fallback);

        [Fact]
        public async Task BrowseSystemTypeWithPrimitiveRidMarksParentExpandableAndUsesVarInfoFallback()
        {
            var parent = await BrowseAsync(
                SystemNodeRoot("conn", "TCON_IP_v4", 0x02000005),
                new List<string> { "MyDb" });

            var conn = Assert.Single(parent);
            Assert.True(conn.IsStructure);
            Assert.Equal("TCON_IP_v4", conn.NativeTypeName);
            Assert.True(conn.HasChildren);

            var nodes = await BrowseAsync(
                SystemNodeRoot("conn", "TCON_IP_v4", 0x02000005),
                new List<string> { "MyDb", "conn" },
                _ => Task.FromResult<IReadOnlyList<S7CommPlusBrowseNode>>(
                    S7CommPlusBlockInterfaceReader.BuildVarInfoChildNodes("MyDb.conn", TconVarInfos())));

            Assert.Equal(
                new[] { "InterfaceId", "ID", "ActiveEstablished" },
                nodes.Select(node => node.Name).ToArray());
            Assert.Equal(
                new[] { "HW_ANY", "CONN_OUC", "Bool" },
                nodes.Select(node => node.NativeTypeName).ToArray());
            Assert.All(nodes, node => Assert.False(node.HasChildren));
        }

        [Fact]
        public void BuildVarInfoChildNodesForRTrigReturnsItsBoolMembers()
        {
            var nodes = S7CommPlusBlockInterfaceReader.BuildVarInfoChildNodes("MyDb.trig", RTrigVarInfos());

            Assert.Equal(
                new[] { "CLK", "Q", "Stat_Bit" },
                nodes.Select(node => node.Name).ToArray());
            Assert.All(nodes, node =>
            {
                Assert.Equal(Softdatatype.S7COMMP_SOFTDATATYPE_BOOL, node.Softdatatype);
                Assert.False(node.IsStructure);
                Assert.Equal("Bool", node.NativeTypeName);
                Assert.False(node.HasChildren);
                Assert.False(node.IsArray);
            });

            Assert.Equal("MyDb.trig.CLK", nodes[0].SymbolPath);
            Assert.Equal("8A0E0005.B5.2B.60.1", nodes[0].AccessSequence);
        }

        [Fact]
        public void BuildVarInfoChildNodesForTonReturnsTimeAndBoolMembers()
        {
            var nodes = S7CommPlusBlockInterfaceReader.BuildVarInfoChildNodes("MyDb.timer", TonVarInfos());

            Assert.Equal(
                new[] { "ST", "PT", "ET", "RU", "IN", "Q" },
                nodes.Select(node => node.Name).ToArray());

            foreach (var node in nodes.Take(3))
            {
                Assert.Equal(Softdatatype.S7COMMP_SOFTDATATYPE_TIME, node.Softdatatype);
                Assert.False(node.IsStructure);
                Assert.Equal("Time", node.NativeTypeName);
                Assert.False(node.HasChildren);
                Assert.False(node.IsArray);
            }

            foreach (var node in nodes.Skip(3))
            {
                Assert.Equal(Softdatatype.S7COMMP_SOFTDATATYPE_BOOL, node.Softdatatype);
                Assert.Equal("Bool", node.NativeTypeName);
                Assert.False(node.HasChildren);
            }
        }

        [Fact]
        public void BuildVarInfoChildNodesQuotedDbNameKeepsDisplayPathButMatchesUnquotedVarInfoNames()
        {
            var nodes = S7CommPlusBlockInterfaceReader.BuildVarInfoChildNodes(
                "\"SC.Core.db_AlarmList\".trig",
                [
                    new VarInfo
                    {
                        Name = "SC.Core.db_AlarmList.trig.CLK",
                        Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_BOOL
                    }
                ]);

            var node = Assert.Single(nodes);
            Assert.Equal("CLK", node.Name);
            Assert.Equal("\"SC.Core.db_AlarmList\".trig.CLK", node.SymbolPath);
        }

        [Fact]
        public void BuildVarInfoChildNodesStructParentWithLeafDescendantsMarksItAsStruct()
        {
            var nodes = S7CommPlusBlockInterfaceReader.BuildVarInfoChildNodes(
                "MyDb.parent",
                [
                    new VarInfo
                    {
                        Name = "MyDb.parent.nested.value",
                        Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_DINT
                    }
                ]);

            var node = Assert.Single(nodes);
            Assert.Equal("nested", node.Name);
            Assert.True(node.IsStructure);
            Assert.Equal(Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT, node.Softdatatype);
            Assert.Equal("Struct", node.NativeTypeName);
            Assert.True(node.HasChildren);
            Assert.Equal("MyDb.parent.nested", node.SymbolPath);
        }

        [Fact]
        public async Task BrowseRootMarksSystemMemberWithoutSubPartAsExpandable()
        {
            var nodes = await BrowseAsync(
                SystemNodeRoot("trig", "R_TRIG", 0x93000001),
                new List<string> { "MyDb" });

            var trig = Assert.Single(nodes);
            Assert.Equal("trig", trig.Name);
            Assert.True(trig.IsStructure);
            Assert.Equal("R_TRIG", trig.NativeTypeName);
            Assert.True(trig.HasChildren);
        }

        [Fact]
        public async Task BrowseSystemMemberWithoutSubPartUsesVarInfoFallback()
        {
            var nodes = await BrowseAsync(
                SystemNodeRoot("trig", "R_TRIG", 0x93000001),
                new List<string> { "MyDb", "trig" },
                _ => Task.FromResult<IReadOnlyList<S7CommPlusBrowseNode>>(
                    S7CommPlusBlockInterfaceReader.BuildVarInfoChildNodes("MyDb.trig", RTrigVarInfos())));

            Assert.Equal(
                new[] { "CLK", "Q", "Stat_Bit" },
                nodes.Select(node => node.Name).ToArray());
        }

        [Fact]
        public async Task BrowseSystemMemberWithoutSubPartWithoutFallbackReturnsEmptyInsteadOfThrowing()
        {
            var nodes = await BrowseAsync(
                SystemNodeRoot("trig", "R_TRIG", 0x93000001),
                new List<string> { "MyDb", "trig" });

            Assert.Empty(nodes);
        }
    }
}
