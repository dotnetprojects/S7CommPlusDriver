using System;

namespace S7CommPlusDriver
{
    /// <summary>
    /// Controls how PLC symbols are represented by <see cref="S7CommPlusClient.BrowseAsync(S7CommPlusBrowseOptions, System.Threading.CancellationToken)"/>.
    /// </summary>
    /// <remarks>
    /// Arrays of structures are always traversed so their readable members remain discoverable. This option only affects
    /// arrays whose elements are directly readable primitive PLC values such as <c>BYTE</c>, <c>BOOL</c>, or <c>STRING</c>.
    /// </remarks>
    public sealed class S7CommPlusBrowseOptions
    {
        /// <summary>
        /// Gets or sets whether every primitive-array element is returned as a separate browse item.
        /// </summary>
        /// <remarks>
        /// The default is <see langword="false"/>, which returns one <see cref="VarInfo"/> for the array and describes its
        /// bounds through <see cref="VarInfo.ArrayDimensions"/>. Enable this only when a caller intentionally needs the
        /// legacy flattened representation such as <c>Payload[0]</c>, <c>Payload[1]</c>, and so on.
        /// </remarks>
        public bool ExpandPrimitiveArrayElements { get; set; }
    }

    /// <summary>
    /// Describes one PLC array dimension using its declared lower bound and number of elements.
    /// </summary>
    /// <param name="lowerBound">The first valid PLC index in this dimension.</param>
    /// <param name="elementCount">The number of valid indices in this dimension.</param>
    public sealed class S7CommPlusArrayDimension
    {
        /// <summary>
        /// Initializes an immutable description of one PLC array dimension.
        /// </summary>
        /// <param name="lowerBound">The first valid PLC index in this dimension.</param>
        /// <param name="elementCount">The number of valid indices in this dimension.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="elementCount"/> is zero.</exception>
        public S7CommPlusArrayDimension(int lowerBound, uint elementCount)
        {
            if (elementCount == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(elementCount), "An array dimension must contain at least one element.");
            }

            LowerBound = lowerBound;
            ElementCount = elementCount;
        }

        /// <summary>
        /// Gets the first valid PLC index in this dimension.
        /// </summary>
        public int LowerBound { get; }

        /// <summary>
        /// Gets the number of valid indices in this dimension.
        /// </summary>
        public uint ElementCount { get; }

        /// <summary>
        /// Gets the last valid PLC index without narrowing the unsigned element count.
        /// </summary>
        public long UpperBound => (long)LowerBound + ElementCount - 1;
    }
}
