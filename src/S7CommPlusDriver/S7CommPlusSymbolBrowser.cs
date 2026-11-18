using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using S7CommPlusDriver.ClientApi;
using S7CommPlusDriver.Internal;

namespace S7CommPlusDriver
{
    /// <summary>
    /// Provides lazy, per-level browsing of the PLC data blocks visible in the variable browser.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Browsing is built from the public client API:
    /// <see cref="S7CommPlusClient.BrowseBlocksAsync(System.Threading.CancellationToken)"/> supplies the data-block list and
    /// <see cref="S7CommPlusClient.GetBlockContentsAsync(System.Collections.Generic.IReadOnlyList{uint}, System.Threading.CancellationToken, bool)"/>
    /// with <c>sourceOnly: true</c> supplies the interface XML of exactly one data block.
    /// </para>
    /// <para>
    /// The interface XML is parsed one node level at a time. At most one data-block interface is cached, never the complete
    /// PLC symbol tree, so memory usage stays bounded by the currently browsed data block. System types (IEC timers,
    /// R_TRIG, F_TRIG, ...) are referenced without an embedded subpart; for those nodes the driver symbol snapshot is used
    /// to resolve their members. Assign <see cref="VariableSnapshotProvider"/> to reuse an already loaded snapshot instead
    /// of browsing the PLC again.
    /// </para>
    /// <para>
    /// One instance represents the browse state of one connection. Call <see cref="ClearCache"/> after a reconnect or a PLC
    /// program change.
    /// </para>
    /// </remarks>
    public sealed class S7CommPlusSymbolBrowser
    {
        private readonly S7CommPlusClient _client;
        private readonly ILogger _logger;
        private readonly string _endpoint;
        private readonly object _cacheLock = new object();

        private List<S7CommPlusBlockInfo> _dbBlockListCache;
        private Task<List<S7CommPlusBlockInfo>> _dbBlockListTask;
        private DbInterfaceCache _dbInterfaceCache;
        private IReadOnlyList<VarInfo> _variableSnapshot;

        /// <summary>
        /// Initializes a browser for one connected client.
        /// </summary>
        /// <param name="client">The client whose PLC is browsed.</param>
        /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
        public S7CommPlusSymbolBrowser(S7CommPlusClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            var options = client.Options;
            _logger = options.Logger ?? NullLogger.Instance;
            _endpoint = options.Address + ":" + options.Port;
        }

        /// <summary>
        /// Gets or sets an optional provider for the flat driver symbol snapshot used to expand system types that are not
        /// embedded in the data-block interface XML.
        /// </summary>
        /// <remarks>
        /// Set this to reuse a snapshot that the application already loaded; the browser then never triggers an additional
        /// <see cref="S7CommPlusClient.BrowseAsync(System.Threading.CancellationToken)"/>. The provider may throw
        /// <see cref="OperationCanceledException"/>; every other failure is logged and treated as "not expandable".
        /// </remarks>
        public Func<CancellationToken, Task<IReadOnlyList<VarInfo>>> VariableSnapshotProvider { get; set; }

        /// <summary>
        /// Browses one level below <paramref name="symbolPath"/>.
        /// </summary>
        /// <param name="symbolPath">
        /// The symbolic path returned in <see cref="S7CommPlusBrowseNode.SymbolPath"/>, or <see langword="null"/>/empty to
        /// browse the data blocks themselves.
        /// </param>
        /// <param name="cancellationToken">Cancels the PLC requests.</param>
        /// <returns>The direct children of the addressed node.</returns>
        /// <exception cref="S7CommPlusException">
        /// The addressed data block or member does not exist, or the member has no browsable children.
        /// </exception>
        /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
        public async Task<IReadOnlyList<S7CommPlusBrowseNode>> BrowseNodeAsync(
            string symbolPath, CancellationToken cancellationToken = default)
        {
            var dbBlocks = await GetOrLoadDbBlockListAsync(cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrEmpty(symbolPath))
            {
                return BrowseTopLevel(dbBlocks);
            }

            List<string> segments;
            S7CommPlusBlockInfo dbBlock;
            int dbSegmentCount;
            try
            {
                segments = S7CommPlusSymbolPath.Split(symbolPath);
                dbBlock = FindDbBlock(dbBlocks, segments, out dbSegmentCount);
                if (dbBlock == null)
                {
                    throw new InvalidOperationException(
                        $"Data block '{S7CommPlusSymbolPath.Unquote(segments[0])}' not found.");
                }
            }
            catch (InvalidOperationException ex)
            {
                throw CreateMetadataException(ex);
            }

            var dbInterface = await GetOrLoadDbInterfaceAsync(dbBlock, cancellationToken).ConfigureAwait(false);
            if (dbInterface == null || dbInterface.Root == null)
            {
                return new List<S7CommPlusBrowseNode>();
            }

            // Normalize the path so the reader always sees the exact DB name in segment 0 and all
            // following segments are member path segments. This also accepts unquoted DB names that
            // contain dots (e.g. "SC.GC.db_AlarmList"), not only the quoted form emitted by the browser.
            var normalizedSegments = new List<string>(segments.Count - dbSegmentCount + 1)
            {
                dbBlock.Name
            };
            normalizedSegments.AddRange(segments.Skip(dbSegmentCount));

            try
            {
                return await S7CommPlusBlockInterfaceReader.BrowseInDbAsync(
                        dbInterface.Root,
                        dbBlock.Name,
                        dbBlock.RelationId,
                        normalizedSegments,
                        dbInterface.ArrayDimensions,
                        ResolveTagForBrowseAsync,
                        cancellationToken,
                        // System types (IEC timers, R_TRIG, F_TRIG, ...) are referenced by the DB
                        // source XML without an embedded SubPart. Their members are only available
                        // through the driver's flat symbol browse, so fall back to that snapshot when
                        // the XML cannot expand a node.
                        token => TryBrowseSystemNodeAsync(symbolPath, token))
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                throw CreateMetadataException(ex);
            }
        }

