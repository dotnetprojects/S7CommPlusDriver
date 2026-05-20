namespace S7CommPlusDriver.Internal
{
    internal static class S7CommPlusErrorClassifier
    {
        public static S7CommPlusException CreateException(string operation, string endpoint, int errorCode)
        {
            var message = $"{operation} failed for PLC {endpoint}: {S7Client.ErrorText(errorCode)}.";
            return CreateTypedException(operation, endpoint, errorCode, message);
        }

        public static S7CommPlusException CreateException(string operation, string endpoint, int errorCode, string detail)
        {
            var message = $"{operation} failed for PLC {endpoint}: {S7Client.ErrorText(errorCode)}.";
            if (!string.IsNullOrWhiteSpace(detail))
            {
                message += " " + detail.Trim();
            }

            return CreateTypedException(operation, endpoint, errorCode, message);
        }

        private static S7CommPlusException CreateTypedException(string operation, string endpoint, int errorCode, string message)
        {
            if (operation == "Legitimate")
            {
                return new S7CommPlusLegitimationException(operation, endpoint, errorCode, IsTransient(errorCode), message);
            }

            if (IsTisWatchCreateRejection(operation, errorCode))
            {
                message += " The PLC rejected opening the TIS online block view. Known causes are a block/view that is already watched by TIA or a stale CPU-side watch job; for FBs it can also mean the trigger needs a more specific instance/call path.";
                return new S7CommPlusTisWatchUnavailableException(operation, endpoint, errorCode, IsTransient(errorCode), message);
            }

            return new S7CommPlusConnectionException(operation, endpoint, errorCode, IsTransient(errorCode), message);
        }

        private static bool IsTisWatchCreateRejection(string operation, int errorCode)
        {
            return errorCode == S7Consts.errCliInvalidParams
                && operation != null
                && operation.Contains("CreateTisWatchSubscription")
                && operation.Contains("create TIS watch job");
        }

        public static bool IsTransient(int errorCode)
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
                || errorCode == S7Consts.errOpenSSL
                || errorCode == S7Consts.errS7CommPlusDigestMismatch;
        }

        public static bool IsConnectionDefinitelyClosed(int errorCode)
        {
            return errorCode == S7Consts.errTCPNotConnected
                || errorCode == S7Consts.errTCPConnectionReset
                || IsIsoInvalidPdu(errorCode);
        }

        private static bool IsIsoInvalidPdu(int errorCode)
        {
            return errorCode >= S7Consts.errIsoInvalidPDU
                && errorCode <= S7Consts.errIsoInvalidPDU12;
        }
    }
}
