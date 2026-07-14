using S7CommPlusDriver.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class BrowserArrayTests
    {
        [Fact]
        public void AggregatePrimitiveArrayReturnsOneItemWithBounds()
        {
            var browser = CreateBrowser(
                expandPrimitiveArrayElements: false,
                "Payload",
                Softdatatype.S7COMMP_SOFTDATATYPE_BYTE,
                new POffsetInfoType_Array1Dim
                {
                    ArrayLowerBounds = 5,
                    ArrayElementCount = 3,
                    OptimizedAddress = 100,
                    NonoptimizedAddress = 200,
                });

            var variable = Assert.Single(browser.GetVarInfoList());

            Assert.Equal("Data.Payload", variable.Name);
            Assert.Equal("8A0E0001.2A", variable.AccessSequence);
            Assert.Equal(Softdatatype.S7COMMP_SOFTDATATYPE_BYTE, variable.Softdatatype);
            Assert.Equal(100U, variable.OptAddress);
            Assert.Equal(200U, variable.NonOptAddress);
            Assert.Equal(3U, variable.ArrayElementCount);
            var dimension = Assert.Single(variable.ArrayDimensions);
            Assert.Equal(5, dimension.LowerBound);
            Assert.Equal(3U, dimension.ElementCount);
            Assert.Equal(7, dimension.UpperBound);
            Assert.True(variable.HmiVisible);
            Assert.True(variable.HmiAccessible);
            Assert.Equal(
                S7CommPlusSymbolCrc.ComputeArrayItemCrc("Payload", Softdatatype.S7COMMP_SOFTDATATYPE_BYTE, 5),
                variable.SymbolCrc);
        }

        [Fact]
        public void ExpandedPrimitiveArrayRetainsLegacyElementItems()
        {
            var browser = CreateBrowser(
                expandPrimitiveArrayElements: true,
                "Payload",
                Softdatatype.S7COMMP_SOFTDATATYPE_BYTE,
                new POffsetInfoType_Array1Dim
                {
                    ArrayLowerBounds = 5,
                    ArrayElementCount = 3,
                    OptimizedAddress = 100,
                    NonoptimizedAddress = 200,
                });

            var variables = browser.GetVarInfoList();

            Assert.Equal(new[] { "Data.Payload[5]", "Data.Payload[6]", "Data.Payload[7]" }, variables.Select(variable => variable.Name));
            Assert.Equal(new uint[] { 100, 101, 102 }, variables.Select(variable => variable.OptAddress));
            Assert.All(variables, variable =>
            {
                Assert.Equal(0U, variable.ArrayElementCount);
                Assert.Empty(variable.ArrayDimensions);
            });
        }

        [Fact]
        public void AggregateMultidimensionalArrayReportsSymbolOrderAndTotalCount()
        {
            var browser = CreateBrowser(
                expandPrimitiveArrayElements: false,
                "Matrix",
                Softdatatype.S7COMMP_SOFTDATATYPE_INT,
                new POffsetInfoType_ArrayMDim
                {
                    ArrayLowerBounds = 10,
                    ArrayElementCount = 6,
                    MdimArrayLowerBounds = new[] { 10, 20, 0, 0, 0, 0 },
                    MdimArrayElementCount = new uint[] { 3, 2, 0, 0, 0, 0 },
                });

            var variable = Assert.Single(browser.GetVarInfoList());

            Assert.Equal("Data.Matrix", variable.Name);
            Assert.Equal(6U, variable.ArrayElementCount);
            Assert.Collection(
                variable.ArrayDimensions,
                dimension =>
                {
                    Assert.Equal(20, dimension.LowerBound);
                    Assert.Equal(2U, dimension.ElementCount);
                },
                dimension =>
                {
                    Assert.Equal(10, dimension.LowerBound);
                    Assert.Equal(3U, dimension.ElementCount);
                });
        }

        [Fact]
        public void AggregateModeStillExpandsArraysOfStructures()
        {
            const uint rootTypeRelationId = 100;
            const uint elementTypeRelationId = 200;
            var rootType = CreateTypeObject(
                rootTypeRelationId,
                "Entries",
                Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT,
                new POffsetInfoType_Struct1Dim
                {
                    ArrayLowerBounds = 1,
                    ArrayElementCount = 2,
                    RelationId = elementTypeRelationId,
                });
            var elementType = CreateTypeObject(
                elementTypeRelationId,
                "Value",
                Softdatatype.S7COMMP_SOFTDATATYPE_INT,
                new POffsetInfoType_Std());
            elementType.AddAttribute(Ids.TI_TComSize, new ValueUDInt(2));

            var browser = new Browser(expandPrimitiveArrayElements: false);
            browser.AddBlockNode(eNodeType.Root, "Data", 0x8A0E0001, rootTypeRelationId);
            browser.SetTypeInfoContainerObjects(new List<PObject> { rootType, elementType });
            browser.BuildTree();
            browser.BuildFlatList();

            var variables = browser.GetVarInfoList();
            Assert.Equal(new[] { "Data.Entries[1].Value", "Data.Entries[2].Value" }, variables.Select(variable => variable.Name));
            Assert.All(variables, variable => Assert.Equal(0U, variable.ArrayElementCount));
        }

        [Fact]
        public void NestedLeafInheritsDisabledHmiFlagsFromContainingMember()
        {
            const uint rootTypeRelationId = 100;
            const uint nestedTypeRelationId = 200;
            var rootType = CreateTypeObject(
                rootTypeRelationId,
                "State",
                Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT,
                new POffsetInfoType_Struct { RelationId = nestedTypeRelationId },
                attributeFlags: 0x0000);
            var nestedType = CreateTypeObject(
                nestedTypeRelationId,
                "DdiAvailable",
                Softdatatype.S7COMMP_SOFTDATATYPE_BBOOL,
                new POffsetInfoType_Std(),
                attributeFlags: 0x0A00);

            var browser = new Browser(expandPrimitiveArrayElements: false);
            browser.AddBlockNode(eNodeType.Root, "Axes", 0x8A0E0001, rootTypeRelationId);
            browser.SetTypeInfoContainerObjects(new List<PObject> { rootType, nestedType });
            browser.BuildTree();
            browser.BuildFlatList();

            var variable = Assert.Single(browser.GetVarInfoList());
            Assert.Equal("Axes.State.DdiAvailable", variable.Name);
            Assert.False(variable.HmiVisible);
            Assert.False(variable.HmiAccessible);
        }

        /// <summary>
        /// Builds and executes a browser over one synthetic primitive PLC member.
        /// </summary>
        /// <param name="expandPrimitiveArrayElements">Selects aggregate or legacy element-expanded output.</param>
        /// <param name="memberName">The synthetic member name.</param>
        /// <param name="softdatatype">The primitive element datatype.</param>
        /// <param name="offsetInfo">The PLC array bounds and storage offsets.</param>
        /// <returns>The browser after its tree and flat-list phases have completed.</returns>
        private static Browser CreateBrowser(
            bool expandPrimitiveArrayElements,
            string memberName,
            uint softdatatype,
            POffsetInfoType offsetInfo)
        {
            const uint typeRelationId = 100;
            var typeObject = CreateTypeObject(typeRelationId, memberName, softdatatype, offsetInfo);
            var browser = new Browser(expandPrimitiveArrayElements);
            browser.AddBlockNode(eNodeType.Root, "Data", 0x8A0E0001, typeRelationId);
            browser.SetTypeInfoContainerObjects(new List<PObject> { typeObject });
            browser.BuildTree();
            browser.BuildFlatList();
            return browser;
        }

        /// <summary>
        /// Creates the minimum type-information object needed to exercise one browser member without a live PLC.
        /// </summary>
        /// <param name="relationId">The relation id used to associate this type with a root or structure member.</param>
        /// <param name="memberName">The single member name stored in the type information.</param>
        /// <param name="softdatatype">The member's S7 soft datatype.</param>
        /// <param name="offsetInfo">The member's scalar, array, or relation offset metadata.</param>
        /// <param name="attributeFlags">The raw TIA member attributes, including HMI visibility and accessibility bits.</param>
        /// <returns>A synthetic protocol type-information object.</returns>
        private static PObject CreateTypeObject(
            uint relationId,
            string memberName,
            uint softdatatype,
            POffsetInfoType offsetInfo,
            ushort attributeFlags = 0x0A00)
        {
            return new PObject
            {
                RelationId = relationId,
                VarnameList = new PVarnameList { Names = new List<string> { memberName } },
                VartypeList = new PVartypeList
                {
                    Elements = new List<PVartypeListElement>
                    {
                        new PVartypeListElement
                        {
                            LID = 0x2A,
                            Softdatatype = softdatatype,
                            AttributeFlags = attributeFlags,
                            OffsetInfoType = offsetInfo,
                        },
                    },
                },
            };
        }
    }
}
