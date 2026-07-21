#region License
/******************************************************************************
 * S7CommPlusDriver
 *
 * Copyright (C) 2023 Thomas Wiens, th.wiens@gmx.de
 *
 * This file is part of S7CommPlusDriver.
 *
 * S7CommPlusDriver is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as
 * published by the Free Software Foundation, either version 3 of the
 * License, or (at your option) any later version.
 /****************************************************************************/
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using S7CommPlusDriver.Internal;

namespace S7CommPlusDriver
{
    internal sealed class S7CommPlusMetadataService
    {
        private readonly IS7CommPlusProtocolSession _session;
        private readonly S7CommPlusProtocolRequests _requests;

        public S7CommPlusMetadataService(IS7CommPlusProtocolSession session)
        {
            _session = session;
            _requests = new S7CommPlusProtocolRequests(session);
        }


        public int BrowseBlocks(out List<S7CommPlusBlockInfo> exploreData)
        {
            int res;
            Browser vars = new Browser();
            ExploreRequest exploreReq;
            ExploreResponse exploreRes;

            #region Read all objects

            exploreData = new List<S7CommPlusBlockInfo>();

            exploreReq = new ExploreRequest(ProtocolVersion.V2);
            exploreReq.ExploreId = Ids.NativeObjects_thePLCProgram_Rid;
            exploreReq.ExploreRequestId = Ids.None;
            exploreReq.ExploreChildsRecursive = 1;
            exploreReq.ExploreParents = 0;

            // We want to know the following attributes
            exploreReq.AddressList.Add(Ids.ObjectVariableTypeName);
            exploreReq.AddressList.Add(Ids.Block_BlockNumber);
            exploreReq.AddressList.Add(Ids.Block_BlockLanguage);

            res = _requests.SendExplore(exploreReq, out exploreRes);
            if (res != 0)
            {
                return res;
            }

            #endregion

            #region Evaluate all data blocks that then need to be browsed

            var obj = exploreRes.Objects.First(o => o.ClassId == Ids.PLCProgram_Class_Rid);
            foreach (var ob in obj.GetObjects())
            {
                switch (ob.ClassId)
                {
                    case Ids.DB_Class_Rid:
                    case Ids.FB_Class_Rid:
                    case Ids.FC_Class_Rid:
                    case Ids.OB_Class_Rid:
                    case 2637:
                    case 2639:
                    case 2640:
                    case 2641:
                    case 2642:
                    case 2643:
                    case 2644:
                    case 2645:
                    case 2646:
                    case 2647:
                    case 2648:
                    case 2649:
                    case 2650:
                    case 2651:
                    case 2652:
                    case 2653:
                    case 2654:
                    case 2655:
                    case 2656:
                    case 2657:
                    case 2658:
                    case 8440:
                        UInt32 relid = ob.RelationId;
                        UInt32 area = (relid >> 16);
                        UInt32 num = relid & 0xffff;

                        var name = (ValueWString)(ob.GetAttribute(Ids.ObjectVariableTypeName));
                        var data = new S7CommPlusBlockInfo();
                        data.RelationId = relid;
                        data.Name = name.GetValue();
                        data.Number = num;
                        data.Type = GetBlockTypeFromClassId(ob.ClassId);

                        var lang = ((ValueUInt)ob.Attributes[Ids.Block_BlockLanguage]).GetValue();
                        data.Language = (S7CommPlusProgrammingLanguage)lang;
                        exploreData.Add(data);
                        break;
                }
            }

            #endregion

            return 0;
        }

        public int GetPlcStructureXml(out S7CommPlusPlcStructureSnapshot plcStructure)
        {
            int res;
            Browser vars = new Browser();
            ExploreRequest exploreReq;
            ExploreResponse exploreRes;
            plcStructure = PlcStructureXmlParser.CreateSnapshot(string.Empty);

            #region Read all objects

            exploreReq = new ExploreRequest(ProtocolVersion.V2);
            exploreReq.ExploreId = Ids.Constants | 0x0000ffff;
            exploreReq.ExploreRequestId = Ids.None;
            exploreReq.ExploreChildsRecursive = 1;
            exploreReq.ExploreParents = 1;

            exploreReq.AddressList.Add(Ids.ConstantsGlobal_Symbolics);

            res = _requests.SendExplore(exploreReq, out exploreRes);
            if (res != 0)
            {
                return res;
            }

            #endregion

            var attr = exploreRes?.Objects?[0]?.Objects?.First().Value?.Objects?.First().Value?.Attributes?[Ids.ConstantsGlobal_Symbolics] as ValueBlob;
            if (attr != null)
            {
                BlobDecompressor bd3 = new BlobDecompressor();
                var v = attr.GetValue();
                var xml = bd3.decompress(v, 0);
                plcStructure = PlcStructureXmlParser.CreateSnapshot(xml);
            }

            return 0;
        }

