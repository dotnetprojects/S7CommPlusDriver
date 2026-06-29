using Microsoft.Extensions.Logging;
using S7CommPlusDriver.Alarming;
using S7CommPlusDriver.ClientApi;
using S7CommPlusDriver.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace S7CommPlusDriver
{
    public sealed class S7CommPlusClient : IAsyncDisposable
    {
        private readonly S7CommPlusClientOptions _options;
        private readonly Func<IS7CommPlusSession> _sessionFactory;
        private readonly SemaphoreSlim _operationGate = new SemaphoreSlim(1, 1);
        private IS7CommPlusSession _session;
        private bool _disposed;
        private S7CommPlusConnectionState _state = S7CommPlusConnectionState.Disconnected;

        public S7CommPlusClient(S7CommPlusClientOptions options)
            : this(options, () => new S7CommPlusProtocolSession())
        {
        }

        internal S7CommPlusClient(S7CommPlusClientOptions options, Func<IS7CommPlusSession> sessionFactory)
        {
            _options = options?.Clone() ?? throw new ArgumentNullException(nameof(options));
            _options.Validate();
            _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        }

        public event EventHandler<S7CommPlusConnectionStateChangedEventArgs> ConnectionStateChanged;
        public event EventHandler<S7CommPlusCommunicationErrorEventArgs> CommunicationError;

        public S7CommPlusConnectionState State => _state;
        public bool IsConnected => _session?.IsConnected == true && _state == S7CommPlusConnectionState.Connected;
        public S7CommPlusClientOptions Options => _options.Clone();

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ConnectCoreAsync(S7CommPlusConnectionState.Connecting, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
            {
                return;
            }

            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await DisconnectCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public Task<IReadOnlyList<VarInfo>> BrowseAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteReadOperationAsync("Browse", session =>
            {
                var error = session.BrowseVariables(out var vars);
                ThrowIfError("Browse", error);
                return (IReadOnlyList<VarInfo>)(vars ?? new List<VarInfo>());
            }, cancellationToken);
        }

        public Task<IReadOnlyList<S7CommPlusBlockInfo>> BrowseBlocksAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteReadOperationAsync("BrowseBlocks", session =>
            {
                var error = session.BrowseBlocks(out var blocks);
                ThrowIfError("BrowseBlocks", error);
                return (IReadOnlyList<S7CommPlusBlockInfo>)(blocks ?? new List<S7CommPlusBlockInfo>());
            }, cancellationToken);
        }

        public Task<S7CommPlusPlcStructureSnapshot> GetPlcStructureXmlAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteReadOperationAsync("GetPlcStructureXml", session =>
            {
                var error = session.GetPlcStructureXml(out var structure);
                ThrowIfError("GetPlcStructureXml", error);
                return structure ?? PlcStructureXmlParser.CreateSnapshot(string.Empty);
            }, cancellationToken);
        }

        public Task<IReadOnlyList<S7CommPlusPlcStructureNode>> BrowseBlockStructureAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteReadOperationAsync("BrowseBlockStructure", session =>
            {
                var error = session.GetPlcStructureXml(out var structure);
                ThrowIfError("BrowseBlockStructure", error);
                try
                {
                    return structure?.Structure ?? Array.Empty<S7CommPlusPlcStructureNode>();
                }
                catch (Exception ex)
                {
                    throw new S7CommPlusConnectionException(
                        "BrowseBlockStructure",
                        Endpoint,
                        S7Consts.errIsoInvalidPDU,
                        false,
                        $"BrowseBlockStructure failed for PLC {Endpoint}: PLC structure XML could not be parsed.",
                        ex);
                }
            }, cancellationToken);
        }

        public Task<S7CommPlusClientBlockContent> GetBlockContentAsync(uint relid, CancellationToken cancellationToken = default)
        {
            return ExecuteReadOperationAsync("GetBlockContent", session =>
            {
                var error = session.GetBlockContent(relid, out var blockContent);
                ThrowIfError("GetBlockContent", error);
                return blockContent;
            }, cancellationToken);
        }

        public Task<S7CommPlusCpuInfo> GetCpuInfoAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteReadOperationAsync("GetCpuInfo", session =>
            {
                var error = session.GetCpuInfo(out var cpuInfo);
                ThrowIfError("GetCpuInfo", error);
                return cpuInfo;
            }, cancellationToken);
        }

        public Task<S7CommPlusCpuCultureInfo> GetCpuCultureInfoAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteReadOperationAsync("GetCpuCultureInfo", session =>
            {
                var error = session.GetCpuCultureInfo(out var cultureInfo);
                ThrowIfError("GetCpuCultureInfo", error);
                return cultureInfo;
            }, cancellationToken);
        }

        /// <summary>
        /// Reads PLC text lists for all CPU languages, plus language-independent system text lists.
        /// </summary>
        public Task<S7CommPlusTextListCatalog> GetTextListsAsync(CancellationToken cancellationToken = default)
        {
            return GetTextListsAsync(Array.Empty<int>(), cancellationToken);
        }

        /// <summary>
        /// Reads PLC text lists for the requested LCIDs, plus language-independent system text lists.
        /// Pass an empty collection to request all CPU languages.
        /// </summary>
        public Task<S7CommPlusTextListCatalog> GetTextListsAsync(IEnumerable<int> languageIds, CancellationToken cancellationToken = default)
        {
            var languageIdList = languageIds?.ToList() ?? new List<int>();
            if (languageIdList.Any(languageId => languageId < 0 || languageId > UInt16.MaxValue))
            {
                throw new ArgumentOutOfRangeException(nameof(languageIds), "Language ids must be positive LCID values.");
            }

            return ExecuteReadOperationAsync("GetTextLists", session =>
            {
                var error = session.GetTextLists(languageIdList, out var textLists);
                ThrowIfError("GetTextLists", error);
                return textLists ?? S7CommPlusTextListCatalog.Empty;
            }, cancellationToken);
        }

        public Task<S7CommPlusCommunicationResources> GetCommunicationResourcesAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteReadOperationAsync("GetCommunicationResources", session =>
            {
                var error = session.GetCommunicationResources(out var resources);
                ThrowIfError("GetCommunicationResources", error);
                return new S7CommPlusCommunicationResources(resources);
            }, cancellationToken);
        }

        public async Task LegitimateAsync(string password, string username = "", CancellationToken cancellationToken = default)
        {
            if (password == null)
            {
                throw new ArgumentNullException(nameof(password));
            }
            username ??= string.Empty;

            await ExecuteSessionOperationAsync("Legitimate", session =>
            {
                var error = session.Legitimate(password, username);
                ThrowIfError("Legitimate", error);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the currently active PLC alarms and requests all alarm text languages returned by the PLC.
        /// The legacy <see cref="S7CommPlusAlarm.AlarmTexts"/> property contains the first returned language;
        /// use <see cref="S7CommPlusAlarm.AlarmTextsByLanguage"/> to access every returned language.
        /// </summary>
        public Task<IReadOnlyList<S7CommPlusAlarm>> GetActiveAlarmsAsync(CancellationToken cancellationToken = default)
        {
            return GetActiveAlarmsCoreAsync(0, cancellationToken);
        }

        /// <summary>
        /// Reads the currently active PLC alarms and selects the requested alarm text language from the returned text payload.
        /// </summary>
        /// <param name="languageId">LCID to expose through <see cref="S7CommPlusAlarm.AlarmTexts"/>, for example 1031 for de-DE.</param>
        public Task<IReadOnlyList<S7CommPlusAlarm>> GetActiveAlarmsAsync(int languageId, CancellationToken cancellationToken = default)
        {
            return GetActiveAlarmsAsync(languageId, null, cancellationToken);
        }

        /// <summary>
        /// Reads active PLC alarms and resolves text-list placeholders using a catalog returned by <see cref="GetTextListsAsync(CancellationToken)"/>.
        /// </summary>
        public Task<IReadOnlyList<S7CommPlusAlarm>> GetActiveAlarmsAsync(S7CommPlusTextListCatalog textLists, CancellationToken cancellationToken = default)
        {
            return GetActiveAlarmsCoreAsync(0, CreateTextListResolver(textLists), cancellationToken);
        }

        /// <summary>
        /// Reads active PLC alarms for one LCID and resolves text-list placeholders using a catalog returned by <see cref="GetTextListsAsync(CancellationToken)"/>.
        /// </summary>
        public Task<IReadOnlyList<S7CommPlusAlarm>> GetActiveAlarmsAsync(int languageId, S7CommPlusTextListCatalog textLists, CancellationToken cancellationToken = default)
        {
            if (languageId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(languageId), "Language ids must be positive LCID values.");
            }

            return GetActiveAlarmsCoreAsync(languageId, CreateTextListResolver(textLists), cancellationToken);
        }

        private Task<IReadOnlyList<S7CommPlusAlarm>> GetActiveAlarmsCoreAsync(int alarmTextLanguageId, CancellationToken cancellationToken)
        {
            return GetActiveAlarmsCoreAsync(alarmTextLanguageId, null, cancellationToken);
        }

        private Task<IReadOnlyList<S7CommPlusAlarm>> GetActiveAlarmsCoreAsync(int alarmTextLanguageId, Func<string, long, int, string> textListResolver, CancellationToken cancellationToken)
        {
            return ExecuteReadOperationAsync("GetActiveAlarms", session =>
            {
                var error = session.GetActiveAlarms(out var alarmList, alarmTextLanguageId, textListResolver);
                ThrowIfError("GetActiveAlarms", error);
                return (IReadOnlyList<S7CommPlusAlarm>)((alarmList ?? new List<S7CommPlusAlarm>()).Where(alarm => alarm != null).ToList());
            }, cancellationToken);
        }

        public async Task<S7CommPlusTagSubscription> SubscribeTagsAsync(IEnumerable<PlcTag> tags, S7CommPlusSubscriptionOptions options = null, CancellationToken cancellationToken = default)
        {
            if (tags == null)
            {
                throw new ArgumentNullException(nameof(tags));
            }

            var tagList = tags.ToList();
            if (tagList.Count == 0)
            {
                throw new ArgumentException("At least one tag is required.", nameof(tags));
            }
            if (tagList.Any(tag => tag == null))
            {
                throw new ArgumentException("Tag list cannot contain null entries.", nameof(tags));
            }

            var subscriptionOptions = (options ?? new S7CommPlusSubscriptionOptions()).Clone();
            subscriptionOptions.Validate(requireCycleTime: true);
            var tagsByReferenceId = tagList
                .Select((tag, index) => new KeyValuePair<uint, PlcTag>((uint)(index + 1), tag))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            var subscription = new S7CommPlusTagSubscription(tagsByReferenceId);

            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                await EnsureConnectedCoreAsync(cancellationToken).ConfigureAwait(false);
                uint subscriptionObjectId = 0;
                var error = await RunWithTimeoutAsync(
                    "CreateTagSubscription",
                    () => _session.CreateTagSubscription(tagList, subscriptionOptions.CycleTimeMilliseconds, subscriptionOptions.InitialCreditLimit, out subscriptionObjectId),
                    _options.RequestTimeout,
                    cancellationToken).ConfigureAwait(false);
                ThrowIfError("CreateTagSubscription", error);

                subscription.Start(token => RunTagSubscriptionLoopAsync(subscription, subscriptionOptions, subscriptionObjectId, token));
                return subscription;
            }
            catch (S7CommPlusException ex)
            {
                RaiseCommunicationError(ex);
                if (ex.IsTransient || ex is S7CommPlusTisWatchUnavailableException)
                {
                    SetState(S7CommPlusConnectionState.Faulted, ex);
                }
                subscription.MarkFaulted(ex);
                throw;
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public async Task<S7CommPlusTisWatchSubscription> OpenBlockOnlineViewAsync(S7CommPlusTisWatchRequest request, S7CommPlusSubscriptionOptions options = null, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            request.Validate();
            var watchRequest = request.Clone();
            var subscriptionOptions = (options ?? new S7CommPlusSubscriptionOptions()).Clone();
            subscriptionOptions.Validate(requireCycleTime: false);
            var subscription = new S7CommPlusTisWatchSubscription(watchRequest.ResultModel);

            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                await EnsureConnectedCoreAsync(cancellationToken).ConfigureAwait(false);
                uint subscriptionObjectId = 0;
                var error = await RunWithTimeoutAsync(
                    "CreateTisWatchSubscription",
                    () => _session.CreateTisWatchSubscription(watchRequest, out subscriptionObjectId),
                    _options.RequestTimeout,
                    cancellationToken).ConfigureAwait(false);
                var operation = String.IsNullOrWhiteSpace(watchRequest.LastLifecycleStage)
                    ? "CreateTisWatchSubscription"
                    : $"CreateTisWatchSubscription ({watchRequest.LastLifecycleStage})";
                ThrowIfError(operation, error);

                subscription.Start(token => RunTisWatchSubscriptionLoopAsync(subscription, subscriptionOptions, subscriptionObjectId, token));
                return subscription;
            }
            catch (S7CommPlusException ex)
            {
                RaiseCommunicationError(ex);
                if (ex.IsTransient || ex is S7CommPlusTisWatchUnavailableException)
                {
                    SetState(S7CommPlusConnectionState.Faulted, ex);
                }
                subscription.MarkFaulted(ex);
                throw;
            }
            finally
            {
                _operationGate.Release();
            }
        }

        /// <summary>
        /// Creates a live alarm subscription and requests all alarm text languages. The legacy
        /// <see cref="S7CommPlusAlarm.AlarmTexts"/> property contains the first returned language;
        /// use <see cref="S7CommPlusAlarm.AlarmTextsByLanguage"/> to access every returned language.
        /// </summary>
        public Task<S7CommPlusAlarmSubscription> SubscribeAlarmsAsync(S7CommPlusSubscriptionOptions options = null, CancellationToken cancellationToken = default)
        {
            return SubscribeAlarmsAsync(Array.Empty<int>(), 0, options, cancellationToken);
        }

        /// <summary>
        /// Creates a live alarm subscription and requests alarm texts for one LCID.
        /// </summary>
        public Task<S7CommPlusAlarmSubscription> SubscribeAlarmsAsync(int languageId, S7CommPlusSubscriptionOptions options = null, CancellationToken cancellationToken = default)
        {
            return SubscribeAlarmsAsync(new[] { languageId }, languageId, options, cancellationToken);
        }

        /// <summary>
        /// Creates a live alarm subscription for one LCID and resolves text-list placeholders using a catalog returned by <see cref="GetTextListsAsync(CancellationToken)"/>.
        /// </summary>
        public Task<S7CommPlusAlarmSubscription> SubscribeAlarmsAsync(int languageId, S7CommPlusTextListCatalog textLists, S7CommPlusSubscriptionOptions options = null, CancellationToken cancellationToken = default)
        {
            return SubscribeAlarmsAsync(new[] { languageId }, languageId, textLists, options, cancellationToken);
        }

        /// <summary>
        /// Creates a live alarm subscription first, then uses the supplied separate snapshot client to read the
        /// initially active alarms with all alarm text languages. Early live notifications are buffered by the subscription.
        /// </summary>
        public Task<S7CommPlusAlarmSubscriptionWithSnapshot> SubscribeAlarmsWithSnapshotAsync(S7CommPlusClient snapshotClient, S7CommPlusSubscriptionOptions options = null, CancellationToken cancellationToken = default)
        {
            return SubscribeAlarmsWithSnapshotAsync(snapshotClient, Array.Empty<int>(), 0, options, cancellationToken);
        }

        /// <summary>
        /// Creates a live alarm subscription first, then uses the supplied separate snapshot client to read the
        /// initially active alarms for the requested LCID. Early live notifications are buffered by the subscription.
        /// </summary>
        public Task<S7CommPlusAlarmSubscriptionWithSnapshot> SubscribeAlarmsWithSnapshotAsync(S7CommPlusClient snapshotClient, int languageId, S7CommPlusSubscriptionOptions options = null, CancellationToken cancellationToken = default)
        {
            return SubscribeAlarmsWithSnapshotAsync(snapshotClient, new[] { languageId }, languageId, options, cancellationToken);
        }

        /// <summary>
        /// Creates a live alarm subscription for one LCID, reads initially active alarms, and resolves text-list placeholders using a catalog returned by <see cref="GetTextListsAsync(CancellationToken)"/>.
        /// </summary>
        public Task<S7CommPlusAlarmSubscriptionWithSnapshot> SubscribeAlarmsWithSnapshotAsync(S7CommPlusClient snapshotClient, int languageId, S7CommPlusTextListCatalog textLists, S7CommPlusSubscriptionOptions options = null, CancellationToken cancellationToken = default)
        {
            return SubscribeAlarmsWithSnapshotAsync(snapshotClient, new[] { languageId }, languageId, textLists, options, cancellationToken);
        }

        /// <summary>
        /// Creates a live alarm subscription first, then uses the supplied separate snapshot client to read the
        /// initially active alarms. Pass an empty language collection to request all alarm text languages.
        /// </summary>
        public async Task<S7CommPlusAlarmSubscriptionWithSnapshot> SubscribeAlarmsWithSnapshotAsync(S7CommPlusClient snapshotClient, IEnumerable<int> languageIds, int alarmTextLanguageId, S7CommPlusSubscriptionOptions options = null, CancellationToken cancellationToken = default)
        {
            return await SubscribeAlarmsWithSnapshotAsync(snapshotClient, languageIds, alarmTextLanguageId, null, options, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a live alarm subscription first, then uses the supplied separate snapshot client to read the
        /// initially active alarms. Pass an empty language collection to request all alarm text languages.
        /// Text-list placeholders are resolved through the supplied catalog.
        /// </summary>
        public async Task<S7CommPlusAlarmSubscriptionWithSnapshot> SubscribeAlarmsWithSnapshotAsync(S7CommPlusClient snapshotClient, IEnumerable<int> languageIds, int alarmTextLanguageId, S7CommPlusTextListCatalog textLists, S7CommPlusSubscriptionOptions options = null, CancellationToken cancellationToken = default)
        {
            if (snapshotClient == null)
            {
                throw new ArgumentNullException(nameof(snapshotClient));
            }
            if (ReferenceEquals(this, snapshotClient))
            {
                throw new ArgumentException("The snapshot client must be a separate S7CommPlusClient instance.", nameof(snapshotClient));
            }

            var subscription = await SubscribeAlarmsAsync(languageIds, alarmTextLanguageId, textLists, options, cancellationToken).ConfigureAwait(false);
            try
            {
                var activeAlarms = await snapshotClient.GetActiveAlarmsCoreAsync(alarmTextLanguageId, CreateTextListResolver(textLists), cancellationToken).ConfigureAwait(false);
                return new S7CommPlusAlarmSubscriptionWithSnapshot(activeAlarms, subscription);
            }
            catch
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Creates a live alarm subscription. Pass an empty language collection to request all alarm text languages.
        /// The <paramref name="alarmTextLanguageId"/> selects the language exposed through the legacy
        /// <see cref="S7CommPlusAlarm.AlarmTexts"/> property; use 0 to expose the first returned language there and
        /// inspect <see cref="S7CommPlusAlarm.AlarmTextsByLanguage"/> for the full set.
        /// </summary>
        public async Task<S7CommPlusAlarmSubscription> SubscribeAlarmsAsync(IEnumerable<int> languageIds, int alarmTextLanguageId, S7CommPlusSubscriptionOptions options = null, CancellationToken cancellationToken = default)
        {
            return await SubscribeAlarmsAsync(languageIds, alarmTextLanguageId, null, options, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a live alarm subscription. Pass an empty language collection to request all alarm text languages.
        /// Text-list placeholders are resolved through the supplied catalog.
        /// </summary>
        public async Task<S7CommPlusAlarmSubscription> SubscribeAlarmsAsync(IEnumerable<int> languageIds, int alarmTextLanguageId, S7CommPlusTextListCatalog textLists, S7CommPlusSubscriptionOptions options = null, CancellationToken cancellationToken = default)
        {
            var languageIdList = languageIds?.ToList() ?? new List<int>();
            if (languageIdList.Any(languageId => languageId < 0))
            {
                throw new ArgumentOutOfRangeException(nameof(languageIds), "Language ids must be positive LCID values.");
            }
            if (alarmTextLanguageId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(alarmTextLanguageId), "Language ids must be positive LCID values.");
            }

            var subscriptionOptions = (options ?? new S7CommPlusSubscriptionOptions()).Clone();
            subscriptionOptions.Validate(requireCycleTime: false);
            var subscription = new S7CommPlusAlarmSubscription(alarmTextLanguageId, CreateTextListResolver(textLists));

            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                await EnsureConnectedCoreAsync(cancellationToken).ConfigureAwait(false);
                var languageIdsUint = languageIdList.Select(languageId => checked((uint)languageId)).ToArray();
                uint subscriptionObjectId = 0;
                var error = await RunWithTimeoutAsync(
                    "CreateAlarmSubscription",
                    () => _session.CreateAlarmSubscription(languageIdsUint, subscriptionOptions.InitialCreditLimit, out subscriptionObjectId),
                    _options.RequestTimeout,
                    cancellationToken).ConfigureAwait(false);
                ThrowIfError("CreateAlarmSubscription", error);

                subscription.Start(token => RunAlarmSubscriptionLoopAsync(subscription, subscriptionOptions, subscriptionObjectId, token));
                return subscription;
            }
            catch (S7CommPlusException ex)
            {
                RaiseCommunicationError(ex);
                if (ex.IsTransient)
                {
                    SetState(S7CommPlusConnectionState.Faulted, ex);
                }
                subscription.MarkFaulted(ex);
                throw;
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public Task<PlcTag> GetTagBySymbolAsync(string symbol, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new ArgumentException("Symbol is required.", nameof(symbol));
            }

            return ExecuteReadOperationAsync("GetTagBySymbol", session =>
            {
                var tag = session.GetPlcTagBySymbol(symbol);
                if (tag == null)
                {
                    throw new S7CommPlusConnectionException("GetTagBySymbol", Endpoint, S7Consts.errCliItemNotAvailable, false, $"PLC tag '{symbol}' could not be resolved.");
                }
                return tag;
            }, cancellationToken);
        }

        public Task<S7CommPlusBatchResult<S7CommPlusReadResult>> ReadAsync(IEnumerable<ItemAddress> addresses, CancellationToken cancellationToken = default)
        {
            if (addresses == null)
            {
                throw new ArgumentNullException(nameof(addresses));
            }

            var addressList = addresses.ToList();
            if (addressList.Count == 0)
            {
                return Task.FromResult(new S7CommPlusBatchResult<S7CommPlusReadResult>(Array.Empty<S7CommPlusReadResult>()));
            }

            return ExecuteReadOperationAsync("Read", session =>
            {
                var error = session.ReadValues(addressList, out var values, out var itemErrors);
                ThrowIfError("Read", error);
                var items = new List<S7CommPlusReadResult>(addressList.Count);
                for (var i = 0; i < addressList.Count; i++)
                {
                    var value = i < values.Count ? values[i] : null;
                    var itemError = i < itemErrors.Count ? itemErrors[i] : ulong.MaxValue;
                    items.Add(new S7CommPlusReadResult(addressList[i], value, itemError));
                }
                return new S7CommPlusBatchResult<S7CommPlusReadResult>(items);
            }, cancellationToken);
        }

        public Task<S7CommPlusBatchResult<S7CommPlusTagReadResult>> ReadAsync(IEnumerable<PlcTag> tags, CancellationToken cancellationToken = default)
        {
            if (tags == null)
            {
                throw new ArgumentNullException(nameof(tags));
            }

            var tagList = tags.ToList();
            if (tagList.Any(tag => tag == null))
            {
                throw new ArgumentException("Tag list cannot contain null entries.", nameof(tags));
            }
            if (tagList.Count == 0)
            {
                return Task.FromResult(new S7CommPlusBatchResult<S7CommPlusTagReadResult>(Array.Empty<S7CommPlusTagReadResult>()));
            }

            return ExecuteReadOperationAsync("ReadTags", session =>
            {
                var addresses = tagList.Select(tag => tag.Address).ToList();
                var error = session.ReadValues(addresses, out var values, out var itemErrors);
                ThrowIfError("ReadTags", error);
                var items = new List<S7CommPlusTagReadResult>(tagList.Count);
                for (var i = 0; i < tagList.Count; i++)
                {
                    var value = i < values.Count ? values[i] : null;
                    var itemError = i < itemErrors.Count ? itemErrors[i] : ulong.MaxValue;
                    tagList[i].ProcessReadResult(value, itemError);
                    items.Add(new S7CommPlusTagReadResult(tagList[i], itemError));
                }
                return new S7CommPlusBatchResult<S7CommPlusTagReadResult>(items);
            }, cancellationToken);
        }

        internal Task<S7CommPlusBatchResult<S7CommPlusWriteResult>> WriteAsync(IEnumerable<ItemAddress> addresses, IEnumerable<PValue> values, CancellationToken cancellationToken = default)
        {
            if (addresses == null)
            {
                throw new ArgumentNullException(nameof(addresses));
            }
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            var addressList = addresses.ToList();
            var valueList = values.ToList();
            if (addressList.Count != valueList.Count)
            {
                throw new ArgumentException("Address and value counts must match.", nameof(values));
            }
            if (addressList.Count == 0)
            {
                return Task.FromResult(new S7CommPlusBatchResult<S7CommPlusWriteResult>(Array.Empty<S7CommPlusWriteResult>()));
            }

            return ExecuteWriteOperationAsync("Write", session =>
            {
                var error = session.WriteValues(addressList, valueList, out var itemErrors);
                ThrowIfError("Write", error);
                var items = new List<S7CommPlusWriteResult>(addressList.Count);
                for (var i = 0; i < addressList.Count; i++)
                {
                    var itemError = i < itemErrors.Count ? itemErrors[i] : ulong.MaxValue;
                    items.Add(new S7CommPlusWriteResult(addressList[i], itemError));
                }
                return new S7CommPlusBatchResult<S7CommPlusWriteResult>(items);
            }, cancellationToken);
        }

        public Task<S7CommPlusBatchResult<S7CommPlusWriteResult>> WriteAsync(IEnumerable<PlcTag> tags, CancellationToken cancellationToken = default)
        {
            if (tags == null)
            {
                throw new ArgumentNullException(nameof(tags));
            }

            var tagList = tags.ToList();
            if (tagList.Any(tag => tag == null))
            {
                throw new ArgumentException("Tag list cannot contain null entries.", nameof(tags));
            }
            if (tagList.Count == 0)
            {
                return Task.FromResult(new S7CommPlusBatchResult<S7CommPlusWriteResult>(Array.Empty<S7CommPlusWriteResult>()));
            }

            return ExecuteWriteOperationAsync("WriteTags", session =>
            {
                var addresses = tagList.Select(tag => tag.Address).ToList();
                var values = tagList.Select(tag => tag.GetWriteValue()).ToList();
                var error = session.WriteValues(addresses, values, out var itemErrors);
                ThrowIfError("WriteTags", error);
                var items = new List<S7CommPlusWriteResult>(tagList.Count);
                for (var i = 0; i < tagList.Count; i++)
                {
                    var itemError = i < itemErrors.Count ? itemErrors[i] : ulong.MaxValue;
                    tagList[i].ProcessWriteResult(itemError);
                    items.Add(new S7CommPlusWriteResult(addresses[i], itemError));
                }
                return new S7CommPlusBatchResult<S7CommPlusWriteResult>(items);
            }, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                await DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _options.Logger.LogWarning(ex, "Error while disposing S7CommPlusClient for {Endpoint}.", Endpoint);
            }
            finally
            {
                _disposed = true;
                _operationGate.Dispose();
            }
        }

        private Task<T> ExecuteReadOperationAsync<T>(string operation, Func<IS7CommPlusSession, T> operationFunc, CancellationToken cancellationToken)
        {
            return ExecuteOperationAsync(operation, allowReconnect: true, operationFunc, cancellationToken);
        }

        private Task<T> ExecuteSessionOperationAsync<T>(string operation, Func<IS7CommPlusSession, T> operationFunc, CancellationToken cancellationToken)
        {
            return ExecuteOperationAsync(operation, allowReconnect: false, operationFunc, cancellationToken);
        }

        private Task<T> ExecuteWriteOperationAsync<T>(string operation, Func<IS7CommPlusSession, T> operationFunc, CancellationToken cancellationToken)
        {
            if (!_options.WriteEnabled)
            {
                throw new S7CommPlusWriteDisabledException(Endpoint);
            }
            return ExecuteOperationAsync(operation, allowReconnect: false, operationFunc, cancellationToken);
        }

        private async Task<T> ExecuteOperationAsync<T>(string operation, bool allowReconnect, Func<IS7CommPlusSession, T> operationFunc, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureConnectedCoreAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    return await RunWithTimeoutAsync(operation, () => operationFunc(_session), _options.RequestTimeout, cancellationToken).ConfigureAwait(false);
                }
                catch (S7CommPlusException ex) when (allowReconnect && _options.AutoReconnect && ex.IsTransient)
                {
                    RaiseCommunicationError(ex);
                    _options.Logger.LogWarning(ex, "Transient {Operation} failure for {Endpoint}; reconnecting and retrying once.", operation, Endpoint);
                    await ReconnectCoreAsync(cancellationToken).ConfigureAwait(false);
                    return await RunWithTimeoutAsync(operation, () => operationFunc(_session), _options.RequestTimeout, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (S7CommPlusException ex)
            {
                RaiseCommunicationError(ex);
                if (ex.IsTransient)
                {
                    SetState(S7CommPlusConnectionState.Faulted, ex);
                }
                throw;
            }
            finally
            {
                _operationGate.Release();
            }
        }

        private async Task EnsureConnectedCoreAsync(CancellationToken cancellationToken)
        {
            if (_session?.IsConnected == true && _state == S7CommPlusConnectionState.Connected)
            {
                return;
            }

            await ConnectCoreAsync(S7CommPlusConnectionState.Connecting, cancellationToken).ConfigureAwait(false);
        }

        private async Task ConnectCoreAsync(S7CommPlusConnectionState connectingState, CancellationToken cancellationToken)
        {
            if (_session?.IsConnected == true && _state == S7CommPlusConnectionState.Connected)
            {
                return;
            }

            SetState(connectingState);
            _session = _sessionFactory();
            _options.Logger.LogInformation("Connecting to PLC {Endpoint}.", Endpoint);

            var error = await RunWithTimeoutAsync("Connect", () => _session.Connect(_options), _options.ConnectTimeout, cancellationToken).ConfigureAwait(false);
            if (error != 0)
            {
                var exception = CreateException("Connect", error);
                _session = null;
                throw exception;
            }

            SetState(S7CommPlusConnectionState.Connected);
            _options.Logger.LogInformation("Connected to PLC {Endpoint}.", Endpoint);
        }

        private async Task ReconnectCoreAsync(CancellationToken cancellationToken)
        {
            SetState(S7CommPlusConnectionState.Reconnecting);
            await DisconnectCoreAsync(cancellationToken).ConfigureAwait(false);
            await ConnectCoreAsync(S7CommPlusConnectionState.Reconnecting, cancellationToken).ConfigureAwait(false);
        }

        private async Task DisconnectCoreAsync(CancellationToken cancellationToken)
        {
            if (_session == null && _state == S7CommPlusConnectionState.Disconnected)
            {
                return;
            }

            SetState(S7CommPlusConnectionState.Disconnecting);
            var session = _session;
            _session = null;
            if (session != null)
            {
                try
                {
                    var error = await RunWithTimeoutAsync("Disconnect", () => session.Disconnect(_options.DisconnectTimeoutMilliseconds), _options.DisconnectTimeout, cancellationToken).ConfigureAwait(false);
                    if (error != 0)
                    {
                        var exception = CreateException("Disconnect", error);
                        RaiseCommunicationError(exception);
                        _options.Logger.LogWarning("PLC disconnect for {Endpoint} returned error {ErrorCode}.", Endpoint, error);
                    }
                }
                catch (S7CommPlusException ex)
                {
                    RaiseCommunicationError(ex);
                    _options.Logger.LogWarning(ex, "PLC disconnect for {Endpoint} failed or timed out.", Endpoint);
                }
            }
            SetState(S7CommPlusConnectionState.Disconnected);
        }

        private async Task RunTagSubscriptionLoopAsync(S7CommPlusTagSubscription subscription, S7CommPlusSubscriptionOptions subscriptionOptions, uint subscriptionObjectId, CancellationToken cancellationToken)
        {
            try
            {
                await RunSubscriptionLoopAsync(
                    "WaitForTagSubscriptionNotifications",
                    subscription,
                    subscriptionOptions,
                    waitFunc: () =>
                    {
                        var error = _session.WaitForTagSubscriptionNotifications(
                            subscriptionObjectId,
                            subscriptionOptions.NotificationTimeoutMilliseconds,
                            subscriptionOptions.CreditLimitStep,
                            out var notifications);
                        return (error, notifications);
                    },
                    publish: notification => subscription.Publish(notification)).ConfigureAwait(false);
            }
            finally
            {
                await TryDeleteSubscriptionAsync("DeleteTagSubscription", subscription, subscriptionOptions, () => _session.DeleteTagSubscription(subscriptionObjectId)).ConfigureAwait(false);
            }
        }

        private async Task RunAlarmSubscriptionLoopAsync(S7CommPlusAlarmSubscription subscription, S7CommPlusSubscriptionOptions subscriptionOptions, uint subscriptionObjectId, CancellationToken cancellationToken)
        {
            try
            {
                await RunSubscriptionLoopAsync(
                    "WaitForAlarmNotifications",
                    subscription,
                    subscriptionOptions,
                    waitFunc: () =>
                    {
                        var error = _session.WaitForAlarmNotifications(
                            subscriptionObjectId,
                            subscriptionOptions.NotificationTimeoutMilliseconds,
                            subscriptionOptions.CreditLimitStep,
                            out var notifications);
                        return (error, notifications);
                    },
                    publish: notification => subscription.Publish(notification)).ConfigureAwait(false);
            }
            finally
            {
                await TryDeleteSubscriptionAsync("DeleteAlarmSubscription", subscription, subscriptionOptions, () => _session.DeleteAlarmSubscription(subscriptionObjectId)).ConfigureAwait(false);
            }
        }

        private async Task RunTisWatchSubscriptionLoopAsync(S7CommPlusTisWatchSubscription subscription, S7CommPlusSubscriptionOptions subscriptionOptions, uint subscriptionObjectId, CancellationToken cancellationToken)
        {
            try
            {
                await RunTisWatchLoopAsync(
                    "WaitForTisWatchNotifications",
                    subscription,
                    subscriptionOptions,
                    waitFunc: () =>
                    {
                        var error = _session.WaitForTisWatchNotifications(
                            subscriptionObjectId,
                            subscriptionOptions.NotificationTimeoutMilliseconds,
                            out var notifications);
                        return (error, notifications);
                    },
                    publish: notification => subscription.Publish(notification)).ConfigureAwait(false);
            }
            finally
            {
                await TryDeleteSubscriptionAsync("DeleteTisWatchSubscription", subscription, subscriptionOptions, () => _session.DeleteTisWatchSubscription(subscriptionObjectId)).ConfigureAwait(false);
            }
        }

        private async Task RunSubscriptionLoopAsync(
            string operation,
            S7CommPlusSubscription subscription,
            S7CommPlusSubscriptionOptions subscriptionOptions,
            Func<(int Error, List<Notification> Notifications)> waitFunc,
            Action<Notification> publish)
        {
            var consecutiveTimeouts = 0;
            while (!subscription.IsStopRequested)
            {
                try
                {
                    var result = await RunWithTimeoutAsync(
                        operation,
                        waitFunc,
                        subscriptionOptions.NotificationTimeout + TimeSpan.FromSeconds(1),
                        CancellationToken.None).ConfigureAwait(false);

                    if (result.Error == S7Consts.errCliJobTimeout || result.Error == S7Consts.errTCPReceiveTimeout)
                    {
                        consecutiveTimeouts++;
                        if (subscriptionOptions.MaxConsecutiveTimeoutsBeforeFault > 0
                            && consecutiveTimeouts >= subscriptionOptions.MaxConsecutiveTimeoutsBeforeFault)
                        {
                            throw CreateException(operation, result.Error);
                        }
                        continue;
                    }

                    ThrowIfError(operation, result.Error);
                    consecutiveTimeouts = 0;

                    foreach (var notification in result.Notifications ?? Enumerable.Empty<Notification>())
                    {
                        publish(notification);
                    }
                }
                catch (S7CommPlusException ex)
                {
                    RaiseCommunicationError(ex);
                    if (ex.IsTransient)
                    {
                        SetState(S7CommPlusConnectionState.Faulted, ex);
                    }
                    subscription.MarkFaulted(ex);
                    throw;
                }
                catch (Exception ex)
                {
                    var wrapped = new S7CommPlusConnectionException(operation, Endpoint, S7Consts.errCliFunctionRefused, true, $"{operation} failed for PLC {Endpoint}.", ex);
                    RaiseCommunicationError(wrapped);
                    SetState(S7CommPlusConnectionState.Faulted, wrapped);
                    subscription.MarkFaulted(wrapped);
                    throw wrapped;
                }
            }
        }

        private async Task RunTisWatchLoopAsync(
            string operation,
            S7CommPlusSubscription subscription,
            S7CommPlusSubscriptionOptions subscriptionOptions,
            Func<(int Error, List<S7CommPlusTisWatchNotification> Notifications)> waitFunc,
            Action<S7CommPlusTisWatchNotification> publish)
        {
            var consecutiveTimeouts = 0;
            while (!subscription.IsStopRequested)
            {
                try
                {
                    var result = await RunWithTimeoutAsync(
                        operation,
                        waitFunc,
                        subscriptionOptions.NotificationTimeout + _options.RequestTimeout + TimeSpan.FromSeconds(1),
                        CancellationToken.None).ConfigureAwait(false);

                    if (result.Error == S7Consts.errCliJobTimeout || result.Error == S7Consts.errTCPReceiveTimeout)
                    {
                        consecutiveTimeouts++;
                        if (subscriptionOptions.MaxConsecutiveTimeoutsBeforeFault > 0
                            && consecutiveTimeouts >= subscriptionOptions.MaxConsecutiveTimeoutsBeforeFault)
                        {
                            throw CreateTisWatchException(operation, result.Error);
                        }
                        continue;
                    }

                    ThrowIfTisWatchError(operation, result.Error);
                    consecutiveTimeouts = 0;

                    foreach (var notification in result.Notifications ?? Enumerable.Empty<S7CommPlusTisWatchNotification>())
                    {
                        publish(notification);
                    }
                }
                catch (S7CommPlusException ex)
                {
                    RaiseCommunicationError(ex);
                    if (ex.IsTransient)
                    {
                        SetState(S7CommPlusConnectionState.Faulted, ex);
                    }
                    subscription.MarkFaulted(ex);
                    throw;
                }
                catch (Exception ex)
                {
                    var wrapped = new S7CommPlusConnectionException(operation, Endpoint, S7Consts.errCliFunctionRefused, true, $"{operation} failed for PLC {Endpoint}.", ex);
                    RaiseCommunicationError(wrapped);
                    SetState(S7CommPlusConnectionState.Faulted, wrapped);
                    subscription.MarkFaulted(wrapped);
                    throw wrapped;
                }
            }
        }

        private void ThrowIfTisWatchError(string operation, int errorCode)
        {
            if (errorCode == 0)
            {
                return;
            }

            throw CreateTisWatchException(operation, errorCode);
        }

        private S7CommPlusException CreateTisWatchException(string operation, int errorCode)
        {
            var diagnostic = _session?.LastTisWatchDiagnostic;
            var effectiveOperation = string.IsNullOrWhiteSpace(diagnostic)
                ? operation
                : $"{operation}: {diagnostic}";
            return CreateException(effectiveOperation, errorCode);
        }

        private async Task TryDeleteSubscriptionAsync(string operation, S7CommPlusSubscription subscription, S7CommPlusSubscriptionOptions subscriptionOptions, Func<int> deleteFunc)
        {
            if (!subscriptionOptions.DeleteOnStop || _session == null)
            {
                return;
            }
            if (subscription.FaultException != null && S7CommPlusErrorClassifier.IsConnectionDefinitelyClosed(subscription.FaultException.ErrorCode))
            {
                _options.Logger.LogDebug(
                    "Skipping {Operation} for {Endpoint} because the subscription already observed connection loss {ErrorCode}.",
                    operation,
                    Endpoint,
                    subscription.FaultException.ErrorCode);
                return;
            }

            try
            {
                var error = await RunWithTimeoutAsync(operation, deleteFunc, _options.DisconnectTimeout, CancellationToken.None).ConfigureAwait(false);
                if (error != 0)
                {
                    var exception = CreateException(operation, error);
                    RaiseCommunicationError(exception);
                    subscription.MarkFaulted(exception);
                    _options.Logger.LogWarning("PLC subscription delete for {Endpoint} returned error {ErrorCode}.", Endpoint, error);
                }
            }
            catch (S7CommPlusException ex)
            {
                RaiseCommunicationError(ex);
                subscription.MarkFaulted(ex);
                _options.Logger.LogWarning(ex, "PLC subscription delete for {Endpoint} failed or timed out.", Endpoint);
            }
        }

        private async Task<T> RunWithTimeoutAsync<T>(string operation, Func<T> func, TimeSpan timeout, CancellationToken cancellationToken)
        {
            try
            {
                return await Task.Run(func).WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                throw new S7CommPlusTimeoutException(operation, Endpoint, S7Consts.errCliJobTimeout, $"{operation} timed out after {timeout}.", ex);
            }
            catch (S7CommPlusException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new S7CommPlusConnectionException(operation, Endpoint, S7Consts.errCliFunctionRefused, true, $"{operation} failed for PLC {Endpoint}.", ex);
            }
        }

        private void ThrowIfError(string operation, int errorCode)
        {
            if (errorCode != 0)
            {
                throw CreateException(operation, errorCode);
            }
        }

        private S7CommPlusException CreateException(string operation, int errorCode)
            => S7CommPlusErrorClassifier.CreateException(operation, Endpoint, errorCode, _session?.LastErrorDetail);

        private void RaiseCommunicationError(S7CommPlusException exception)
        {
            CommunicationError?.Invoke(this, new S7CommPlusCommunicationErrorEventArgs(exception));
        }

        private void SetState(S7CommPlusConnectionState newState, Exception exception = null)
        {
            var oldState = _state;
            if (oldState == newState)
            {
                return;
            }

            _state = newState;
            ConnectionStateChanged?.Invoke(this, new S7CommPlusConnectionStateChangedEventArgs(oldState, newState, exception));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(S7CommPlusClient));
            }
        }

        private static Func<string, long, int, string> CreateTextListResolver(S7CommPlusTextListCatalog textLists)
        {
            return textLists == null ? null : new Func<string, long, int, string>(textLists.ResolveText);
        }

        private string Endpoint => $"{_options.Address}:{_options.Port}";
    }
}
