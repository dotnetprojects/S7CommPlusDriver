using System;
using System.Threading;
using S7CommPlusDriver.Internal;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class ReceiveQueueTests
    {
        [Fact]
        public void CompletionDrainsQueuedItemsAndRejectsNewWrites()
        {
            var queue = new S7CommPlusReceiveQueue<int>();
            Assert.True(queue.TryWrite(1)); Assert.True(queue.TryWrite(2));
            queue.Complete(); queue.Complete();
            Assert.False(queue.TryWrite(3));
            Assert.Equal(0, queue.Read(100, out var first)); Assert.Equal(1, first);
            Assert.Equal(0, queue.Read(100, out var second)); Assert.Equal(2, second);
            Assert.Equal(S7Consts.errTCPNotConnected, queue.Read(100, out _));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void WaitingReaderIsWokenByDataOrCompletion(bool complete)
        {
            var queue = new S7CommPlusReceiveQueue<int>();
            using var started = new ManualResetEventSlim();
            using var done = new ManualResetEventSlim();
            var result = -1; var value = -1;
            var reader = new Thread(() =>
            {
                started.Set(); result = queue.Read(5000, out value); done.Set();
            }) { IsBackground = true };
            reader.Start(); Assert.True(started.Wait(2000));
            if (complete) queue.Complete(); else Assert.True(queue.TryWrite(42));
            Assert.True(done.Wait(2000)); Assert.True(reader.Join(2000));
            Assert.Equal(complete ? S7Consts.errTCPNotConnected : 0, result);
            Assert.Equal(complete ? 0 : 42, value);
        }

        [Fact]
        public void EmptyQueueTimesOutWithoutCompletingTheQueue()
        {
            var queue = new S7CommPlusReceiveQueue<int>();
            Assert.Equal(S7Consts.errCliJobTimeout, queue.Read(1, out _));
            Assert.True(queue.TryWrite(42));
            Assert.Equal(0, queue.Read(100, out var value)); Assert.Equal(42, value);
        }

        [Fact]
        public void ConcurrentProducerPreservesEveryItemInOrder()
        {
            var queue = new S7CommPlusReceiveQueue<int>();
            var producer = new Thread(() =>
            {
                for (var i = 0; i < 10000; i++) queue.TryWrite(i);
                queue.Complete();
            }) { IsBackground = true };
            producer.Start();
            for (var i = 0; i < 10000; i++)
            {
                Assert.Equal(0, queue.Read(2000, out var value)); Assert.Equal(i, value);
            }
            Assert.True(producer.Join(2000));
            Assert.Equal(S7Consts.errTCPNotConnected, queue.Read(100, out _));
        }
    }
}