        /// <summary>
        /// Clears the cached data-block list, the cached data-block interface, and the cached symbol snapshot.
        /// </summary>
        /// <returns><see langword="true"/> when anything was actually cached.</returns>
        public bool ClearCache()
        {
            lock (_cacheLock)
            {
                var hadCachedState = _dbBlockListCache != null || _dbInterfaceCache != null;
                _dbBlockListCache = null;
                _dbBlockListTask = null;
                _dbInterfaceCache = null;
                _variableSnapshot = null;
                return hadCachedState;
            }
        }

        private S7CommPlusException CreateMetadataException(InvalidOperationException exception)
        {
            return new S7CommPlusException(
                "BrowseNode",
                _endpoint,
                S7Consts.errS7CommPlusMetadataUnavailable,
                false,
                exception.Message,
                exception);
        }

        private static IReadOnlyList<S7CommPlusBrowseNode> BrowseTopLevel(List<S7CommPlusBlockInfo> dbBlocks)
        {
            var nodes = new List<S7CommPlusBrowseNode>(dbBlocks.Count);
            foreach (var db in dbBlocks)
            {
                // db.Name can itself contain dots (e.g. "SC.EX.db_AlarmList"). Quote it so
                // BrowseNodeAsync later recognizes it as a single identifier instead of hierarchy
                // separators.
                nodes.Add(new S7CommPlusBrowseNode
                {
                    Name = db.Name,
                    SymbolPath = S7CommPlusSymbolPath.QuoteIfContainsDot(db.Name),
                    Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT,
                    IsStructure = true,
                    NativeTypeName = Softdatatype.Types[Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT],
                    HasChildren = true,
                    AccessSequence = db.RelationId.ToString("X", CultureInfo.InvariantCulture)
                });
            }

            return nodes;
        }

        /// <summary>
        /// Finds the data block with the longest matching name prefix. A DB whose name contains dots wins over a shorter
        /// DB name that happens to be its prefix.
        /// </summary>
        internal static S7CommPlusBlockInfo FindDbBlock(
            List<S7CommPlusBlockInfo> dbBlocks, List<string> segments, out int segmentCount)
        {
            for (var count = segments.Count; count >= 1; count--)
            {
                var candidate = string.Join(".", segments.Take(count).Select(S7CommPlusSymbolPath.Unquote));
                var match = dbBlocks.Find(block =>
                    string.Equals(block.Name, candidate, StringComparison.Ordinal));
                if (match != null)
                {
                    segmentCount = count;
                    return match;
                }
            }

            segmentCount = 0;
            return null;
        }

        private async Task<List<S7CommPlusBlockInfo>> GetOrLoadDbBlockListAsync(CancellationToken cancellationToken)
        {
            lock (_cacheLock)
            {
                if (_dbBlockListCache != null)
                {
                    return _dbBlockListCache;
                }
            }

            Task<List<S7CommPlusBlockInfo>> task;
            lock (_cacheLock)
            {
                task = _dbBlockListTask;
                if (task == null)
                {
                    task = LoadDbBlockListAsync(cancellationToken);
                    _dbBlockListTask = task;
                }
            }

            List<S7CommPlusBlockInfo> list;
            try
            {
                list = await task.ConfigureAwait(false);
            }
            catch
            {
                // Do not keep a faulted single-flight task: a later browse must be able to retry.
                lock (_cacheLock)
                {
                    if (ReferenceEquals(_dbBlockListTask, task))
                    {
                        _dbBlockListTask = null;
                    }
                }

                throw;
            }

            lock (_cacheLock)
            {
                if (ReferenceEquals(_dbBlockListTask, task))
                {
                    _dbBlockListCache = list;
                    _dbBlockListTask = null;
                }

                return _dbBlockListCache ?? list;
            }
        }

