using System;

namespace S7CommPlusDriver
{
    public enum S7CommPlusConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting,
        Disconnecting,
        Faulted
    }

    public sealed class S7CommPlusConnectionStateChangedEventArgs : EventArgs
    {
        public S7CommPlusConnectionStateChangedEventArgs(S7CommPlusConnectionState oldState, S7CommPlusConnectionState newState, Exception exception = null)
        {
            OldState = oldState;
            NewState = newState;
            Exception = exception;
        }

        public S7CommPlusConnectionState OldState { get; }
        public S7CommPlusConnectionState NewState { get; }
        public Exception Exception { get; }
    }

    public sealed class S7CommPlusCommunicationErrorEventArgs : EventArgs
    {
        public S7CommPlusCommunicationErrorEventArgs(S7CommPlusException exception)
        {
            Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        }

        public S7CommPlusException Exception { get; }
    }
}
