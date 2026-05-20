using Microsoft.Extensions.Logging;
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
        private readonly Func<ILegacyS7CommPlusConnection> _connectionFactory;
        private readonly SemaphoreSlim _operationGate = new SemaphoreSlim(1, 1);
        private ILegacyS7CommPlusConnection _connection;
        private bool _disposed;
        private S7CommPlusConnectionState _state = S7CommPlusConnectionState.Disconnected;

        public S7CommPlusClient(S7CommPlusClientOptions options)
            : this(options, () => new LegacyS7CommPlusConnectionAdapter())
        {
        }

        internal S7CommPlusClient(S7CommPlusClientOptions options, Func<ILegacyS7CommPlusConnection> connectionFactory)
        {
            _options = options?.Clone() ?? throw new ArgumentNullException(nameof(options));
            _options.Validate();
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public event EventHandler<S7CommPlusConnectionStateChangedEventArgs> ConnectionStateChanged;
        public event EventHandler<S7CommPlusCommunicationErrorEventArgs> CommunicationError;

        public S7CommPlusConnectionState State => _state;
        public bool IsConnected => _connection?.IsConnected == true && _state == S7CommPlusConnectionState.Connected;
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
            return ExecuteReadOperationAsync("Browse", connection =>
            {
                var error = connection.Browse(out var vars);
                ThrowIfError("Browse", error);
                return (IReadOnlyList<VarInfo>)(vars ?? new List<VarInfo>());
            }, cancellationToken);
        }

        public Task<S7CommPlusConnection.CpuInfo> GetCpuInfoAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteReadOperationAsync("GetCpuInfo", connection =>
            {
                var error = connection.GetCpuInfos(out var cpuInfo);
                ThrowIfError("GetCpuInfo", error);
                return cpuInfo;
            }, cancellationToken);
        }

        public Task<PlcTag> GetTagBySymbolAsync(string symbol, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new ArgumentException("Symbol is required.", nameof(symbol));
            }

            return ExecuteReadOperationAsync("GetTagBySymbol", connection =>
            {
                var tag = connection.GetPlcTagBySymbol(symbol);
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
            return ExecuteReadOperationAsync("Read", connection =>
            {
                var error = connection.ReadValues(addressList, out var values, out var itemErrors);
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

            return ExecuteReadOperationAsync("ReadTags", connection =>
            {
                var addresses = tagList.Select(tag => tag.Address).ToList();
                var error = connection.ReadValues(addresses, out var values, out var itemErrors);
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

        public Task<S7CommPlusBatchResult<S7CommPlusWriteResult>> WriteAsync(IEnumerable<ItemAddress> addresses, IEnumerable<PValue> values, CancellationToken cancellationToken = default)
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

            return ExecuteWriteOperationAsync("Write", connection =>
            {
                var error = connection.WriteValues(addressList, valueList, out var itemErrors);
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

            return ExecuteWriteOperationAsync("WriteTags", connection =>
            {
                var addresses = tagList.Select(tag => tag.Address).ToList();
                var values = tagList.Select(tag => tag.GetWriteValue()).ToList();
                var error = connection.WriteValues(addresses, values, out var itemErrors);
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

        private Task<T> ExecuteReadOperationAsync<T>(string operation, Func<ILegacyS7CommPlusConnection, T> operationFunc, CancellationToken cancellationToken)
        {
            return ExecuteOperationAsync(operation, allowReconnect: true, operationFunc, cancellationToken);
        }

        private Task<T> ExecuteWriteOperationAsync<T>(string operation, Func<ILegacyS7CommPlusConnection, T> operationFunc, CancellationToken cancellationToken)
        {
            if (!_options.WriteEnabled)
            {
                throw new S7CommPlusWriteDisabledException(Endpoint);
            }
            return ExecuteOperationAsync(operation, allowReconnect: false, operationFunc, cancellationToken);
        }

        private async Task<T> ExecuteOperationAsync<T>(string operation, bool allowReconnect, Func<ILegacyS7CommPlusConnection, T> operationFunc, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureConnectedCoreAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    return await RunWithTimeoutAsync(operation, () => operationFunc(_connection), _options.RequestTimeout, cancellationToken).ConfigureAwait(false);
                }
                catch (S7CommPlusException ex) when (allowReconnect && _options.AutoReconnect && ex.IsTransient)
                {
                    RaiseCommunicationError(ex);
                    _options.Logger.LogWarning(ex, "Transient {Operation} failure for {Endpoint}; reconnecting and retrying once.", operation, Endpoint);
                    await ReconnectCoreAsync(cancellationToken).ConfigureAwait(false);
                    return await RunWithTimeoutAsync(operation, () => operationFunc(_connection), _options.RequestTimeout, cancellationToken).ConfigureAwait(false);
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
            if (_connection?.IsConnected == true && _state == S7CommPlusConnectionState.Connected)
            {
                return;
            }

            await ConnectCoreAsync(S7CommPlusConnectionState.Connecting, cancellationToken).ConfigureAwait(false);
        }

        private async Task ConnectCoreAsync(S7CommPlusConnectionState connectingState, CancellationToken cancellationToken)
        {
            if (_connection?.IsConnected == true && _state == S7CommPlusConnectionState.Connected)
            {
                return;
            }

            SetState(connectingState);
            _connection = _connectionFactory();
            _options.Logger.LogInformation("Connecting to PLC {Endpoint}.", Endpoint);

            var error = await RunWithTimeoutAsync("Connect", () => _connection.Connect(_options), _options.ConnectTimeout, cancellationToken).ConfigureAwait(false);
            if (error != 0)
            {
                _connection = null;
                throw CreateException("Connect", error);
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
            if (_connection == null && _state == S7CommPlusConnectionState.Disconnected)
            {
                return;
            }

            SetState(S7CommPlusConnectionState.Disconnecting);
            var connection = _connection;
            _connection = null;
            if (connection != null)
            {
                try
                {
                    var error = await RunWithTimeoutAsync("Disconnect", () => connection.Disconnect(_options.DisconnectTimeoutMilliseconds), _options.DisconnectTimeout, cancellationToken).ConfigureAwait(false);
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
        {
            var message = $"{operation} failed for PLC {Endpoint}: {S7Client.ErrorText(errorCode)}.";
            return new S7CommPlusConnectionException(operation, Endpoint, errorCode, IsTransient(errorCode), message);
        }

        private static bool IsTransient(int errorCode)
        {
            return errorCode == S7Consts.errTCPConnectionTimeout
                || errorCode == S7Consts.errTCPConnectionFailed
                || errorCode == S7Consts.errTCPReceiveTimeout
                || errorCode == S7Consts.errTCPDataReceive
                || errorCode == S7Consts.errTCPSendTimeout
                || errorCode == S7Consts.errTCPDataSend
                || errorCode == S7Consts.errTCPConnectionReset
                || errorCode == S7Consts.errTCPNotConnected
                || errorCode == S7Consts.errTCPUnreachableHost
                || errorCode == S7Consts.errCliJobTimeout
                || errorCode == S7Consts.errOpenSSL;
        }

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

        private string Endpoint => $"{_options.Address}:{_options.Port}";
    }
}
