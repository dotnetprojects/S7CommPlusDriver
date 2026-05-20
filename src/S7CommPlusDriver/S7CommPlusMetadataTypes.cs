using System;

namespace S7CommPlusDriver
{
    public enum S7CommPlusBlockType
    {
        Unknown,
        DB,
        FB,
        FC,
        OB,
        UDT,
    }

    public enum S7CommPlusProgrammingLanguage : int
    {
        Undef = 0,
        STL = 1,
        LAD_CLASSIC = 2,
        FBD_CLASSIC = 3,
        SCL = 4,
        DB = 5,
        GRAPH = 6,
        SDB = 7,
        CPU_DB = 8,
        CPU_SDB = 17,
        CforS7 = 21,
        HIGRAPH = 22,
        CFC = 23,
        SFC = 24,
        S7_PDIAG = 26,
        RSE = 29,
        F_STL = 31,
        F_LAD = 32,
        F_FBD = 33,
        F_DB = 34,
        F_CALL = 35,
        TechnoDB = 37,
        F_LAD_LIB = 38,
        F_FBD_LIB = 39,
        ClassicEncryption = 41,
        FCP = 50,
        LAD_IEC = 100,
        FBD_IEC = 101,
        FLD = 102,
        UDT = 150,
        SDT = 151,
        FBT = 152,
        CB = 160,
        ST = 161,
        AX_CODE = 190,
        AX_DATA = 191,
        BMC_200 = 200,
        Motion_DB = 201,
        BMC_202 = 202,
        BMC_203 = 203,
        BMC_204 = 204,
        BMC_205 = 205,
        BMC_206 = 206,
        BMC_207 = 207,
        BMC_208 = 208,
        BMC_209 = 209,
        GRAPH_ACTIONS = 300,
        GRAPH_SEQUENCE = 301,
        GRAPH_ADDINFOS = 303,
        GRAPH_PLUS = 310,
        MC7plus = 400,
        ProDiag = 500,
        ProDiag_OB = 501,
        CEM = 600,
    }

    public sealed class S7CommPlusBlockInfo
    {
        public string Name { get; set; }
        public uint Number { get; set; }
        public S7CommPlusBlockType Type { get; set; }
        public S7CommPlusProgrammingLanguage Language { get; set; }
        public uint RelationId { get; set; }
        public uint TypeInfoRelationId { get; set; }
    }

    public sealed class S7CommPlusCpuInfo
    {
        public string PlcName { get; set; }
        public string ProjectName { get; set; }
        public Version VersionTia { get; set; }
        public Version Version2 { get; set; }
        public string CpuMlfb { get; set; }
        public string CpuSerial { get; set; }
        public Version CpuFirmware { get; set; }
    }

    internal enum S7CommPlusBinaryArtifactType : uint
    {
        Undefined = 0u,
        PlcFamily = 2147483649u,
        PlcMc7plusData = 2147483650u,
        PlcOptimizationInfoData = 2147483651u,
        PlcClosedImmediateData = 2147483652u,
        SimulatorFamily = 2147483665u,
        SimulatorMc7plusData = 2147483666u,
        SimulatorOptimizationInfoData = 2147483667u,
        SimulatorClosedImmediateData = 2147483668u,
        VirtualPlcFamilyKey = 2147483681u,
        VirtualPlcMc7plusDataKey = 2147483682u,
        VirtualPlcOptimizationInfoDataKey = 2147483683u,
        VirtualPlcClosedImmediateDataKey = 2147483684u
    }
}
