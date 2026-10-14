using System;
using System.Reflection;
using System.Threading;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class ResponseDispatchReservationTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AcknowledgedSendWaitsForNotificationDispatcherBeforeSending(bool raw)
        {
            var session = new S7CommPlusProtocolSession();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var dispatchLock = typeof(S7CommPlusProtocolSession).GetField("m_ReceiveDispatchLock", flags)!.GetValue(session)!;
            var method = typeof(S7CommPlusProtocolSession).GetMethod(raw
                ? "SendRawS7plusPduAndWait" : "SendS7plusFunctionObjectAndWait", flags)!;
            using var started = new ManualResetEventSlim();
            using var completed = new ManualResetEventSlim();
            Exception? failure = null;
            Thread worker;
            // Simulate a notification poller that currently owns response dispatch.
            Monitor.Enter(dispatchLock);
            try
            {
                worker = new Thread(() =>
                {
                    started.Set();
                    failure = Record.Exception(() => method.Invoke(session, raw
                        ? new object[] { new byte[] { 0x31 }, 1, ProtocolVersion.V2, 100 }
                        : new object[] { new GetMultiVariablesRequest(ProtocolVersion.V2), 100 }));
                    completed.Set();
                }) { IsBackground = true };
                worker.Start();
                Assert.True(started.Wait(2000));
                Assert.False(completed.Wait(50));
            }
            finally
            {
                Monitor.Exit(dispatchLock);
            }
            Assert.True(completed.Wait(2000));
            Assert.True(worker.Join(2000));
            // There is intentionally no transport: reaching the send after releasing
            // dispatch fails immediately, proving it did not start while the poller owned it.
            Assert.IsType<TargetInvocationException>(failure);
        }
    }
}