        public int GetBlockContent(uint relid, out S7CommPlusClientBlockContent blockContent)
        {
            int res;
            // With requesting DataInterface_InterfaceDescription, whe would be able to get all informations like the access ids and
            // datatype informations, that we get from the other browsing method. Needs to be tested which one is more efficient on network traffic or plc load.
            // If we keep use browsing for the comments, at least we would be able to read all information in one request.
            blockContent = null;
            var xmlLineComment = String.Empty;
            var xmlComments = new Dictionary<uint, string>();
            var interfaceDescription = String.Empty;
            var blockBody = Array.Empty<string>();
            var functionalObjectCode = String.Empty;
            var functionalObjectCodeBytes = Array.Empty<byte>();
            var functionalObjectDebugInfo = String.Empty;
            var internalReferences = Array.Empty<string>();
            var externalReferences = Array.Empty<string>();
            var codeModifiedTimestampBytes = Array.Empty<byte>();
            var binaryArtifacts = new Dictionary<uint, byte[]>();
            var onlineMetadata = new Dictionary<uint, string>();
            var networkComments = new Dictionary<uint, string>();
            var networkTitles = new Dictionary<uint, string>();
            string blockName = null;
            var lang = S7CommPlusProgrammingLanguage.Undef;
            var blockNumber = relid & 0xffff;
            var blockType = S7CommPlusBlockType.Unknown;

            var exploreReq = new ExploreRequest(ProtocolVersion.V2);
            exploreReq.ExploreId = relid;
            exploreReq.ExploreRequestId = Ids.None;
            exploreReq.ExploreChildsRecursive = 1;
            exploreReq.ExploreParents = 0;

            // We want to know the following attributes
            exploreReq.AddressList.Add(Ids.ObjectVariableTypeName);
            //exploreReq.AddressList.Add(Ids.Block_BlockNumber);
            exploreReq.AddressList.Add(Ids.Block_BlockLanguage);
            exploreReq.AddressList.Add(Ids.Block_RuntimeModified);
            exploreReq.AddressList.Add(Ids.Block_CRC);
            exploreReq.AddressList.Add(Ids.Block_FunctionalSignature);

            exploreReq.AddressList.Add(Ids.ASObjectES_Comment);
            exploreReq.AddressList.Add(Ids.DataInterface_LineComments);
            exploreReq.AddressList.Add(Ids.DataInterface_InterfaceDescription);
            exploreReq.AddressList.Add(Ids.Block_BodyDescription);
            exploreReq.AddressList.Add(Ids.FunctionalObject_Code);
            exploreReq.AddressList.Add(Ids.FunctionalObject_ParameterModified);
            exploreReq.AddressList.Add(Ids.FunctionalObject_InterfaceSignature);
            exploreReq.AddressList.Add(Ids.FunctionalObject_NetworkComments);
            exploreReq.AddressList.Add(Ids.FunctionalObject_NetworkTitles);
            exploreReq.AddressList.Add(Ids.FunctionalObject_DebugInfo);
            exploreReq.AddressList.Add(Ids.FunctionalObject_extRefData);
            exploreReq.AddressList.Add(Ids.FunctionalObject_intRefData);

            res = _requests.SendExplore(exploreReq, out var exploreRes);
            if (res != 0)
            {
                return res;
            }

            foreach (var obj in exploreRes.Objects)
            {
                blockType = GetBlockTypeFromClassId(obj.ClassId);


                foreach (var att in obj.Attributes)
                {
                    onlineMetadata[att.Key] = DescribeValue(att.Value);
                    switch (att.Key)
                    {
                        case Ids.ObjectVariableTypeName:
                            {
                                blockName = ((ValueWString)att.Value).GetValue();
                                break;
                            }
                        case Ids.Block_BlockNumber:
                            {
                                break;
                            }
                        case Ids.Block_BlockLanguage:
                            {
                                var l = ((ValueUInt)att.Value).GetValue();
                                lang = (S7CommPlusProgrammingLanguage)l;
                                break;
                            }
                        case Ids.Block_RuntimeModified:
                            {
                                var timestamp = TryGetUInt64(att.Value);
                                if (timestamp.HasValue)
                                    codeModifiedTimestampBytes = ToBigEndianBytes(timestamp.Value);
                                break;
                            }
                        case Ids.FunctionalObject_extRefData:
                            {
                                var xx = (ValueBlobSparseArray)att.Value;
                                BlobDecompressor bd3 = new BlobDecompressor();
                                var blob_sp3 = xx.GetValue();
                                externalReferences = new string[blob_sp3.Count];
                                var i = 0;
                                foreach (var key in blob_sp3.Keys)
                                {
                                    if (blob_sp3[key].value != null)
                                        externalReferences[i++] = bd3.decompress(blob_sp3[key].value, 4);
                                }
                                break;
                            }
                        case Ids.FunctionalObject_intRefData:
                            {
                                var xx = (ValueBlobSparseArray)att.Value;
                                BlobDecompressor bd3 = new BlobDecompressor();
                                var blob_sp3 = xx.GetValue();
                                internalReferences = new string[blob_sp3.Count];
                                var i = 0;
                                foreach (var key in blob_sp3.Keys)
                                {
                                    if (blob_sp3[key].value != null)
                                        internalReferences[i++] = bd3.decompress(blob_sp3[key].value, 4);
                                }
                                break;
                            }
                        case Ids.ASObjectES_Comment:
                            {
                                var att_comment = (ValueWStringSparseArray)att.Value;
                                xmlComments = att_comment.GetValue();
                                break;
                            }
                        case Ids.DataInterface_LineComments:
                            {
                                var att_linecomment = (ValueBlobSparseArray)att.Value;
                                BlobDecompressor bd = new BlobDecompressor();
                                var blob_sp = att_linecomment.GetValue();
                                // In DBs we get the data with Sparsearray key = 1, in M-Area with key = 2.
                                // For now, just take the first, don't know where the key ids are for.
                                foreach (var key in blob_sp.Keys)
                                {
                                    xmlLineComment = bd.decompress(blob_sp[key].value, 4); // Offset of 4, as we have a header for the zlib dictionary version
                                    break;
                                }
                                break;
                            }
                        case Ids.FunctionalObject_NetworkComments:
                            {
                                networkComments = ReadSparseStrings(att.Value);
                                break;
                            }
                        case Ids.FunctionalObject_NetworkTitles:
                            {
                                networkTitles = ReadSparseStrings(att.Value);
                                break;
                            }
                        case Ids.DataInterface_InterfaceDescription:
                            {
                                var att_ifsescr = (ValueBlob)att.Value;
                                BlobDecompressor bd2 = new BlobDecompressor();
                                var blob_sp2 = att_ifsescr.GetValue();
                                interfaceDescription = bd2.decompress(blob_sp2, 4); // Offset of 4, as we have a header for the zlib dictionary version
                                break;
                            }
                        case Ids.Block_BodyDescription:
                            {
                                var xx = (ValueBlobSparseArray)att.Value;
                                BlobDecompressor bd3 = new BlobDecompressor();
                                var blob_sp3 = xx.GetValue();
                                blockBody = new string[blob_sp3.Where(x => x.Key < (uint)S7CommPlusBinaryArtifactType.PlcFamily).Count()];
                                var i = 0;
                                foreach (var key in blob_sp3.Keys.OrderBy(x => x))
                                {
                                    if (!(key < (uint)S7CommPlusBinaryArtifactType.PlcFamily))
                                    {
                                        // Binary artifacts are metadata, not source text; keep them out of blockBody.
                                        var binaryArtifactType = (S7CommPlusBinaryArtifactType)key;
                                        if (blob_sp3[key].value != null)
                                            binaryArtifacts[key] = blob_sp3[key].value;
                                        continue;
                                    }

                                    if (blob_sp3[key].value != null)
                                    {
                                        var code = bd3.decompress(blob_sp3[key].value, 4);
                                        blockBody[i++] = code;
                                    }
                                }
                                break;
                            }
                        case Ids.FunctionalObject_DebugInfo:
                            {
                                functionalObjectDebugInfo = DecompressFirstBlob(att.Value);
                                break;
                            }
                        case Ids.FunctionalObject_Code:
                            {
                                functionalObjectCodeBytes = GetFirstBlobBytes(att.Value);
                                functionalObjectCode = TryDecompressFirstBlob(att.Value);
                                break;
                            }
                    }
                }
            }

            blockContent = new S7CommPlusClientBlockContent(
                relid,
                blockName,
                lang,
                blockNumber,
                blockType,
                xmlLineComment,
                xmlComments,
                interfaceDescription,
                blockBody,
                functionalObjectCode,
                functionalObjectDebugInfo,
                internalReferences,
                externalReferences,
                functionalObjectCodeBytes,
                codeModifiedTimestampBytes,
                binaryArtifacts,
                onlineMetadata,
                networkComments,
                networkTitles);
            return 0;
        }

