using System;
using System.Collections.Generic;
using System.Globalization;
using System.Buffers.Binary;

namespace S7CommPlusDriver
{
    public sealed class S7CommPlusTisWatchRequest
    {
        public byte[] RequestBlob { get; set; } = Array.Empty<byte>();
        public byte[] TriggerBlob { get; set; } = Array.Empty<byte>();
        public string JobName { get; set; } = "S7pDriver_TisWatchJob";
        public S7CommPlusTisResultModel ResultModel { get; set; } = new S7CommPlusTisResultModel();
        public string LastLifecycleStage { get; internal set; } = "";

        internal S7CommPlusTisWatchRequest Clone()
        {
            return new S7CommPlusTisWatchRequest
            {
                RequestBlob = (byte[])(RequestBlob ?? Array.Empty<byte>()).Clone(),
                TriggerBlob = (byte[])(TriggerBlob ?? Array.Empty<byte>()).Clone(),
                JobName = String.IsNullOrWhiteSpace(JobName) ? "S7pDriver_TisWatchJob" : JobName,
                ResultModel = ResultModel?.Clone() ?? new S7CommPlusTisResultModel(),
                LastLifecycleStage = LastLifecycleStage ?? ""
            };
        }

        internal void Validate()
        {
            if (RequestBlob == null || RequestBlob.Length == 0)
            {
                throw new ArgumentException("A TIS watch request blob (2693) is required.", nameof(RequestBlob));
            }
            if (TriggerBlob == null || TriggerBlob.Length == 0)
            {
                throw new ArgumentException("A TIS watch trigger blob (2694) is required.", nameof(TriggerBlob));
            }
        }
    }

    public sealed class S7CommPlusTisWatchPointSpec
    {
        public int Sac { get; set; }
        public string NetworkId { get; set; } = "";
        public string Uid { get; set; } = "";
        public string Pin { get; set; } = "";
        public bool IncludeRlo { get; set; } = true;
        public List<S7CommPlusTisWatchValueSpec> Values { get; } = new List<S7CommPlusTisWatchValueSpec>();
    }

    public sealed class S7CommPlusTisWatchValueSpec
    {
        public string NetworkId { get; set; } = "";
        public string Uid { get; set; } = "";
        public string Pin { get; set; } = "";
        public string DataType { get; set; } = "";
        public byte[] DataAddressBlob { get; set; } = Array.Empty<byte>();
        public int ByteLength { get; set; } = 1;
        public int Alignment { get; set; } = 1;
        public bool NeedsValidCount { get; set; }
        public bool InvertBool { get; set; }
        public int Sac { get; set; } = -1;
    }

    public sealed class S7CommPlusTisResultModel
    {
        public List<S7CommPlusTisWatchPointModel> WatchPoints { get; } = new List<S7CommPlusTisWatchPointModel>();

        internal S7CommPlusTisResultModel Clone()
        {
            var clone = new S7CommPlusTisResultModel();
            foreach (var watchPoint in WatchPoints)
            {
                clone.WatchPoints.Add(watchPoint.Clone());
            }
            return clone;
        }
    }

    public sealed class S7CommPlusTisWatchPointModel
    {
        public string NetworkId { get; set; } = "";
        public string Uid { get; set; } = "";
        public string Pin { get; set; } = "";
        public int RloOffset { get; set; } = -1;
        public List<S7CommPlusTisValueModel> Values { get; } = new List<S7CommPlusTisValueModel>();

        internal S7CommPlusTisWatchPointModel Clone()
        {
            var clone = new S7CommPlusTisWatchPointModel
            {
                NetworkId = NetworkId ?? "",
                Uid = Uid ?? "",
                Pin = Pin ?? "",
                RloOffset = RloOffset
            };
            foreach (var value in Values)
            {
                clone.Values.Add(value.Clone());
            }
            return clone;
        }
    }

    public sealed class S7CommPlusTisValueModel
    {
        public string NetworkId { get; set; } = "";
        public string Uid { get; set; } = "";
        public string Pin { get; set; } = "";
        public int ValueOffset { get; set; } = -1;
        public int ByteLength { get; set; } = 1;
        public string DataType { get; set; } = "";
        public int ValidityOffset { get; set; } = -1;
        public int ValidCountOffset { get; set; } = -1;
        public bool InvertBool { get; set; }

