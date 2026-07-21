using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace S7CommPlusDriver
{
    internal static class RuntimeCompatibility
    {
        private static readonly DateTimeOffset UnixEpochValue =
            new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public static long TickCount64
        {
            get
            {
#if NETFRAMEWORK
                return (long)(Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency));
#else
                return Environment.TickCount64;
#endif
            }
        }

        public static DateTimeOffset UnixEpoch => UnixEpochValue;

        public static void FillRandom(Span<byte> destination)
        {
#if NETFRAMEWORK
            var bytes = new byte[destination.Length];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }
            bytes.AsSpan().CopyTo(destination);
#else
            RandomNumberGenerator.Fill(destination);
#endif
        }

        public static string ReplaceOrdinalIgnoreCase(string value, string oldValue, string newValue)
        {
            if (string.IsNullOrEmpty(oldValue))
            {
                throw new ArgumentException("The value to replace cannot be empty.", nameof(oldValue));
            }

#if NETFRAMEWORK
            var startIndex = 0;
            var matchIndex = value.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0)
            {
                return value;
            }

            var builder = new StringBuilder(value.Length);
            while (matchIndex >= 0)
            {
                builder.Append(value, startIndex, matchIndex - startIndex);
                builder.Append(newValue);
                startIndex = matchIndex + oldValue.Length;
                matchIndex = value.IndexOf(oldValue, startIndex, StringComparison.OrdinalIgnoreCase);
            }
            builder.Append(value, startIndex, value.Length - startIndex);
            return builder.ToString();
#else
            return value.Replace(oldValue, newValue, StringComparison.OrdinalIgnoreCase);
#endif
        }

        public static string ToHexString(ReadOnlySpan<byte> value)
        {
#if NETFRAMEWORK
            var bytes = value.ToArray();
            return BitConverter.ToString(bytes).Replace("-", string.Empty);
#else
            return Convert.ToHexString(value);
#endif
        }

        public static byte[] FromHexString(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            if (value.Length % 2 != 0)
            {
                throw new FormatException("Hexadecimal text must contain an even number of characters.");
            }

            var bytes = new byte[value.Length / 2];
            for (var index = 0; index < bytes.Length; index++)
            {
                bytes[index] = Convert.ToByte(value.Substring(index * 2, 2), 16);
            }
            return bytes;
        }

        public static byte[] Sha256(ReadOnlySpan<byte> value)
        {
#if NETFRAMEWORK
            using (var sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(value.ToArray());
            }
#else
            return SHA256.HashData(value);
#endif
        }

        public static void HmacSha256(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, Span<byte> destination)
        {
#if NETFRAMEWORK
            using (var hmac = new HMACSHA256(key.ToArray()))
            {
                hmac.ComputeHash(value.ToArray()).AsSpan().CopyTo(destination);
            }
#else
            HMACSHA256.HashData(key, value, destination);
#endif
        }

        public static string[] SplitAndTrim(string value, char separator)
        {
            var parts = value.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index < parts.Length; index++)
            {
                parts[index] = parts[index].Trim();
            }
            return parts;
        }

        public static async Task WaitAsync(Task task, CancellationToken cancellationToken)
        {
#if NETFRAMEWORK
            if (task.IsCompleted)
            {
                await task.ConfigureAwait(false);
                return;
            }

            var cancellationTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(
                state => ((TaskCompletionSource<bool>)state).TrySetCanceled(),
                cancellationTask))
            {
                var completed = await Task.WhenAny(task, cancellationTask.Task).ConfigureAwait(false);
                await completed.ConfigureAwait(false);
            }
#else
            await task.WaitAsync(cancellationToken).ConfigureAwait(false);
#endif
        }

        public static async Task<T> WaitAsync<T>(Task<T> task, TimeSpan timeout, CancellationToken cancellationToken)
        {
#if NETFRAMEWORK
            using (var timeoutCancellation = new CancellationTokenSource(timeout))
            using (var combinedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellation.Token))
            {
                try
                {
                    await WaitAsync(task, combinedCancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException();
                }
            }

            return await task.ConfigureAwait(false);
#else
            return await task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
#endif
        }

        public static Task<T> WaitAsync<T>(Task<T> task, TimeSpan timeout) =>
            WaitAsync(task, timeout, CancellationToken.None);

        public static IEnumerable<T[]> Chunk<T>(IEnumerable<T> source, int size)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            var chunk = new List<T>(size);
            foreach (var item in source)
            {
                chunk.Add(item);
                if (chunk.Count == size)
                {
                    yield return chunk.ToArray();
                    chunk.Clear();
                }
            }

            if (chunk.Count > 0)
            {
                yield return chunk.ToArray();
            }
        }
    }
}