        /// <summary>
        /// Retrieves only the line-comment and interface attributes needed to resolve multilingual symbol comments.
        /// </summary>
        /// <param name="relationId">The data-block or absolute I/Q/M area relation ID.</param>
        /// <param name="comments">Receives comments indexed for direct <see cref="VarInfo"/> lookup.</param>
        /// <returns>A native driver error code, or zero on success.</returns>
        public int GetSymbolComments(uint relationId, out S7CommPlusSymbolCommentCatalog comments)
        {
            comments = null;
            var blockName = string.Empty;
            var lineCommentsXml = string.Empty;
            var interfaceDescriptionXml = string.Empty;

            var request = new ExploreRequest(ProtocolVersion.V2)
            {
                ExploreId = relationId,
                ExploreRequestId = Ids.None,
                ExploreChildsRecursive = 1,
                ExploreParents = 0,
            };
            request.AddressList.Add(Ids.ObjectVariableTypeName);
            request.AddressList.Add(Ids.DataInterface_LineComments);
            request.AddressList.Add(Ids.DataInterface_InterfaceDescription);

            var result = _requests.SendExplore(request, out var response);
            if (result != 0)
            {
                return result;
            }

            foreach (var obj in response.Objects)
            {
                foreach (var attribute in obj.Attributes)
                {
                    switch (attribute.Key)
                    {
                        case Ids.ObjectVariableTypeName when attribute.Value is ValueWString name:
                            blockName = name.GetValue();
                            break;
                        case Ids.DataInterface_LineComments when attribute.Value is ValueBlobSparseArray lineComments:
                            lineCommentsXml = DecompressFirstSparseBlob(lineComments);
                            break;
                        case Ids.DataInterface_InterfaceDescription when attribute.Value is ValueBlob interfaceDescription:
                            interfaceDescriptionXml = DecompressBlob(interfaceDescription);
                            break;
                    }
                }
            }

            comments = S7CommPlusSymbolCommentParser.Parse(
                relationId,
                blockName,
                lineCommentsXml,
                interfaceDescriptionXml);
            return 0;
        }

