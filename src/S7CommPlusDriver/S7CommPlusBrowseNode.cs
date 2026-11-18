namespace S7CommPlusDriver
{
    /// <summary>
    /// Describes one direct child node of a lazily browsed PLC data-block interface.
    /// </summary>
    /// <remarks>
    /// The node is produced by <see cref="S7CommPlusSymbolBrowser"/> from the block interface XML reported by the PLC or,
    /// for system types without an embedded subpart, from the flat symbol catalog returned by
    /// <see cref="S7CommPlusClient.BrowseAsync(System.Threading.CancellationToken)"/>.
    /// </remarks>
    public sealed class S7CommPlusBrowseNode
    {
        /// <summary>Gets or sets the member name as declared in the data block.</summary>
        public string Name { get; set; }

        /// <summary>Gets or sets the symbolic path that expands this node in <see cref="S7CommPlusSymbolBrowser"/>.</summary>
        public string SymbolPath { get; set; }

        /// <summary>
        /// Gets or sets the S7 soft-datatype used to transport the value, or
        /// <see cref="Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT"/> when <see cref="IsStructure"/> is set.
        /// </summary>
        public uint Softdatatype { get; set; }

        /// <summary>
        /// Gets or sets whether the node aggregates members instead of representing a scalar value.
        /// </summary>
        public bool IsStructure { get; set; }

        /// <summary>
        /// Gets or sets the declared PLC type name, for example <c>Bool</c>, <c>TCON_IP_v4</c>, or
        /// <c>Array[0..2] of Bool</c>.
        /// </summary>
        public string NativeTypeName { get; set; }

        /// <summary>Gets or sets whether browsing this node returns further children.</summary>
        public bool HasChildren { get; set; }

        /// <summary>Gets or sets whether the node is declared as an array.</summary>
        public bool IsArray { get; set; }

        /// <summary>
        /// Gets or sets the element count when the node is a one-dimensional array, or <see langword="null"/> for scalars,
        /// multidimensional arrays, and arrays whose symbolic bounds have not been resolved.
        /// </summary>
        public int? ArrayDimension { get; set; }

        /// <summary>
        /// Gets or sets the maximum character count for <c>String</c>/<c>WString</c> values, or <see langword="null"/> for
        /// every other datatype.
        /// </summary>
        public int? StringLength { get; set; }

        /// <summary>
        /// Gets or sets the accumulated protocol access sequence of a leaf node, or <see langword="null"/> when the node
        /// must be expanded before a leaf address is available.
        /// </summary>
        public string AccessSequence { get; set; }

        /// <inheritdoc />
        public override string ToString()
        {
            return SymbolPath ?? Name;
        }
    }
}
