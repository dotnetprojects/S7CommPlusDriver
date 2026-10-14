using System;
using System.Collections.Generic;
using System.Threading;

namespace S7CommPlusDriver.Internal
{
    /// <summary>A synchronous receive handoff for the synchronous protocol worker.</summary>
    internal sealed class S7CommPlusReceiveQueue<T>
    {
        private readonly Queue<T> _items = new Queue<T>();
        private bool _completed;

        internal bool TryWrite(T item)
        {
            lock (_items)
            {
                if (_completed) return false;
                _items.Enqueue(item);
                Monitor.Pulse(_items);
                return true;
            }
        }

        internal int Read(int timeout, out T item)
        {
            var deadline = RuntimeCompatibility.TickCount64 + Math.Max(1, timeout);
            lock (_items)
            {
                while (_items.Count == 0 && !_completed)
                {
                    var remaining = deadline - RuntimeCompatibility.TickCount64;
                    if (remaining <= 0)
                    {
                        item = default;
                        return S7Consts.errCliJobTimeout;
                    }
                    Monitor.Wait(_items, (int)Math.Min(int.MaxValue, remaining));
                }
                if (_items.Count != 0)
                {
                    item = _items.Dequeue();
                    return 0;
                }
                item = default;
                return S7Consts.errTCPNotConnected;
            }
        }

        internal void Complete()
        {
            lock (_items)
            {
                _completed = true;
                Monitor.PulseAll(_items);
            }
        }
    }
}