        /// <summary>
        /// Decompresses the first non-empty sparse blob used by Siemens line-comment attributes.
        /// </summary>
        /// <param name="value">The sparse blob array returned by the PLC.</param>
        /// <returns>The decompressed XML, or an empty string when no value is present.</returns>
        private static string DecompressFirstSparseBlob(ValueBlobSparseArray value)
        {
            var blobs = value.GetValue();
            foreach (var key in blobs.Keys.OrderBy(key => key))
            {
                if (blobs[key].value != null)
                {
                    return new BlobDecompressor().decompress(blobs[key].value, 4);
                }
            }
            return string.Empty;
        }

        /// <summary>
        /// Decompresses one Siemens blob whose first four bytes contain the dictionary header.
        /// </summary>
        /// <param name="value">The compressed interface-description value.</param>
        /// <returns>The decompressed XML, or an empty string for an absent payload.</returns>
        private static string DecompressBlob(ValueBlob value)
        {
            var blob = value.GetValue();
            return blob == null || blob.Length == 0
                ? string.Empty
                : new BlobDecompressor().decompress(blob, 4);
        }

        private static S7CommPlusBlockType GetBlockTypeFromClassId(uint classId)
        {
            return classId switch
            {
                Ids.DB_Class_Rid => S7CommPlusBlockType.DB,
                Ids.FB_Class_Rid => S7CommPlusBlockType.FB,
                Ids.FC_Class_Rid => S7CommPlusBlockType.FC,
                Ids.UDT_Class_Rid => S7CommPlusBlockType.UDT,
                Ids.OB_Class_Rid => S7CommPlusBlockType.OB,
                2637 => S7CommPlusBlockType.OB, // ACCcommunicationOB
                2639 => S7CommPlusBlockType.OB, // CPUredundancyErrorOB
                2640 => S7CommPlusBlockType.OB, // CyclicOB
                2641 => S7CommPlusBlockType.OB, // DiagnosticErrorOB
                2642 => S7CommPlusBlockType.OB, // IOaccessErrorOB
                2643 => S7CommPlusBlockType.OB, // IOredundancyErrorOB
                2644 => S7CommPlusBlockType.OB, // PeripheralAccessErrorOB
                2645 => S7CommPlusBlockType.OB, // ProcessEventOB
                2646 => S7CommPlusBlockType.OB, // ProfileEventOB
                2647 => S7CommPlusBlockType.OB, // ProgramCycleOB (OB1)
                2648 => S7CommPlusBlockType.OB, // ProgrammingErrorOB
                2649 => S7CommPlusBlockType.OB, // PullPlugEventOB
                2650 => S7CommPlusBlockType.OB, // RackStationFailureOB
                2651 => S7CommPlusBlockType.OB, // StartupOB
                2652 => S7CommPlusBlockType.OB, // StatusEventOB
                2653 => S7CommPlusBlockType.OB, // SynchronousCycleOB
                2654 => S7CommPlusBlockType.OB, // TechnologyEventOB
                2655 => S7CommPlusBlockType.OB, // TimeDelayOB
                2656 => S7CommPlusBlockType.OB, // TimeErrorOB
                2657 => S7CommPlusBlockType.OB, // TimeOfDayOB
                2658 => S7CommPlusBlockType.OB, // UpdateEventOB
                8440 => S7CommPlusBlockType.OB, // LookAheadOB
                _ => S7CommPlusBlockType.Unknown,
            };
        }

