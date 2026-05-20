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
    public enum S7CommPlusSubscriptionState
    {
        Created,
        Running,
        Stopping,
        Stopped,
        Faulted
    }

    public sealed class S7CommPlusSubscriptionOptions
    {
        public ushort CycleTimeMilliseconds { get; set; } = 1000;
        public TimeSpan NotificationTimeout { get; set; } = TimeSpan.FromSeconds(5);
        public short InitialCreditLimit { get; set; } = S7CommPlusProtocolConstants.DefaultSubscriptionCreditLimit;
        public short CreditLimitStep { get; set; } = S7CommPlusProtocolConstants.DefaultSubscriptionCreditLimitStep;
        public int MaxConsecutiveTimeoutsBeforeFault { get; set; }
        public bool DeleteOnStop { get; set; } = true;

        internal int NotificationTimeoutMilliseconds => checked((int)NotificationTimeout.TotalMilliseconds);

        internal void Validate(bool requireCycleTime)
        {
            if (requireCycleTime && CycleTimeMilliseconds == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(CycleTimeMilliseconds), "Cycle time must be greater than zero.");
            }
            if (NotificationTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(NotificationTimeout), "Notification timeout must be greater than zero.");
            }
            if (InitialCreditLimit == 0 || InitialCreditLimit < -1 || InitialCreditLimit > 255)
            {
                throw new ArgumentOutOfRangeException(nameof(InitialCreditLimit), "Initial credit limit must be -1 or between 1 and 255.");
            }
            if (CreditLimitStep < 0 || CreditLimitStep > 255)
            {
                throw new ArgumentOutOfRangeException(nameof(CreditLimitStep), "Credit limit step must be between 0 and 255.");
            }
            if (MaxConsecutiveTimeoutsBeforeFault < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxConsecutiveTimeoutsBeforeFault), "Timeout fault threshold must be zero or greater.");
            }
        }

        internal S7CommPlusSubscriptionOptions Clone()
        {
            return (S7CommPlusSubscriptionOptions)MemberwiseClone();
        }
    }

    public sealed class S7CommPlusSubscriptionStateChangedEventArgs : EventArgs
    {
        public S7CommPlusSubscriptionStateChangedEventArgs(S7CommPlusSubscriptionState oldState, S7CommPlusSubscriptionState newState, Exception exception)
        {
            OldState = oldState;
            NewState = newState;
            Exception = exception;
        }

        public S7CommPlusSubscriptionState OldState { get; }
        public S7CommPlusSubscriptionState NewState { get; }
        public Exception Exception { get; }
    }

    public sealed class S7CommPlusSubscriptionErrorEventArgs : EventArgs
    {
        public S7CommPlusSubscriptionErrorEventArgs(S7CommPlusException exception)
        {
            Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        }

        public S7CommPlusException Exception { get; }
    }

    public abstract class S7CommPlusSubscription : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stopCts = new CancellationTokenSource();
        private readonly object _stateLock = new object();
        private Task _completion = Task.CompletedTask;
        private int _stopRequested;
        private S7CommPlusSubscriptionState _state = S7CommPlusSubscriptionState.Created;
        private EventHandler<S7CommPlusSubscriptionErrorEventArgs> _communicationError;

        public event EventHandler<S7CommPlusSubscriptionStateChangedEventArgs> StateChanged;
        public event EventHandler<S7CommPlusSubscriptionErrorEventArgs> CommunicationError
        {
            add
            {
                _communicationError += value;
                var fault = FaultException;
                if (fault != null && value != null)
                {
                    _ = Task.Run(() => value(this, new S7CommPlusSubscriptionErrorEventArgs(fault)));
                }
            }
            remove
            {
                _communicationError -= value;
            }
        }

        public S7CommPlusSubscriptionState State
        {
            get
            {
                lock (_stateLock)
                {
                    return _state;
                }
            }
        }

        public Task Completion => _completion;
        public S7CommPlusException FaultException { get; private set; }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _stopRequested, 1) == 0)
            {
                if (State == S7CommPlusSubscriptionState.Created || State == S7CommPlusSubscriptionState.Running)
                {
                    SetState(S7CommPlusSubscriptionState.Stopping);
                }
                _stopCts.Cancel();
            }

            try
            {
                await _completion.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch when (State == S7CommPlusSubscriptionState.Faulted || FaultException != null)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
            _stopCts.Dispose();
        }

        internal bool IsStopRequested => _stopRequested != 0 || _stopCts.IsCancellationRequested;
        internal CancellationToken StopToken => _stopCts.Token;

        internal void Start(Func<CancellationToken, Task> runAsync)
        {
            if (runAsync == null)
            {
                throw new ArgumentNullException(nameof(runAsync));
            }

            SetState(S7CommPlusSubscriptionState.Running);
            _completion = Task.Run(async () =>
            {
                try
                {
                    await runAsync(_stopCts.Token).ConfigureAwait(false);
                    if (State != S7CommPlusSubscriptionState.Faulted)
                    {
                        SetState(S7CommPlusSubscriptionState.Stopped);
                    }
                }
                catch (OperationCanceledException) when (IsStopRequested)
                {
                    SetState(S7CommPlusSubscriptionState.Stopped);
                }
                catch (S7CommPlusException ex)
                {
                    MarkFaulted(ex);
                    throw;
                }
            });
        }

        internal void MarkFaulted(S7CommPlusException exception)
        {
            if (State == S7CommPlusSubscriptionState.Faulted && FaultException != null)
            {
                return;
            }

            FaultException = exception;
            _communicationError?.Invoke(this, new S7CommPlusSubscriptionErrorEventArgs(exception));
            SetState(S7CommPlusSubscriptionState.Faulted, exception);
        }

        private void SetState(S7CommPlusSubscriptionState newState, Exception exception = null)
        {
            S7CommPlusSubscriptionState oldState;
            lock (_stateLock)
            {
                oldState = _state;
                if (oldState == newState)
                {
                    return;
                }
                _state = newState;
            }

            StateChanged?.Invoke(this, new S7CommPlusSubscriptionStateChangedEventArgs(oldState, newState, exception));
        }
    }

    public sealed class S7CommPlusTagNotificationEventArgs : EventArgs
    {
        public S7CommPlusTagNotificationEventArgs(S7CommPlusTagNotification notification)
        {
            Notification = notification ?? throw new ArgumentNullException(nameof(notification));
        }

        public S7CommPlusTagNotification Notification { get; }
    }

    public sealed class S7CommPlusTagNotification
    {
        public S7CommPlusTagNotification(DateTime timestamp, uint sequenceNumber, byte creditTick, IReadOnlyList<S7CommPlusTagNotificationItem> items)
        {
            Timestamp = timestamp;
            SequenceNumber = sequenceNumber;
            CreditTick = creditTick;
            Items = items ?? throw new ArgumentNullException(nameof(items));
        }

        public DateTime Timestamp { get; }
        public uint SequenceNumber { get; }
        public byte CreditTick { get; }
        public IReadOnlyList<S7CommPlusTagNotificationItem> Items { get; }
    }

    public sealed class S7CommPlusTagNotificationItem
    {
        internal S7CommPlusTagNotificationItem(uint itemReferenceId, PlcTag tag, object value, ulong itemError)
        {
            ItemReferenceId = itemReferenceId;
            Tag = tag;
            Value = value;
            ItemError = itemError;
        }

        public uint ItemReferenceId { get; }
        public PlcTag Tag { get; }
        public object Value { get; }
        public ulong ItemError { get; }
        public bool IsSuccess => ItemError == 0;
    }

    public sealed class S7CommPlusTagSubscription : S7CommPlusSubscription
    {
        internal S7CommPlusTagSubscription(IReadOnlyDictionary<uint, PlcTag> tagsByReferenceId)
        {
            TagsByReferenceId = tagsByReferenceId ?? throw new ArgumentNullException(nameof(tagsByReferenceId));
        }

        public event EventHandler<S7CommPlusTagNotificationEventArgs> NotificationReceived;
        public IReadOnlyDictionary<uint, PlcTag> TagsByReferenceId { get; }

        internal void Publish(Notification notification)
        {
            var items = new List<S7CommPlusTagNotificationItem>();
            foreach (var value in notification.Values)
            {
                TagsByReferenceId.TryGetValue(value.Key, out var tag);
                items.Add(new S7CommPlusTagNotificationItem(value.Key, tag, value.Value, 0));
            }
            foreach (var returnValue in notification.ReturnValues)
            {
                TagsByReferenceId.TryGetValue(returnValue.Key, out var tag);
                items.Add(new S7CommPlusTagNotificationItem(returnValue.Key, tag, null, returnValue.Value));
            }

            var tagNotification = new S7CommPlusTagNotification(
                notification.Add1Timestamp,
                notification.NotificationSequenceNumber,
                notification.NotificationCreditTick,
                items.OrderBy(item => item.ItemReferenceId).ToList());
            NotificationReceived?.Invoke(this, new S7CommPlusTagNotificationEventArgs(tagNotification));
        }
    }

    public sealed class S7CommPlusAlarmNotificationEventArgs : EventArgs
    {
        public S7CommPlusAlarmNotificationEventArgs(S7CommPlusAlarmNotification notification)
        {
            Notification = notification ?? throw new ArgumentNullException(nameof(notification));
        }

        public S7CommPlusAlarmNotification Notification { get; }
    }

    public sealed class S7CommPlusAlarmNotification
    {
        public S7CommPlusAlarmNotification(DateTime timestamp, uint sequenceNumber, byte creditTick, byte returnValue, IReadOnlyList<S7CommPlusAlarm> alarms)
        {
            Timestamp = timestamp;
            SequenceNumber = sequenceNumber;
            CreditTick = creditTick;
            ReturnValue = returnValue;
            Alarms = alarms ?? throw new ArgumentNullException(nameof(alarms));
        }

        public DateTime Timestamp { get; }
        public uint SequenceNumber { get; }
        public byte CreditTick { get; }
        public byte ReturnValue { get; }
        public IReadOnlyList<S7CommPlusAlarm> Alarms { get; }
        public bool IsSuccess => (S7CommPlusNotificationReturnCode)ReturnValue == S7CommPlusNotificationReturnCode.AlarmObject;
    }

    public sealed class S7CommPlusAlarmSubscriptionWithSnapshot : IAsyncDisposable
    {
        internal S7CommPlusAlarmSubscriptionWithSnapshot(IReadOnlyList<S7CommPlusAlarm> activeAlarms, S7CommPlusAlarmSubscription subscription)
        {
            ActiveAlarms = activeAlarms ?? Array.Empty<S7CommPlusAlarm>();
            Subscription = subscription ?? throw new ArgumentNullException(nameof(subscription));
        }

        public IReadOnlyList<S7CommPlusAlarm> ActiveAlarms { get; }
        public S7CommPlusAlarmSubscription Subscription { get; }

        public ValueTask DisposeAsync()
        {
            return Subscription.DisposeAsync();
        }
    }

    public sealed class S7CommPlusAlarmSubscription : S7CommPlusSubscription
    {
        private const int MaxReplayNotifications = 256;
        private readonly object _notificationLock = new object();
        private readonly Queue<S7CommPlusAlarmNotification> _notificationReplay = new Queue<S7CommPlusAlarmNotification>();
        private EventHandler<S7CommPlusAlarmNotificationEventArgs> _notificationReceived;

        internal S7CommPlusAlarmSubscription(int alarmTextLanguageId)
        {
            AlarmTextLanguageId = alarmTextLanguageId;
        }

        public event EventHandler<S7CommPlusAlarmNotificationEventArgs> NotificationReceived
        {
            add
            {
                S7CommPlusAlarmNotification[] replay;
                lock (_notificationLock)
                {
                    _notificationReceived += value;
                    replay = _notificationReplay.ToArray();
                }

                if (value != null && replay.Length > 0)
                {
                    _ = Task.Run(() =>
                    {
                        foreach (var notification in replay)
                        {
                            value(this, new S7CommPlusAlarmNotificationEventArgs(notification));
                        }
                    });
                }
            }
            remove
            {
                lock (_notificationLock)
                {
                    _notificationReceived -= value;
                }
            }
        }

        public int AlarmTextLanguageId { get; }
        /// <summary>
        /// True when the subscription requested every alarm text language instead of one LCID.
        /// </summary>
        public bool ReceivesAllAlarmTextLanguages => AlarmTextLanguageId == 0;

        internal void Publish(Notification notification)
        {
            var alarms = new List<S7CommPlusAlarm>();
            if (notification.P2Objects != null)
            {
                foreach (var alarmObject in notification.P2Objects)
                {
                    var alarm = S7CommPlusAlarm.FromNotificationObject(alarmObject, AlarmTextLanguageId);
                    if (alarm != null)
                    {
                        alarms.Add(alarm);
                    }
                }
            }

            var alarmNotification = new S7CommPlusAlarmNotification(
                notification.Add1Timestamp,
                notification.NotificationSequenceNumber,
                notification.NotificationCreditTick,
                notification.P2ReturnValue,
                alarms);
            EventHandler<S7CommPlusAlarmNotificationEventArgs> handler;
            lock (_notificationLock)
            {
                _notificationReplay.Enqueue(alarmNotification);
                while (_notificationReplay.Count > MaxReplayNotifications)
                {
                    _notificationReplay.Dequeue();
                }
                handler = _notificationReceived;
            }

            handler?.Invoke(this, new S7CommPlusAlarmNotificationEventArgs(alarmNotification));
        }
    }
}
