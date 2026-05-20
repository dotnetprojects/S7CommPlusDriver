using System;

namespace S7CommPlusDriver
{
    public class S7CommPlusException : Exception
    {
        public S7CommPlusException(string operation, string endpoint, int errorCode, bool isTransient, string message, Exception innerException = null)
            : base(message, innerException)
        {
            Operation = operation;
            Endpoint = endpoint;
            ErrorCode = errorCode;
            IsTransient = isTransient;
        }

        public string Operation { get; }
        public string Endpoint { get; }
        public int ErrorCode { get; }
        public bool IsTransient { get; }
    }

    public sealed class S7CommPlusConnectionException : S7CommPlusException
    {
        public S7CommPlusConnectionException(string operation, string endpoint, int errorCode, bool isTransient, string message, Exception innerException = null)
            : base(operation, endpoint, errorCode, isTransient, message, innerException)
        {
        }
    }

    public sealed class S7CommPlusTisWatchUnavailableException : S7CommPlusException
    {
        public S7CommPlusTisWatchUnavailableException(string operation, string endpoint, int errorCode, bool isTransient, string message, Exception innerException = null)
            : base(operation, endpoint, errorCode, isTransient, message, innerException)
        {
        }
    }

    public sealed class S7CommPlusTimeoutException : S7CommPlusException
    {
        public S7CommPlusTimeoutException(string operation, string endpoint, int errorCode, string message, Exception innerException = null)
            : base(operation, endpoint, errorCode, true, message, innerException)
        {
        }
    }

    public sealed class S7CommPlusLegitimationException : S7CommPlusException
    {
        public S7CommPlusLegitimationException(string operation, string endpoint, int errorCode, bool isTransient, string message, Exception innerException = null)
            : base(operation, endpoint, errorCode, isTransient, message, innerException)
        {
        }
    }

    public sealed class S7CommPlusWriteDisabledException : S7CommPlusException
    {
        public S7CommPlusWriteDisabledException(string endpoint)
            : base("Write", endpoint, S7Consts.errCliAccessDenied, false, "Writes are disabled. Set S7CommPlusClientOptions.WriteEnabled to true to allow write operations.")
        {
        }
    }

    public sealed class S7CommPlusUnsupportedSecurityModeException : S7CommPlusException
    {
        public S7CommPlusUnsupportedSecurityModeException(S7CommPlusSecurityMode securityMode, string endpoint, string message)
            : base("Connect", endpoint, S7Consts.errCliFunctionNotImplemented, false, message)
        {
            SecurityMode = securityMode;
        }

        public S7CommPlusSecurityMode SecurityMode { get; }
    }
}