        private static Dictionary<uint, string> ReadSparseStrings(PValue value)
        {
            switch (value)
            {
                case ValueWStringSparseArray strings:
                    {
                        var result = new Dictionary<uint, string>();
                        foreach (var item in strings.GetValue())
                        {
                            result[TryReadTextRefId(item.Value) ?? item.Key] = item.Value;
                        }

                        return result;
                    }
                case ValueBlobSparseArray blobs:
                    {
                        var result = new Dictionary<uint, string>();
                        var decompressor = new BlobDecompressor();
                        foreach (var item in blobs.GetValue())
                        {
                            if (item.Value.value != null)
                            {
                                var text = decompressor.decompress(item.Value.value, 4);
                                result[TryReadTextRefId(text) ?? item.Key] = text;
                            }
                        }

                        return result;
                    }
                default:
                    return new Dictionary<uint, string>();
            }
        }

        private static uint? TryReadTextRefId(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || !text.TrimStart().StartsWith("<"))
                return null;

            try
            {
                var root = XDocument.Parse(text).Root;
                var refId = root?.Attributes()
                    .FirstOrDefault(x => x.Name.LocalName.Equals("RefID", StringComparison.OrdinalIgnoreCase) ||
                                         x.Name.LocalName.Equals("RefId", StringComparison.OrdinalIgnoreCase) ||
                                         x.Name.LocalName.Equals("refId", StringComparison.OrdinalIgnoreCase))
                    ?.Value;

                return uint.TryParse(refId, out var value) ? value : null;
            }
            catch
            {
                return null;
            }
        }

        private static string DescribeValue(PValue value)
        {
            if (value == null)
                return String.Empty;

            var numeric = TryGetUInt64(value);
            if (numeric.HasValue)
                return numeric.Value.ToString();

            return value.ToString();
        }

        private static ulong? TryGetUInt64(PValue value)
        {
            switch (value)
            {
                case ValueTimestamp timestamp:
                    return timestamp.GetValue();
                case ValueULInt ulint:
                    return ulint.GetValue();
                case ValueUDInt udint:
                    return udint.GetValue();
                case ValueUInt uintValue:
                    return uintValue.GetValue();
                case ValueUSInt usint:
                    return usint.GetValue();
                case ValueLWord lword:
                    return lword.GetValue();
                case ValueDWord dword:
                    return dword.GetValue();
                case ValueWord word:
                    return word.GetValue();
                case ValueByte byteValue:
                    return byteValue.GetValue();
                default:
                    return null;
            }
        }

        private static byte[] ToBigEndianBytes(ulong value)
        {
            return new[]
            {
                (byte)(value >> 56),
                (byte)(value >> 48),
                (byte)(value >> 40),
                (byte)(value >> 32),
                (byte)(value >> 24),
                (byte)(value >> 16),
                (byte)(value >> 8),
                (byte)value
            };
        }

        private static byte[] GetFirstBlobBytes(PValue value)
        {
            if (value is ValueBlob blob)
            {
                return blob.GetValue() ?? Array.Empty<byte>();
            }

            if (value is ValueBlobSparseArray sparseArray)
            {
                var sparse = sparseArray.GetValue();
                foreach (var key in sparse.Keys.OrderBy(x => x))
                {
                    var data = sparse[key].value;
                    if (data != null && data.Length > 0)
                        return data;
                }
            }

            return Array.Empty<byte>();
        }

        private static string TryDecompressFirstBlob(PValue value)
        {
            try
            {
                return DecompressFirstBlob(value);
            }
            catch (InvalidDataException)
            {
                return String.Empty;
            }
        }

        private static string DecompressFirstBlob(PValue value)
        {
            var decompressor = new BlobDecompressor();
            if (value is ValueBlob blob)
            {
                var data = blob.GetValue();
                return data == null || data.Length == 0 ? String.Empty : decompressor.decompress(data, 4);
            }

            if (value is ValueBlobSparseArray sparseArray)
            {
                var sparse = sparseArray.GetValue();
                foreach (var key in sparse.Keys.OrderBy(x => x))
                {
                    var data = sparse[key].value;
                    if (data != null && data.Length > 0)
                        return decompressor.decompress(data, 4);
                }
            }

            return String.Empty;
        }

