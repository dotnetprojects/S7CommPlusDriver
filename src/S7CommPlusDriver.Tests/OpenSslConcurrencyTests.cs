#if NET8_0_OR_GREATER
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using OpenSsl;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class OpenSslConcurrencyTests
    {
        [Fact]
        public void CallbackDisposalIsDeferredUntilProcessingReturns()
        {
            using var fixture = new ConnectorFixture();
            bool called = false;
            fixture.Callback.OnWrite = () =>
            {
                called = true;
                fixture.Connector.Dispose();
                Assert.False((bool)Field(fixture.Connector, "m_disposed"));
                Assert.Throws<ObjectDisposedException>(() => fixture.Connector.Write(new byte[1], 1));
            };
            fixture.Connector.Write(new byte[1], 1);
            Assert.True(called);
            Assert.True((bool)Field(fixture.Connector, "m_disposed"));
        }

        [Fact]
        public void CallbackReadIsQueuedUntilTheOuterCallbackReturns()
        {
            using var fixture = new ConnectorFixture();
            bool called = false;
            fixture.Callback.OnWrite = () =>
            {
                called = true;
                fixture.Connector.ReadCompleted(new byte[] { 0x16 }, 1);
                var queue = (System.Collections.ICollection)Field(fixture.Connector, "m_pendingReadList");
                Assert.Single(queue);
            };
            fixture.Connector.Write(new byte[1], 1);
            Assert.True(called);
            Assert.Empty((System.Collections.ICollection)Field(fixture.Connector, "m_pendingReadList"));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExportAndDisposeWaitForAnActiveWrite(bool dispose)
        {
            using var fixture = new ConnectorFixture();
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var contenderStarted = new ManualResetEventSlim();
            fixture.Callback.OnWrite = () =>
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            };
            var writer = Task.Run(() => fixture.Connector.Write(new byte[1], 1));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            var contender = Task.Run(() =>
            {
                contenderStarted.Set();
                if (dispose) fixture.Connector.Dispose();
                else fixture.Connector.getOMSExporterSecret();
            });
            try
            {
                Assert.True(contenderStarted.Wait(TimeSpan.FromSeconds(10)));
                Assert.NotSame(contender, await Task.WhenAny(contender, Task.Delay(100)));
            }
            finally
            {
                release.Set();
                await Task.WhenAll(writer, contender);
            }
        }

        [Fact]
        public void NativeEntryPointsRejectUseAfterDisposal()
        {
            using var fixture = new ConnectorFixture();
            var connector = fixture.Connector;
            connector.Dispose();
            Assert.Throws<ObjectDisposedException>(() => connector.Write(new byte[1], 1));
            Assert.Throws<ObjectDisposedException>(() => connector.ReadCompleted(new byte[1], 1));
            Assert.Throws<ObjectDisposedException>(() => connector.ExpectConnect());
            Assert.Throws<ObjectDisposedException>(() => connector.getOMSExporterSecret());
            byte[] buffer = new byte[1];
            Assert.Throws<ObjectDisposedException>(() => connector.Receive(ref buffer, 1));
        }

        private static object Field(object instance, string name) =>
            instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;

        private sealed class ConnectorFixture : IDisposable
        {
            private readonly IntPtr context;
            public readonly Callback Callback = new Callback();
            public readonly OpenSSLConnector Connector;

            public ConnectorFixture()
            {
                if (!OperatingSystem.IsWindows()) Assert.Skip("Uses the bundled Windows OpenSSL runtime.");
                context = Native.ExpectNonNull(Native.SSL_CTX_new(Native.TLS_client_method()));
                Connector = new OpenSSLConnector(context, Callback);
                Connector.ExpectConnect();
            }

            public void Dispose()
            {
                Connector.Dispose();
                Native.SSL_CTX_free(context);
            }
        }

        private sealed class Callback : OpenSSLConnector.IConnectorCallback
        {
            public Action? OnWrite;
            public void WriteData(byte[] data, int length) => OnWrite?.Invoke();
            public void OnDataAvailable() => throw new InvalidOperationException("Handshake test received application data.");
            public void OnSslError(int error, string state) => throw new InvalidOperationException(state);
        }
    }
}
#endif