        internal S7CommPlusTisValueModel Clone()
        {
            return new S7CommPlusTisValueModel
            {
                NetworkId = NetworkId ?? "",
                Uid = Uid ?? "",
                Pin = Pin ?? "",
                ValueOffset = ValueOffset,
                ByteLength = ByteLength,
                DataType = DataType ?? "",
                ValidityOffset = ValidityOffset,
                ValidCountOffset = ValidCountOffset,
                InvertBool = InvertBool
            };
        }
    }

    public sealed class S7CommPlusTisWatchNotificationEventArgs : EventArgs
    {
        public S7CommPlusTisWatchNotificationEventArgs(S7CommPlusTisWatchNotification notification)
        {
            Notification = notification ?? throw new ArgumentNullException(nameof(notification));
        }

        public S7CommPlusTisWatchNotification Notification { get; }
    }

    public sealed class S7CommPlusTisWatchNotification
    {
        public S7CommPlusTisWatchNotification(DateTime timestamp, uint sequenceNumber, byte creditTick, bool? jobEnabled, byte? notificationCredit, byte[] rawResult, IReadOnlyList<S7CommPlusTisWatchPointResult> watchPoints)
        {
            Timestamp = timestamp;
            SequenceNumber = sequenceNumber;
            CreditTick = creditTick;
            JobEnabled = jobEnabled;
            NotificationCredit = notificationCredit;
            RawResult = rawResult ?? Array.Empty<byte>();
            WatchPoints = watchPoints ?? Array.Empty<S7CommPlusTisWatchPointResult>();
        }

        public DateTime Timestamp { get; }
        public uint SequenceNumber { get; }
        public byte CreditTick { get; }
        public bool? JobEnabled { get; }
        public byte? NotificationCredit { get; }
        public byte[] RawResult { get; }
        public IReadOnlyList<S7CommPlusTisWatchPointResult> WatchPoints { get; }
    }

    public sealed class S7CommPlusTisWatchPointResult
    {
        public S7CommPlusTisWatchPointResult(string networkId, string uid, string pin, bool? rlo, uint rawRloWord, uint executionCount, IReadOnlyList<S7CommPlusTisValueResult> values)
        {
            NetworkId = networkId ?? "";
            Uid = uid ?? "";
            Pin = pin ?? "";
            Rlo = rlo;
            RawRloWord = rawRloWord;
            ExecutionCount = executionCount;
            Values = values ?? Array.Empty<S7CommPlusTisValueResult>();
        }

        public string NetworkId { get; }
        public string Uid { get; }
        public string Pin { get; }
        public bool? Rlo { get; }
        public uint RawRloWord { get; }
        public uint ExecutionCount { get; }
        public IReadOnlyList<S7CommPlusTisValueResult> Values { get; }
    }

    public sealed class S7CommPlusTisValueResult
    {
        public S7CommPlusTisValueResult(string networkId, string uid, string pin, string dataType, byte[] rawValue, bool? boolValue, byte? validity, ushort? validCount)
        {
            NetworkId = networkId ?? "";
            Uid = uid ?? "";
            Pin = pin ?? "";
            DataType = dataType ?? "";
            RawValue = rawValue ?? Array.Empty<byte>();
            BoolValue = boolValue;
            Validity = validity;
            ValidCount = validCount;
        }

        public string NetworkId { get; }
        public string Uid { get; }
        public string Pin { get; }
        public string DataType { get; }
        public byte[] RawValue { get; }
        public bool? BoolValue { get; }
        public byte? Validity { get; }
        public ushort? ValidCount { get; }
        public string DisplayValue => FormatDisplayValue();