        public int RunExploreRequest(uint relid, uint[] attributes, out List<PObject> objects, byte exploreChildsRecursive = 1, byte exploreParents = 0)
        {
            objects = null;
            var res = _requests.Explore(relid, attributes, out var exploreRes, exploreChildsRecursive, exploreParents);
            if (res != 0)
            {
                return res;
            }

            objects = exploreRes.Objects;

            return 0;
        }

        public int RunGetVarSubstreamedRequest(uint objectId, ushort address, out PValue value)
        {
            return _requests.GetVarSubstreamed(objectId, address, out value);
        }

        public int GetCpuState(out S7CommPlusCpuState cpuState)
        {
            cpuState = null;

            var res = RunGetVarSubstreamedRequest(Ids.NativeObjects_theCPUexecUnit_Rid, Ids.HWObject_DIS, out var value);
            if (res == 0 &&
                TryGetStructElement(value, Ids.AS_DIS_OperatingState, out var operatingStateValue) &&
                TryGetInt32(operatingStateValue, out var operatingState))
            {
                var displayStateSwitch = ReadOptionalCpuStateSwitch();
                cpuState = new S7CommPlusCpuState(operatingState, MapCpuOperatingState(operatingState), displayStateSwitch, MapCpuStateSwitch(displayStateSwitch));
                return 0;
            }

            res = RunGetVarSubstreamedRequest(Ids.NativeObjects_theCPUCommon_Rid, Ids.CPUCommon_OperatingState, out value);
            if (res != 0)
            {
                return res;
            }

            if (!TryGetInt32(value, out operatingState))
            {
                return S7Consts.errIsoInvalidPDU;
            }

            var stateSwitch = ReadOptionalCpuStateSwitch();
            cpuState = new S7CommPlusCpuState(operatingState, MapCpuOperatingState(operatingState), stateSwitch, MapCpuStateSwitch(stateSwitch));
            return 0;
        }

        private int? ReadOptionalCpuStateSwitch()
        {
            var res = RunGetVarSubstreamedRequest(Ids.NativeObjects_theCPUCommon_Rid, Ids.CPUCommon_StateSwitch2, out var value);
            if (res != 0)
            {
                return null;
            }

            return TryGetInt32(value, out var stateSwitch) ? stateSwitch : (int?)null;
        }

        public int GetCpuCycleTime(out S7CommPlusCpuCycleTime cycleTime)
        {
            cycleTime = null;

            var configuredMinimum = ReadOptionalCycleTime(Ids.CPUexecUnit_ConfiguredMinScanCycle);
            var configuredMaximum = ReadOptionalCycleTime(Ids.CPUexecUnit_ConfiguredMaxScanCycle);

            var res = ReadRequiredCycleTime(Ids.CPUexecUnit_ShortestScanCycle, out var shortest);
            if (res != 0)
            {
                return res;
            }

            res = ReadRequiredCycleTime(Ids.CPUexecUnit_CurrentScanCycle, out var current);
            if (res != 0)
            {
                return res;
            }

            res = ReadRequiredCycleTime(Ids.CPUexecUnit_LongestScanCycle, out var longest);
            if (res != 0)
            {
                return res;
            }

            cycleTime = new S7CommPlusCpuCycleTime(configuredMinimum, configuredMaximum, shortest, current, longest);
            return 0;
        }

        public int GetCpuMemoryUsage(out S7CommPlusCpuMemoryUsage memoryUsage)
        {
            memoryUsage = null;
            var firstError = 0;
            var areas = new List<S7CommPlusCpuMemoryArea>();

            ReadMemoryArea(
                areas,
                ref firstError,
                "load",
                "Load memory",
                Ids.CPUCommon_LoadMemoryTotal,
                Ids.CPUCommon_LoadMemoryUsed);
            ReadMemoryArea(
                areas,
                ref firstError,
                "work",
                "Work memory",
                Ids.CPUCommon_WorkMemoryTotal,
                Ids.CPUCommon_WorkMemoryUsed);
            ReadMemoryArea(
                areas,
                ref firstError,
                "work-code",
                "Work memory code",
                Ids.CPUCommon_WorkMemoryCodeTotal,
                Ids.CPUCommon_WorkMemoryCodeUsed);
            ReadMemoryArea(
                areas,
                ref firstError,
                "work-data",
                "Work memory data",
                Ids.CPUCommon_WorkMemoryDataTotal,
                Ids.CPUCommon_WorkMemoryDataUsed);
            ReadMemoryArea(
                areas,
                ref firstError,
                "retain",
                "Retain memory",
                Ids.CPUCommon_RetainMemoryTotal,
                Ids.CPUCommon_RetainMemoryUsed);

            if (areas.Count == 0)
            {
                return firstError == 0 ? S7Consts.errIsoInvalidPDU : firstError;
            }

            memoryUsage = new S7CommPlusCpuMemoryUsage(areas);
            return 0;
        }

