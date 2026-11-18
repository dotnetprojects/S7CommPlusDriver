using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using S7CommPlusDriver.ClientApi;

namespace S7CommPlusDriver.Internal
{
    /// <summary>
    /// Converts the TIA block interface XML of one data block into one level of <see cref="S7CommPlusBrowseNode"/> children.
    /// </summary>
    /// <remarks>
    /// The interface is never materialized as a complete symbol tree. Each call walks the requested symbol path through
    /// the XML parts and returns only the direct children of the addressed node, so memory usage is bounded by one data
    /// block interface.
    /// </remarks>
    internal static class S7CommPlusBlockInterfaceReader
    {
        private static readonly char[] DotOrBracket = new[] { '.', '[' };

        private static readonly Regex ArrayTypeRegex = new Regex(
            @"^Array\[(?<dims>.*?)\]\s+of\s+(?<element>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex ArrayRangeRegex = new Regex(
            @"^(?<lo>-?\d+)\s*\.\.\s*(?<hi>-?\d+)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex StringTypeRegex = new Regex(
            @"^(?:W)?String\s*\[\s*(?:(?<single>-?\d+)|(?<lo>-?\d+)\s*\.\.\s*(?<hi>-?\d+))\s*\]$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Dictionary<string, uint> NativeTypeMap = BuildNativeTypeMap();

        /// <summary>
        /// Browses one level below the addressed member path inside one data block interface.
        /// </summary>
        /// <param name="root">The parsed data-block interface XML.</param>
        /// <param name="dbName">The data block name used as first display path segment.</param>
        /// <param name="dbRelationId">The data block relation ID used as access-sequence root.</param>
        /// <param name="segments">The normalized symbol path; segment zero is the data block name.</param>
        /// <param name="arrayDimensionCache">Resolved array dimensions per symbol path, or <see langword="null"/>.</param>
        /// <param name="resolveTag">Resolves one symbol through the PLC to obtain symbolic array bounds, or <see langword="null"/>.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <param name="fallback">
        /// Resolves the children of a node whose type is not embedded in the interface XML, or <see langword="null"/>.
        /// </param>
        /// <returns>The direct children of the addressed node.</returns>
        internal static async Task<List<S7CommPlusBrowseNode>> BrowseInDbAsync(
            XElement root,
            string dbName,
            uint dbRelationId,
            IReadOnlyList<string> segments,
            Dictionary<string, List<S7CommPlusArrayDimension>> arrayDimensionCache,
            Func<string, CancellationToken, Task<PlcTag>> resolveTag,
            CancellationToken cancellationToken,
            Func<CancellationToken, Task<IReadOnlyList<S7CommPlusBrowseNode>>> fallback = null)
        {
            var primaryPart = GetPrimaryPart(root);
            if (primaryPart == null)
            {
                return new List<S7CommPlusBrowseNode>();
            }

            var context = new PartContext(primaryPart, GetSubParts(primaryPart), isStructure: false);
            var displayPath = S7CommPlusSymbolPath.QuoteIfContainsDot(dbName);
            var accessSequence = dbRelationId.ToString("X", CultureInfo.InvariantCulture);
            var insideArray = false;

            for (var i = 1; i < segments.Count; i++)
            {
                var rawSegment = S7CommPlusSymbolPath.Unquote(segments[i]);
                var bracketIndex = rawSegment.IndexOf('[');
                var memberName = bracketIndex >= 0 ? rawSegment.Substring(0, bracketIndex) : rawSegment;
                var hasIndex = bracketIndex >= 0;

                var member = FindMember(context, memberName);
                if (member == null)
                {
                    throw new InvalidOperationException(
                        $"Member '{memberName}' not found in '{string.Join(".", segments.Take(i))}'.");
                }

                var memberInfo = GetMemberTypeInfo(context, member);
                if (memberInfo.Array != null && memberInfo.Array.HasWildcardDimensions)
                {
                    throw new InvalidOperationException(
                        $"'{rawSegment}' has a variable-length array declaration and cannot be browsed.");
                }

                var memberDisplayPath = displayPath + "." + S7CommPlusSymbolPath.QuoteIfContainsDot(memberName);

                if (hasIndex)
                {
                    if (!memberInfo.IsArray)
                    {
                        throw new InvalidOperationException($"'{rawSegment}' is not an array.");
                    }

                    if (!memberInfo.IsComplex || memberInfo.TargetContext == null)
                    {
                        var fallbackResult = await TryFallbackAsync(fallback, cancellationToken)
                            .ConfigureAwait(false);
                        if (fallbackResult != null)
                        {
                            return fallbackResult;
                        }

                        if (memberInfo.IsComplex)
                        {
                            return new List<S7CommPlusBrowseNode>();
                        }

                        throw new InvalidOperationException($"'{rawSegment}' is not a struct array.");
                    }

                    var elementDisplayPath = memberDisplayPath + rawSegment.Substring(bracketIndex);
                    if (i == segments.Count - 1)
                    {
                        return await BuildStructMemberNodesAsync(
                                memberInfo.TargetContext,
                                elementDisplayPath,
                                accessBase: null,
                                arrayDimensionCache,
                                resolveTag,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }

                    insideArray = true;
                    displayPath = elementDisplayPath;
                    context = memberInfo.TargetContext;
                    accessSequence = null;
                    continue;
                }

                if (i == segments.Count - 1)
                {
                    if (memberInfo.IsArray)
                    {
                        return await BuildArrayElementNodesAsync(
                                memberName,
                                memberDisplayPath,
                                memberInfo,
                                arrayDimensionCache,
                                resolveTag,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }

                    if (memberInfo.TargetContext == null)
                    {
                        var fallbackResult = await TryFallbackAsync(fallback, cancellationToken)
                            .ConfigureAwait(false);
                        if (fallbackResult != null)
                        {
                            return fallbackResult;
                        }

                        if (memberInfo.IsComplex)
                        {
                            return new List<S7CommPlusBrowseNode>();
                        }

                        throw new InvalidOperationException($"'{rawSegment}' is not a struct/DB and has no children.");
                    }

                    var childAccessBase = insideArray
                        ? null
                        : accessSequence + "." + memberInfo.Lid.ToString("X", CultureInfo.InvariantCulture);
                    return await BuildStructMemberNodesAsync(
                            memberInfo.TargetContext,
                            memberDisplayPath,
                            childAccessBase,
                            arrayDimensionCache,
                            resolveTag,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                if (memberInfo.TargetContext == null)
                {
                    var fallbackResult = await TryFallbackAsync(fallback, cancellationToken)
                        .ConfigureAwait(false);
                    if (fallbackResult != null)
                    {
                        return fallbackResult;
                    }

                    if (memberInfo.IsComplex)
                    {
                        return new List<S7CommPlusBrowseNode>();
                    }

                    throw new InvalidOperationException($"'{rawSegment}' is not a struct/DB and has no children.");
                }

                if (!insideArray)
                {
                    accessSequence += "." + memberInfo.Lid.ToString("X", CultureInfo.InvariantCulture);
                }

                displayPath = memberDisplayPath;
                context = memberInfo.TargetContext;
            }

            return await BuildStructMemberNodesAsync(
                    context,
                    displayPath,
                    insideArray ? null : accessSequence,
                    arrayDimensionCache,
                    resolveTag,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        private static async Task<List<S7CommPlusBrowseNode>> TryFallbackAsync(
            Func<CancellationToken, Task<IReadOnlyList<S7CommPlusBrowseNode>>> fallback,
            CancellationToken cancellationToken)
        {
            if (fallback == null)
            {
                return null;
            }

            var result = await fallback(cancellationToken).ConfigureAwait(false);
            return result == null ? null : new List<S7CommPlusBrowseNode>(result);
        }

        /// <summary>
        /// Projects the direct children of <paramref name="symbolPath"/> from the flat driver symbol snapshot.
        /// </summary>
        /// <remarks>
        /// The DB interface XML does not embed the members of system types such as IEC timers or R_TRIG. The driver symbol
        /// catalog expands them through the PLC type information, so the snapshot contains their leaves (for example
        /// <c>db.trig.CLK</c>).
        /// </remarks>
        internal static List<S7CommPlusBrowseNode> BuildVarInfoChildNodes(
            string symbolPath, IReadOnlyList<VarInfo> browseResult)
        {
            var nodes = new List<S7CommPlusBrowseNode>();
            if (browseResult == null || browseResult.Count == 0 || string.IsNullOrWhiteSpace(symbolPath))
            {
                return nodes;
            }

            var prefix = S7CommPlusSymbolPath.Normalize(symbolPath) + ".";
            var groups = new Dictionary<string, List<VarInfo>>(StringComparer.Ordinal);
            var order = new List<string>();

            foreach (var info in browseResult)
            {
                var name = info.Name;
                if (string.IsNullOrEmpty(name) || !name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var rest = name.Substring(prefix.Length);
                var separatorIndex = rest.IndexOfAny(DotOrBracket);
                var childKeyLength = separatorIndex < 0 ? rest.Length : separatorIndex;
                if (childKeyLength <= 0)
                {
                    continue;
                }

                var childKey = rest.Substring(0, childKeyLength);
                if (childKey[0] == '[')
                {
                    continue;
                }

                List<VarInfo> group;
                if (!groups.TryGetValue(childKey, out group))
                {
                    group = new List<VarInfo>();
                    groups.Add(childKey, group);
                    order.Add(childKey);
                }

                group.Add(info);
            }

            foreach (var childKey in order)
            {
                var group = groups[childKey];
                var exactName = prefix + childKey;
                VarInfo exact = null;
                var hasNestedEntries = false;
                foreach (var info in group)
                {
                    if (string.Equals(info.Name, exactName, StringComparison.Ordinal))
                    {
                        exact = info;
                    }
                    else
                    {
                        hasNestedEntries = true;
                    }
                }

                var isArray = childKey.IndexOf('[') >= 0
                    || (exact != null
                        && (exact.ArrayDimensions.Count > 0 || exact.ArrayElementCount > 0));

                string nativeTypeName;
                bool isStructure;
                uint softdatatype;
                if (exact != null)
                {
                    softdatatype = exact.Softdatatype;
                    isStructure = IsStructLikeSoftdatatype(exact.Softdatatype);
                    string native;
                    nativeTypeName = Softdatatype.Types.TryGetValue(exact.Softdatatype, out native)
                        ? native
                        : exact.Softdatatype.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    // A parent that only exists through its leaves (struct or struct-array element).
                    softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT;
                    isStructure = true;
                    nativeTypeName = Softdatatype.Types[Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT];
                }

                var node = new S7CommPlusBrowseNode
                {
                    Name = childKey,
                    SymbolPath = symbolPath.TrimEnd('.') + "." + S7CommPlusSymbolPath.QuoteIfContainsDot(childKey),
                    Softdatatype = softdatatype,
                    IsStructure = isStructure,
                    NativeTypeName = nativeTypeName,
                    HasChildren = hasNestedEntries
                        || (exact != null
                            && (exact.ArrayDimensions.Count > 0 || exact.ArrayElementCount > 0))
                        || (exact == null && !isArray),
                    IsArray = isArray
                };

                if (exact != null)
                {
                    node.AccessSequence = exact.AccessSequence;

                    if (exact.MaxStringLength > 0)
                    {
                        node.StringLength = exact.MaxStringLength;
                    }

                    if (isArray)
                    {
                        if (exact.ArrayDimensions.Count == 1)
                        {
                            var elementCount = exact.ArrayDimensions[0].ElementCount;
                            if (elementCount > 0 && elementCount <= int.MaxValue)
                            {
                                node.ArrayDimension = (int)elementCount;
                            }
                        }
                        else if (exact.ArrayDimensions.Count == 0
                                 && exact.ArrayElementCount > 0
                                 && exact.ArrayElementCount <= int.MaxValue)
                        {
                            node.ArrayDimension = (int)exact.ArrayElementCount;
                        }
                    }
                }

                nodes.Add(node);
            }

            return nodes;
        }

        private static async Task<List<S7CommPlusBrowseNode>> BuildArrayElementNodesAsync(
            string memberName,
            string displayPath,
            MemberTypeInfo info,
            Dictionary<string, List<S7CommPlusArrayDimension>> arrayDimensionCache,
            Func<string, CancellationToken, Task<PlcTag>> resolveTag,
            CancellationToken cancellationToken)
        {
            await ResolveArrayDimensionsAsync(
                    info, displayPath, arrayDimensionCache, resolveTag, cancellationToken)
                .ConfigureAwait(false);

            if (info.Array == null || info.Array.Dimensions.Count == 0)
            {
                return new List<S7CommPlusBrowseNode>();
            }

            var nodes = new List<S7CommPlusBrowseNode>();
            foreach (var indices in EnumerateArrayIndices(info.Array.Dimensions))
            {
                var suffix = "[" + string.Join(",", indices) + "]";
                var node = new S7CommPlusBrowseNode
                {
                    Name = memberName + suffix,
                    SymbolPath = displayPath + suffix,
                    Softdatatype = info.Softdatatype,
                    IsStructure = IsStructLikeSoftdatatype(info.Softdatatype),
                    NativeTypeName = info.ElementTypeName,
                    HasChildren = info.ElementHasChildren
                };

                if (IsStringDatatype(info.Softdatatype) && info.StringLength > 0)
                {
                    node.StringLength = info.StringLength;
                }

                nodes.Add(node);
            }

            return nodes;
        }

        private static async Task<List<S7CommPlusBrowseNode>> BuildStructMemberNodesAsync(
            PartContext context,
            string displayPrefix,
            string accessBase,
            Dictionary<string, List<S7CommPlusArrayDimension>> arrayDimensionCache,
            Func<string, CancellationToken, Task<PlcTag>> resolveTag,
            CancellationToken cancellationToken)
        {
            var nodes = new List<S7CommPlusBrowseNode>();
            foreach (var member in DirectMembers(context))
            {
                var name = Attr(member, "Name");
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var info = GetMemberTypeInfo(context, member);
                if (info.Array != null && info.Array.HasWildcardDimensions)
                {
                    continue;
                }

                var memberSymbolPath = displayPrefix + "." + S7CommPlusSymbolPath.QuoteIfContainsDot(name);

                if (info.IsArray)
                {
                    await ResolveArrayDimensionsAsync(
                            info, memberSymbolPath, arrayDimensionCache, resolveTag, cancellationToken)
                        .ConfigureAwait(false);
                }

                var hasChildren = info.IsArray
                    || info.TargetContext != null
                    // System types such as R_TRIG or TON_TIME have no SubPart in the DB source XML.
                    // Their members are resolved through the VarInfo fallback. DTL is complex too, but
                    // the driver treats it as an opaque scalar.
                    || (info.IsComplex && !IsOpaqueSystemTypeName(info.NativeName));

                var node = new S7CommPlusBrowseNode
                {
                    Name = name,
                    SymbolPath = memberSymbolPath,
                    Softdatatype = info.Softdatatype,
                    IsStructure = IsStructLikeSoftdatatype(info.Softdatatype),
                    NativeTypeName = info.NativeName,
                    HasChildren = hasChildren,
                    IsArray = info.IsArray
                };

                if (info.IsArray)
                {
                    // S7CommPlusBrowseNode.ArrayDimension is the element count for one-dimensional arrays.
                    // Multi-dimensional arrays cannot be represented by this single value.
                    node.ArrayDimension = info.Array != null && info.Array.Dimensions.Count == 1
                        ? (int?)info.Array.Dimensions[0].ElementCount
                        : null;
                }

                if (IsStringDatatype(info.Softdatatype) && info.StringLength > 0)
                {
                    node.StringLength = info.StringLength;
                }

                if (!hasChildren && accessBase != null)
                {
                    node.AccessSequence = accessBase + "." + info.Lid.ToString("X", CultureInfo.InvariantCulture);
                }

                nodes.Add(node);
            }

            return nodes;
        }

        private static async Task ResolveArrayDimensionsAsync(
            MemberTypeInfo info,
            string symbolPath,
            Dictionary<string, List<S7CommPlusArrayDimension>> arrayDimensionCache,
            Func<string, CancellationToken, Task<PlcTag>> resolveTag,
            CancellationToken cancellationToken)
        {
            if (!info.IsArray)
            {
                return;
            }

            if (info.Array == null)
            {
                info.Array = new ArrayInfo { ElementTypeText = info.ElementTypeName };
            }

            List<S7CommPlusArrayDimension> cachedDimensions;
            if (arrayDimensionCache != null
                && arrayDimensionCache.TryGetValue(symbolPath, out cachedDimensions))
            {
                info.Array.Dimensions.Clear();
                info.Array.Dimensions.AddRange(cachedDimensions);
                return;
            }

            if (info.Array.Dimensions.Count == 0 && resolveTag != null)
            {
                // Symbolic array bounds (Array[1.."c_i_maxNumberOfModule"] of "...") can only be
                // resolved by the PLC. The driver resolves this single symbol lazily through
                // GetTagBySymbolAsync; it does not build the complete PLC symbol catalog.
                var tag = await resolveTag(symbolPath, cancellationToken).ConfigureAwait(false);
                if (tag != null && tag.ArrayDimensions.Count > 0)
                {
                    info.Array.Dimensions.Clear();
                    foreach (var dimension in tag.ArrayDimensions)
                    {
                        if (dimension.ElementCount == 0 || dimension.ElementCount > int.MaxValue)
                        {
                            continue;
                        }

                        info.Array.Dimensions.Add(
                            new S7CommPlusArrayDimension(dimension.LowerBound, dimension.ElementCount));
                    }
                }
            }

            if (arrayDimensionCache != null && info.Array.Dimensions.Count > 0)
            {
                arrayDimensionCache[symbolPath] = info.Array.Dimensions.ToList();
            }
        }

        private static XElement GetPrimaryPart(XElement root)
        {
            var first = Children(root, "Part").FirstOrDefault();
            if (first == null)
            {
                return null;
            }

            // An instance DB or UDT-typed DB has no source part of its own. Its members come from
            // the source type embedded in the Values part's SubParts.
            if (string.Equals(Attr(first, "Kind"), "Values", StringComparison.OrdinalIgnoreCase))
            {
                var subParts = Child(first, "SubParts");
                return subParts == null ? null : Children(subParts, "Part").FirstOrDefault();
            }

            return first;
        }

        private static List<XElement> GetSubParts(XElement part)
        {
            var subParts = Child(part, "SubParts");
            return subParts == null ? new List<XElement>() : Children(subParts, "Part").ToList();
        }

        private static PartContext GetTargetContext(PartContext parent, XElement member)
        {
            var indexText = Attr(member, "SubPartIndex");
            int index;
            if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out index)
                || index < 0
                || index >= parent.SubParts.Count)
            {
                return null;
            }

            var target = parent.SubParts[index];
            var kind = Attr(target, "Kind") ?? string.Empty;

            if (kind.EndsWith("Source", StringComparison.OrdinalIgnoreCase))
            {
                return new PartContext(target, GetSubParts(target), isStructure: false);
            }

            if (string.Equals(kind, "Structure", StringComparison.OrdinalIgnoreCase))
            {
                // Inline struct/UDT parts share the SubParts list of their enclosing source part.
                return new PartContext(target, parent.SubParts, isStructure: true);
            }

            return null;
        }

        private static XElement FindMember(PartContext context, string name)
        {
            foreach (var member in DirectMembers(context))
            {
                if (string.Equals(Attr(member, "Name"), name, StringComparison.Ordinal))
                {
                    return member;
                }
            }

            return null;
        }

        private static IEnumerable<XElement> DirectMembers(PartContext context)
        {
            var payload = Child(context.Part, "Payload");
            if (payload != null)
            {
                if (context.IsStructure)
                {
                    foreach (var member in MemberContainers(payload))
                    {
                        yield return member;
                    }

                    foreach (var root in Children(payload, "Root"))
                    {
                        foreach (var member in Children(root, "Member"))
                        {
                            if (Attr(member, "RID") != null)
                            {
                                yield return member;
                            }
                        }
                    }
                }
                else
                {
                    // Flat DB/UDT: variables are direct Member children of Payload/Root.
                    foreach (var root in Children(payload, "Root"))
                    {
                        foreach (var member in Children(root, "Member"))
                        {
                            if (Attr(member, "RID") != null)
                            {
                                yield return member;
                            }
                        }
                    }

                    foreach (var member in Children(payload, "Member"))
                    {
                        if (Attr(member, "RID") != null)
                        {
                            yield return member;
                        }
                    }
                }
            }

            if (context.IsStructure)
            {
                yield break;
            }

            // FB/OB: variables live in the *Section parts of SubParts.
            foreach (var section in context.SubParts)
            {
                var kind = Attr(section, "Kind") ?? string.Empty;
                if (!kind.EndsWith("Section", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var sectionPayload = Child(section, "Payload");
                if (sectionPayload == null)
                {
                    continue;
                }

                foreach (var member in MemberContainers(sectionPayload))
                {
                    yield return member;
                }
            }
        }

        private static IEnumerable<XElement> MemberContainers(XElement payload)
        {
            foreach (var container in Children(payload, "Member"))
            {
                var nested = Children(container, "Member").ToList();
                if (nested.Count > 0)
                {
                    foreach (var member in nested)
                    {
                        if (Attr(member, "RID") != null)
                        {
                            yield return member;
                        }
                    }
                }
                else if (Attr(container, "RID") != null)
                {
                    yield return container;
                }
            }
        }

        private static MemberTypeInfo GetMemberTypeInfo(PartContext context, XElement member)
        {
            var typeText = Attr(member, "Type");
            if (typeText != null)
            {
                typeText = typeText.Trim();
            }

            var rid = ParseRid(Attr(member, "RID"));
            var isNamedValueType = string.Equals(Attr(member, "NVT"), "1", StringComparison.Ordinal);
            var info = new MemberTypeInfo { Lid = ParseLid(member) };

            if (!string.IsNullOrEmpty(typeText)
                && typeText.StartsWith("Array[", StringComparison.OrdinalIgnoreCase))
            {
                info.IsArray = true;
                info.Array = TryParseArrayType(typeText);

                var elementTypeText = info.Array == null ? null : info.Array.ElementTypeText;
                if (string.IsNullOrWhiteSpace(elementTypeText))
                {
                    elementTypeText = typeText;
                }

                var resolved = ResolveTypeName(elementTypeText, rid, isNamedValueType);
                info.Softdatatype = resolved.Softdatatype;
                info.IsComplex = resolved.IsComplex;
                info.ElementTypeName = elementTypeText;
                // Keep the complete declaration on the array node (it is useful in the UI); the
                // element nodes use ElementTypeName. Symbolic bounds are resolved when the array is
                // expanded, through the public GetTagBySymbolAsync API.
                info.NativeName = typeText;
                info.StringLength = ParseStringLength(elementTypeText);

                if (info.IsComplex)
                {
                    info.TargetContext = GetTargetContext(context, member);
                }

                return info;
            }

            if ((rid & 0xFFFF0000) == 0x02010000)
            {
                // Primitive array without an explicit Type attribute. Bounds are unavailable, but the
                // member is still reported as an array so the UI does not treat it as a scalar.
                info.IsArray = true;
                var resolved = ResolveTypeName(null, rid);
                info.Softdatatype = resolved.Softdatatype;
                info.IsComplex = resolved.IsComplex;
                info.NativeName = GetNativeName(rid, info.Softdatatype);
                info.ElementTypeName = info.NativeName;

                if (info.IsComplex)
                {
                    info.TargetContext = GetTargetContext(context, member);
                }

                return info;
            }

            var scalarResolved = ResolveTypeName(typeText, rid, isNamedValueType);
            info.Softdatatype = scalarResolved.Softdatatype;
            info.IsComplex = scalarResolved.IsComplex;
            info.StringLength = ParseStringLength(typeText);

            if (!string.IsNullOrEmpty(typeText))
            {
                info.NativeName = typeText;
            }
            else
            {
                var rawSoftdatatype = (rid & 0xFFFF0000) == 0x02000000 ? rid & 0xFF : 0;
                info.NativeName = rawSoftdatatype != 0
                    ? GetNativeName(rid, rawSoftdatatype)
                    : GetNativeName(rid, info.Softdatatype);
            }

            info.ElementTypeName = info.NativeName;

            ApplyTargetContext(context, member, info);

            return info;
        }

        /// <summary>
        /// Attaches a referenced XML subpart to the member. Some system data types (for example
        /// TCON_IP_v4) are carried with a primitive-looking RID, but still have a structural source
        /// part in the interface XML. A referenced part with members therefore takes precedence over
        /// the RID-based scalar classification.
        /// </summary>
        private static void ApplyTargetContext(PartContext context, XElement member, MemberTypeInfo info)
        {
            var target = GetTargetContext(context, member);
            if (target == null)
            {
                return;
            }

            if (!info.IsComplex)
            {
                if (!DirectMembers(target).Any())
                {
                    return;
                }

                info.IsComplex = true;
                info.Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT;
            }

            info.TargetContext = target;
        }

        private static TypeResolution ResolveTypeName(
            string typeName, uint rid, bool isNamedValueType = false)
        {
            if (!string.IsNullOrWhiteSpace(typeName))
            {
                var text = typeName.Trim();
                var baseName = text;
                var bracket = baseName.IndexOf('[');
                if (bracket >= 0)
                {
                    baseName = baseName.Substring(0, bracket).Trim();
                }

                uint mapped;
                if (NativeTypeMap.TryGetValue(baseName, out mapped))
                {
                    return IsStructLikeSoftdatatype(mapped)
                        ? new TypeResolution(Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT, true)
                        : new TypeResolution(mapped, false);
                }

                // Unknown plain system type (TCON_IP_v4, TADDR, TSEND, ...). These types carry a
                // primitive-looking RID, but are structural. Enums/aliases are qualified ("_.e_..." or
                // quoted) or marked with NVT and still use the RID transport type.
                if (!isNamedValueType && IsPlainSystemTypeName(baseName))
                {
                    return new TypeResolution(Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT, true);
                }

                // Enums and named aliases are transported as their base datatype: their RID is a
                // primitive RID (0x0200xxxx for scalars, 0x0201xxxx for arrays) whose low byte holds
                // the actual transport datatype. The Type attribute only carries the enum/alias name.
                uint primitiveFromRid;
                if (TryGetPrimitiveSoftdatatype(rid, out primitiveFromRid))
                {
                    return IsStructLikeSoftdatatype(primitiveFromRid)
                        ? new TypeResolution(Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT, true)
                        : new TypeResolution(primitiveFromRid, false);
                }

                // Unknown complex type name (UDT, library FB, ...).
                return new TypeResolution(Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT, true);
            }

            uint softdatatype;
            if (TryGetPrimitiveSoftdatatype(rid, out softdatatype))
            {
                return IsStructLikeSoftdatatype(softdatatype)
                    ? new TypeResolution(Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT, true)
                    : new TypeResolution(softdatatype, false);
            }

            return new TypeResolution(Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT, true);
        }

        private static bool TryGetPrimitiveSoftdatatype(uint rid, out uint softdatatype)
        {
            var family = rid & 0xFFFF0000;
            if (family == 0x02000000 || family == 0x02010000)
            {
                softdatatype = rid & 0xFF;
                return true;
            }

            softdatatype = 0;
            return false;
        }

        private static bool IsPlainSystemTypeName(string baseName)
        {
            if (string.IsNullOrWhiteSpace(baseName))
            {
                return false;
            }

            // Quoted UDT names and qualified enum/alias names are handled by the existing rules.
            if (baseName[0] == '"' || baseName.StartsWith("_.", StringComparison.Ordinal))
            {
                return false;
            }

            // Known primitives, IEC types and system handles (CONN_OUC, HW_ANY, ...) stay scalars.
            return !NativeTypeMap.ContainsKey(baseName);
        }

        private static bool IsOpaqueSystemTypeName(string nativeName) =>
            string.Equals(nativeName, "DTL", StringComparison.OrdinalIgnoreCase);

        private static bool IsStructLikeSoftdatatype(uint softdatatype)
        {
            switch (softdatatype)
            {
                case Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT:
                case Softdatatype.S7COMMP_SOFTDATATYPE_MULTIFB:
                case Softdatatype.S7COMMP_SOFTDATATYPE_BLOCKFB:
                case Softdatatype.S7COMMP_SOFTDATATYPE_BLOCKFC:
                case Softdatatype.S7COMMP_SOFTDATATYPE_BLOCKSDB:
                case Softdatatype.S7COMMP_SOFTDATATYPE_MULTISFB:
                case Softdatatype.S7COMMP_SOFTDATATYPE_IECTIMER:
                case Softdatatype.S7COMMP_SOFTDATATYPE_IECCOUNTER:
                case Softdatatype.S7COMMP_SOFTDATATYPE_IECSCOUNTER:
                case Softdatatype.S7COMMP_SOFTDATATYPE_IECDCOUNTER:
                case Softdatatype.S7COMMP_SOFTDATATYPE_IECLCOUNTER:
                case Softdatatype.S7COMMP_SOFTDATATYPE_BLOCKSFB:
                case Softdatatype.S7COMMP_SOFTDATATYPE_BLOCKSFC:
                case Softdatatype.S7COMMP_SOFTDATATYPE_BLOCKCB:
                case Softdatatype.S7COMMP_SOFTDATATYPE_BLOCKSCB:
                case Softdatatype.S7COMMP_SOFTDATATYPE_BLOCKOB:
                case Softdatatype.S7COMMP_SOFTDATATYPE_BLOCKUDT:
                case Softdatatype.S7COMMP_SOFTDATATYPE_DTL:
                case Softdatatype.S7COMMP_SOFTDATATYPE_IECLTIMER:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsStringDatatype(uint softdatatype)
        {
            return softdatatype == Softdatatype.S7COMMP_SOFTDATATYPE_STRING
                || softdatatype == Softdatatype.S7COMMP_SOFTDATATYPE_WSTRING;
        }

        private static string GetNativeName(uint rid, uint softdatatype)
        {
            string name;
            if (Softdatatype.Types.TryGetValue(softdatatype, out name))
            {
                return name;
            }

            if ((rid & 0xFFFF0000) == 0x02000000
                && Softdatatype.Types.TryGetValue(rid & 0xFF, out name))
            {
                return name;
            }

            return rid != 0 ? $"0x{rid:X8}" : softdatatype.ToString(CultureInfo.InvariantCulture);
        }

        private static int ParseStringLength(string typeText)
        {
            if (string.IsNullOrWhiteSpace(typeText))
            {
                return 0;
            }

            var match = StringTypeRegex.Match(typeText.Trim());
            if (!match.Success)
            {
                return 0;
            }

            int single;
            if (int.TryParse(match.Groups["single"].Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out single))
            {
                return single;
            }

            int lower;
            int upper;
            if (int.TryParse(match.Groups["lo"].Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out lower)
                && int.TryParse(match.Groups["hi"].Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out upper)
                && upper >= lower)
            {
                return upper - lower + 1;
            }

            return 0;
        }

        private static ArrayInfo TryParseArrayType(string typeText)
        {
            var match = ArrayTypeRegex.Match(typeText.Trim());
            if (!match.Success)
            {
                return null;
            }

            var info = new ArrayInfo { ElementTypeText = match.Groups["element"].Value.Trim() };

            // Variable-length arrays (Array[*], Array[*,*]) have no static bounds and are not part
            // of the browsable node tree. Only the dimension part is checked so a '*' inside an
            // element type name cannot trigger this.
            if (match.Groups["dims"].Value.IndexOf('*') >= 0)
            {
                info.HasWildcardDimensions = true;
                return info;
            }

            foreach (var dimensionText in match.Groups["dims"].Value.Split(','))
            {
                var range = ArrayRangeRegex.Match(dimensionText.Trim());
                int lower;
                int upper;
                if (!range.Success
                    || !int.TryParse(range.Groups["lo"].Value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out lower)
                    || !int.TryParse(range.Groups["hi"].Value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out upper)
                    || upper < lower)
                {
                    // At least one bound is symbolic (a PLC constant or variable, e.g.
                    // Array[1.."c_i_maxNumberOfModule"] of "st_ModuleConfiguration"). The element
                    // type is still known, but the numeric dimensions must be resolved through the
                    // PLC tag (GetTagBySymbolAsync -> PlcTag.ArrayDimensions).
                    info.Dimensions.Clear();
                    return info;
                }

                info.Dimensions.Add(new S7CommPlusArrayDimension(lower, (uint)(upper - lower + 1)));
            }

            return info;
        }

        private static IEnumerable<int[]> EnumerateArrayIndices(
            IReadOnlyList<S7CommPlusArrayDimension> dimensions)
        {
            var dimensionCount = dimensions.Count;
            if (dimensionCount == 0)
            {
                yield break;
            }

            var counters = new int[dimensionCount];
            while (true)
            {
                var indices = new int[dimensionCount];
                for (var dimension = 0; dimension < dimensionCount; dimension++)
                {
                    indices[dimension] = dimensions[dimension].LowerBound + counters[dimension];
                }

                yield return indices;

                var position = dimensionCount - 1;
                while (position >= 0)
                {
                    counters[position]++;
                    if (counters[position] < (int)dimensions[position].ElementCount)
                    {
                        break;
                    }

                    counters[position] = 0;
                    position--;
                }

                if (position < 0)
                {
                    yield break;
                }
            }
        }

        private static uint ParseRid(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            var value = text.Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(2);
            }

            value = value.Replace("_", string.Empty);
            uint rid;
            return uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rid)
                ? rid
                : 0;
        }

        private static int ParseLid(XElement member)
        {
            var text = Attr(member, "LID");
            int lid;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out lid)
                ? lid
                : 0;
        }

        private static Dictionary<string, uint> BuildNativeTypeMap()
        {
            var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in Softdatatype.Types)
            {
                map[entry.Value] = entry.Key;
            }

            map["Date_And_Time"] = Softdatatype.S7COMMP_SOFTDATATYPE_DATEANDTIME;
            map["Time_Of_Day"] = Softdatatype.S7COMMP_SOFTDATATYPE_TIMEOFDAY;
            map["LTOD"] = Softdatatype.S7COMMP_SOFTDATATYPE_LTOD;
            map["LDT"] = Softdatatype.S7COMMP_SOFTDATATYPE_LDT;
            map["IEC_LTIMER"] = Softdatatype.S7COMMP_SOFTDATATYPE_IECLTIMER;
            map["IEC_SCOUNTER"] = Softdatatype.S7COMMP_SOFTDATATYPE_IECSCOUNTER;
            return map;
        }

        private static XElement Child(XElement parent, string localName)
        {
            return parent.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal));
        }

        private static IEnumerable<XElement> Children(XElement parent, string localName)
        {
            return parent.Elements().Where(element =>
                string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal));
        }

        private static string Attr(XElement element, string localName)
        {
            var attribute = element.Attributes().FirstOrDefault(candidate =>
                string.Equals(candidate.Name.LocalName, localName, StringComparison.Ordinal));
            return attribute == null ? null : attribute.Value;
        }

        private readonly struct TypeResolution
        {
            public TypeResolution(uint softdatatype, bool isComplex)
            {
                Softdatatype = softdatatype;
                IsComplex = isComplex;
            }

            public uint Softdatatype { get; }
            public bool IsComplex { get; }
        }

        private sealed class PartContext
        {
            public PartContext(XElement part, List<XElement> subParts, bool isStructure)
            {
                Part = part;
                SubParts = subParts;
                IsStructure = isStructure;
            }

            public XElement Part { get; }
            public List<XElement> SubParts { get; }
            public bool IsStructure { get; }
        }

        private sealed class MemberTypeInfo
        {
            public int Lid { get; set; }
            public bool IsArray { get; set; }
            public bool IsComplex { get; set; }
            public uint Softdatatype { get; set; }
            public string NativeName { get; set; } = string.Empty;
            public string ElementTypeName { get; set; } = string.Empty;
            public int StringLength { get; set; }
            public ArrayInfo Array { get; set; }
            public PartContext TargetContext { get; set; }
            public bool ElementHasChildren =>
                IsComplex && !IsOpaqueSystemTypeName(ElementTypeName);
        }

        private sealed class ArrayInfo
        {
            public string ElementTypeText { get; set; } = string.Empty;
            public bool HasWildcardDimensions { get; set; }
            public List<S7CommPlusArrayDimension> Dimensions { get; } = new List<S7CommPlusArrayDimension>();
        }
    }
}