        private string FormatDisplayValue()
        {
            if (BoolValue.HasValue)
                return BoolValue.Value ? "TRUE" : "FALSE";

            var type = ExtractTypeName(DataType);
            if (RawValue.Length == 0)
                return "";

            if (type.Equals("LDT", StringComparison.OrdinalIgnoreCase) && RawValue.Length >= 8)
                return FormatLdt(ReadUInt64BigEndian(RawValue.AsSpan(0, 8)));

            if (type.Equals("Int", StringComparison.OrdinalIgnoreCase) && RawValue.Length >= 2)
                return BinaryPrimitives.ReadInt16BigEndian(RawValue.AsSpan(0, 2)).ToString(CultureInfo.InvariantCulture);
            if (type.Equals("UInt", StringComparison.OrdinalIgnoreCase) && RawValue.Length >= 2)
                return BinaryPrimitives.ReadUInt16BigEndian(RawValue.AsSpan(0, 2)).ToString(CultureInfo.InvariantCulture);
            if (type.Equals("DInt", StringComparison.OrdinalIgnoreCase) && RawValue.Length >= 4)
                return BinaryPrimitives.ReadInt32BigEndian(RawValue.AsSpan(0, 4)).ToString(CultureInfo.InvariantCulture);
            if (type.Equals("UDInt", StringComparison.OrdinalIgnoreCase) && RawValue.Length >= 4)
                return BinaryPrimitives.ReadUInt32BigEndian(RawValue.AsSpan(0, 4)).ToString(CultureInfo.InvariantCulture);
            if (type.Equals("LInt", StringComparison.OrdinalIgnoreCase) && RawValue.Length >= 8)
                return BinaryPrimitives.ReadInt64BigEndian(RawValue.AsSpan(0, 8)).ToString(CultureInfo.InvariantCulture);
            if (type.Equals("ULInt", StringComparison.OrdinalIgnoreCase) && RawValue.Length >= 8)
                return BinaryPrimitives.ReadUInt64BigEndian(RawValue.AsSpan(0, 8)).ToString(CultureInfo.InvariantCulture);
            if ((type.Equals("Byte", StringComparison.OrdinalIgnoreCase) || type.Equals("USInt", StringComparison.OrdinalIgnoreCase)) && RawValue.Length >= 1)
                return RawValue[0].ToString(CultureInfo.InvariantCulture);
            if (type.Equals("SInt", StringComparison.OrdinalIgnoreCase) && RawValue.Length >= 1)
                return ((sbyte)RawValue[0]).ToString(CultureInfo.InvariantCulture);

            return BitConverter.ToString(RawValue);
        }

        private static string ExtractTypeName(string type)
        {
            if (String.IsNullOrWhiteSpace(type))
                return "";

            var decoded = type.Replace("&quot;", "\"", StringComparison.OrdinalIgnoreCase);
            var end = decoded.LastIndexOf('"');
            if (end >= 0 && end + 1 < decoded.Length)
            {
                var trailing = decoded.Substring(end + 1).Trim(' ', '}');
                if (!String.IsNullOrWhiteSpace(trailing))
                    return trailing;
            }

            if (end > 0)
            {
                var start = decoded.LastIndexOf('"', end - 1);
                if (start >= 0 && end > start)
                    return decoded.Substring(start + 1, end - start - 1);
            }

            var trimmed = decoded.Trim('{', '}', ' ');
            var split = trimmed.Split(new[] { ' ', '.', ':' }, StringSplitOptions.RemoveEmptyEntries);
            return split.Length == 0 ? trimmed : split[split.Length - 1];
        }

        private static ulong ReadUInt64BigEndian(ReadOnlySpan<byte> value) =>
            BinaryPrimitives.ReadUInt64BigEndian(value);

        private static string FormatLdt(ulong nanosecondsSinceUnixEpoch)
        {
            try
            {
                var ticks = checked((long)(nanosecondsSinceUnixEpoch / 100UL));
                var date = DateTimeOffset.UnixEpoch.AddTicks(ticks);
                var remainder = nanosecondsSinceUnixEpoch % 1_000_000_000UL;
                return remainder == 0
                    ? date.ToString("LDT#yyyy-MM-dd-HH:mm:ss", CultureInfo.InvariantCulture)
                    : date.ToString("LDT#yyyy-MM-dd-HH:mm:ss", CultureInfo.InvariantCulture) + "." + remainder.ToString("D9", CultureInfo.InvariantCulture).TrimEnd('0');
            }
            catch
            {
                return nanosecondsSinceUnixEpoch.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    public sealed class S7CommPlusTisWatchSubscription : S7CommPlusSubscription
    {
        internal S7CommPlusTisWatchSubscription(S7CommPlusTisResultModel resultModel)
        {
            ResultModel = resultModel ?? new S7CommPlusTisResultModel();
        }

        public S7CommPlusTisResultModel ResultModel { get; }
        public event EventHandler<S7CommPlusTisWatchNotificationEventArgs> NotificationReceived;

        internal void Publish(S7CommPlusTisWatchNotification notification)
        {
            if (notification != null)
            {
                NotificationReceived?.Invoke(this, new S7CommPlusTisWatchNotificationEventArgs(notification));
            }
        }
    }
}