        private async Task<List<S7CommPlusBlockInfo>> LoadDbBlockListAsync(CancellationToken cancellationToken)
        {
            var blocks = await _client.BrowseBlocksAsync(cancellationToken).ConfigureAwait(false);
            return blocks
                .Where(block => block.Type == S7CommPlusBlockType.DB)
                .Where(block => !string.IsNullOrEmpty(block.Name))
                .ToList();
        }

        /// <summary>
        /// Loads the interface XML of exactly one data block. The result is cached per DB relation ID so navigating within
        /// the same data block does not repeat the block read. The cache holds at most one interface, never the complete
        /// PLC symbol tree.
        /// </summary>
        private async Task<DbInterfaceCache> GetOrLoadDbInterfaceAsync(
            S7CommPlusBlockInfo dbBlock, CancellationToken cancellationToken)
        {
            lock (_cacheLock)
            {
                var cached = _dbInterfaceCache;
                if (cached != null && cached.RelationId == dbBlock.RelationId)
                {
                    return cached;
                }
            }

            S7CommPlusClientBlockContent content = null;
            try
            {
                var contents = await _client.GetBlockContentsAsync(
                    new uint[] { dbBlock.RelationId }, cancellationToken, sourceOnly: true).ConfigureAwait(false);
                content = contents.FirstOrDefault();
            }
            catch (Exception ex) when (ex is S7CommPlusException || ex is ArgumentException)
            {
                _logger.LogWarning(ex, "Could not load the interface of data block '{DbName}'.", dbBlock.Name);
            }

            XElement root = null;
            var xml = content == null ? null : content.InterfaceDescription;
            if (!string.IsNullOrWhiteSpace(xml))
            {
                try
                {
                    root = XElement.Parse(xml, LoadOptions.None);
                }
                catch (XmlException ex)
                {
                    _logger.LogWarning(ex, "The interface XML of data block '{DbName}' is invalid.", dbBlock.Name);
                }
            }

            var dbInterface = new DbInterfaceCache(dbBlock.RelationId, root);
            lock (_cacheLock)
            {
                _dbInterfaceCache = dbInterface;
            }

            return dbInterface;
        }

        private async Task<PlcTag> ResolveTagForBrowseAsync(
            string symbolPath, CancellationToken cancellationToken)
        {
            try
            {
                return await _client.GetTagBySymbolAsync(symbolPath, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex, "Could not resolve array dimensions for '{SymbolPath}'.", symbolPath);
                return null;
            }
        }

        /// <summary>
        /// Builds the direct children of <paramref name="symbolPath"/> from the flat driver symbol snapshot. The DB
        /// interface XML does not embed the members of system types such as IEC timers or R_TRIG; the driver's browser
        /// expands them through the PLC type information, so the snapshot contains their leaves (for example
        /// <c>db.trig.CLK</c>).
        /// </summary>
        private async Task<IReadOnlyList<S7CommPlusBrowseNode>> TryBrowseSystemNodeAsync(
            string symbolPath, CancellationToken cancellationToken)
        {
            IReadOnlyList<VarInfo> snapshot;
            try
            {
                snapshot = await GetVariableSnapshotAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not load the driver symbol snapshot for the system-type browse fallback of '{SymbolPath}'.",
                    symbolPath);
                return null;
            }

            if (snapshot == null || snapshot.Count == 0)
            {
                return null;
            }

            var nodes = S7CommPlusBlockInterfaceReader.BuildVarInfoChildNodes(symbolPath, snapshot);
            return nodes.Count == 0 ? null : nodes;
        }

        private async Task<IReadOnlyList<VarInfo>> GetVariableSnapshotAsync(CancellationToken cancellationToken)
        {
            var provider = VariableSnapshotProvider;
            if (provider != null)
            {
                return await provider(cancellationToken).ConfigureAwait(false);
            }

            lock (_cacheLock)
            {
                if (_variableSnapshot != null && _variableSnapshot.Count > 0)
                {
                    return _variableSnapshot;
                }
            }

            var variables = await _client.BrowseAsync(cancellationToken).ConfigureAwait(false);
            lock (_cacheLock)
            {
                _variableSnapshot = variables;
            }

            return variables;
        }

        private sealed class DbInterfaceCache
        {
            public DbInterfaceCache(uint relationId, XElement root)
            {
                RelationId = relationId;
                Root = root;
            }

            public uint RelationId { get; }

            public XElement Root { get; }

            /// <summary>
            /// Resolved array dimensions per symbol path within this data-block interface. Populated lazily through
            /// GetTagBySymbolAsync for arrays with symbolic bounds.
            /// </summary>
            public Dictionary<string, List<S7CommPlusArrayDimension>> ArrayDimensions { get; } =
                new Dictionary<string, List<S7CommPlusArrayDimension>>(StringComparer.Ordinal);
        }
    }
}