        private void ReadMemoryArea(
            ICollection<S7CommPlusCpuMemoryArea> areas,
            ref int firstError,
            string key,
            string name,
            int totalAddress,
            int usedAddress)
        {
            var totalResult = ReadOptionalMemorySize(totalAddress, out var totalBytes);
            var usedResult = ReadOptionalMemorySize(usedAddress, out var usedBytes);
            if (totalResult != 0 || usedResult != 0 || totalBytes <= 0)
            {
                if (firstError == 0)
                {
                    firstError = totalResult != 0 ? totalResult : usedResult;
                }
                return;
            }

            areas.Add(new S7CommPlusCpuMemoryArea(key, name, totalBytes, usedBytes));
        }

        private int ReadOptionalMemorySize(int address, out long bytes)
        {
            bytes = 0;
            var res = RunGetVarSubstreamedRequest(Ids.NativeObjects_theCPUCommon_Rid, (ushort)address, out var value);
            if (res != 0)
            {
                return res;
            }

            if (!TryGetInt64(value, out bytes))
            {
                return S7Consts.errIsoInvalidPDU;
            }

            return 0;
        }

        private int ReadRequiredCycleTime(int address, out double milliseconds)
        {
            milliseconds = 0;
            var res = RunGetVarSubstreamedRequest(Ids.NativeObjects_theCPUexecUnit_Rid, (ushort)address, out var value);
            if (res != 0)
            {
                return res;
            }

            return TryGetMilliseconds(value, out milliseconds)
                ? 0
                : S7Consts.errIsoInvalidPDU;
        }

        private double? ReadOptionalCycleTime(int address)
        {
            var res = RunGetVarSubstreamedRequest(Ids.NativeObjects_theCPUexecUnit_Rid, (ushort)address, out var value);
            if (res != 0)
            {
                return null;
            }

            return TryGetMilliseconds(value, out var milliseconds) ? milliseconds : (double?)null;
        }

        private static bool TryGetStructElement(PValue value, int elementId, out PValue element)
        {
            element = null;
            if (value is not ValueStruct valueStruct)
            {
                return false;
            }

            try
            {
                element = valueStruct.GetStructElement((uint)elementId);
                return true;
            }
            catch (KeyNotFoundException)
            {
                return false;
            }
        }

        private static bool TryGetMilliseconds(PValue value, out double milliseconds)
        {
            milliseconds = 0;
            if (!TryGetDouble(value, out var rawValue))
            {
                return false;
            }

            milliseconds = IsFloatingPoint(value) ? rawValue : rawValue / 1000.0;
            return true;
        }

        private static bool TryGetInt32(PValue value, out int result)
        {
            result = 0;
            if (!TryGetDouble(value, out var doubleValue))
            {
                return false;
            }

            if (doubleValue < Int32.MinValue || doubleValue > Int32.MaxValue)
            {
                return false;
            }

            result = Convert.ToInt32(doubleValue);
            return true;
        }

        private static bool TryGetInt64(PValue value, out long result)
        {
            result = 0;
            if (!TryGetDouble(value, out var doubleValue))
            {
                return false;
            }

            if (doubleValue < Int64.MinValue || doubleValue > Int64.MaxValue)
            {
                return false;
            }

            result = Convert.ToInt64(doubleValue);
            return true;
        }

        private static bool TryGetDouble(PValue value, out double result)
        {
            switch (value)
            {
                case ValueUSInt v:
                    result = v.GetValue();
                    return true;
                case ValueUInt v:
                    result = v.GetValue();
                    return true;
                case ValueUDInt v:
                    result = v.GetValue();
                    return true;
                case ValueULInt v:
                    result = v.GetValue();
                    return true;
                case ValueSInt v:
                    result = v.GetValue();
                    return true;
                case ValueInt v:
                    result = v.GetValue();
                    return true;
                case ValueDInt v:
                    result = v.GetValue();
                    return true;
                case ValueLInt v:
                    result = v.GetValue();
                    return true;
                case ValueByte v:
                    result = v.GetValue();
                    return true;
                case ValueWord v:
                    result = v.GetValue();
                    return true;
                case ValueDWord v:
                    result = v.GetValue();
                    return true;
                case ValueLWord v:
                    result = v.GetValue();
                    return true;
                case ValueReal v:
                    result = v.GetValue();
                    return true;
                case ValueLReal v:
                    result = v.GetValue();
                    return true;
                case ValueTimespan v:
                    result = v.GetValue() / 1_000_000.0;
                    return true;
                default:
                    result = 0;
                    return false;
            }
        }

        private static bool IsFloatingPoint(PValue value)
        {
            return value is ValueReal or ValueLReal or ValueTimespan;
        }

        private static S7CommPlusCpuOperatingState MapCpuOperatingState(int value)
        {
            return value switch
            {
                0 => S7CommPlusCpuOperatingState.NotSupported,
                1 or 3 or 4 or 17 or 33 => S7CommPlusCpuOperatingState.Stop,
                5 or 8 or 9 or 18 or 20 or 37 or 40 => S7CommPlusCpuOperatingState.Run,
                6 or 35 or 38 => S7CommPlusCpuOperatingState.Startup,
                10 or 34 => S7CommPlusCpuOperatingState.Halt,
                13 => S7CommPlusCpuOperatingState.Defective,
                _ => S7CommPlusCpuOperatingState.Unknown
            };
        }

        private static string MapCpuStateSwitch(int? value)
        {
            return value switch
            {
                1 => "Stop",
                2 => "Run",
                3 => "MRes",
                _ => null
            };
        }


        public int GetCpuCultureInfo(out S7CommPlusCpuCultureInfo cultureInfo)
        {
            int res;
            cultureInfo = null;

            var exploreReq = new ExploreRequest(ProtocolVersion.V2);
            exploreReq.ExploreId = Ids.NativeObjects_theTextContainer_Rid;
            exploreReq.ExploreRequestId = Ids.None;
            exploreReq.ExploreChildsRecursive = 0;
            exploreReq.ExploreParents = 1;
            exploreReq.AddressList.Add(Ids.TextContainer_LCIDs_Aid);

            res = _requests.SendExplore(exploreReq, out var exploreRes);
            if (res != 0)
            {
                return res;
            }

            var textContainer = FindObject(
                exploreRes.Objects,
                obj => obj.RelationId == Ids.NativeObjects_theTextContainer_Rid || obj.ClassId == Ids.TextContainer_Class_Rid);
            if (textContainer == null ||
                !textContainer.Attributes.TryGetValue(Ids.TextContainer_LCIDs_Aid, out var lcidValue))
            {
                return S7Consts.errIsoInvalidPDU;
            }

            if (lcidValue is ValueUIntArray lcidArray)
            {
                cultureInfo = new S7CommPlusCpuCultureInfo(lcidArray.GetValue().Select(languageId => (int)languageId));
                return 0;
            }

            if (lcidValue is ValueUInt lcid)
            {
                cultureInfo = new S7CommPlusCpuCultureInfo(new[] { (int)lcid.GetValue() });
                return 0;
            }

            return S7Consts.errIsoInvalidPDU;
        }

        private static PObject FindObject(IEnumerable<PObject> objects, Func<PObject, bool> predicate)
        {
            if (objects == null)
            {
                return null;
            }

            foreach (var obj in objects)
            {
                if (predicate(obj))
                {
                    return obj;
                }

                var child = FindObject(obj.GetObjects(), predicate);
                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        public int GetCpuInfo(out S7CommPlusCpuInfo cpuInfo)
        {
            cpuInfo = null;

            var res = RunGetVarSubstreamedRequest(Ids.NativeObjects_theASRoot_Rid, 2459, out var pValueVersions);
            if (res != 0)
                return res;
            var arrVersions = ((ValueUSIntArray)pValueVersions).GetValue();
            var version1 = new Version(arrVersions[0], arrVersions[1], arrVersions[2], arrVersions[3]);
            var version2 = new Version(arrVersions[4], arrVersions[5], arrVersions[6], arrVersions[7]);

            res = RunGetVarSubstreamedRequest(Ids.NativeObjects_theCPUCommon_Rid, 233, out var pValuePlcName);
            if (res != 0)
                return res;
            var cpuName = ((ValueWString)pValuePlcName).GetValue();

            res = RunGetVarSubstreamedRequest(Ids.NativeObjects_theCPUProxy_Rid, 3753, out var pValueMlfbSerial);
            if (res != 0)
                return res;
            var data = ((ValueBlob)pValueMlfbSerial).GetValue();
            var mlfb = Encoding.ASCII.GetString(data, 8, 19);
            //space
            var serial = Encoding.ASCII.GetString(data, 28, 16);
            //nulbyte
            var hardware = data[46];
            var firmware = new Version(data[47], data[48], data[49], data[50]);
            //data maybe conatins more:
            //rack (1byte), slot (1byte),

            res = RunGetVarSubstreamedRequest(Ids.ReleaseMngmtRoot_Rid, 8342, out var pValueVersionsAndName);
            if (res != 0)
                return res;
            var versionsAndName = ((ValueWStringArray)pValueVersionsAndName).GetValue();

            cpuInfo = new S7CommPlusCpuInfo()
            {
                PlcName = cpuName,
                ProjectName = versionsAndName[2],
                VersionTia = version1,
                Version2 = version2,
                CpuMlfb = mlfb,
                CpuSerial = serial,
                CpuFirmware = firmware
            };

            return 0;
        }
    }
}
