using S7CommPlusDriver.ClientApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class S7CommPlusClientTests
    {
        [Fact]
        public async Task ConnectBrowseAndReadSucceeds()
        {
            var fake = new FakeLegacyConnection
            {
                BrowseHandler = () => (0, new List<VarInfo> { new VarInfo { Name = "DB.Value" } })
            };
            var client = CreateClient(fake);

            await client.ConnectAsync();
            var vars = await client.BrowseAsync();
            var read = await client.ReadAsync(new[] { new ItemAddress("8A0E0001.F") });

            Assert.Equal(S7CommPlusConnectionState.Connected, client.State);
            Assert.Single(vars);
            Assert.Single(read.Items);
            Assert.True(read.Items[0].IsSuccess);
        }

        [Fact]
        public async Task DisconnectIsIdempotent()
        {
            var fake = new FakeLegacyConnection();
            var client = CreateClient(fake);

            await client.DisconnectAsync();
            await client.ConnectAsync();
            await client.DisconnectAsync();
            await client.DisconnectAsync();

            Assert.Equal(S7CommPlusConnectionState.Disconnected, client.State);
            Assert.Equal(1, fake.DisconnectCount);
        }

        [Fact]
        public async Task RequestTimeoutIsTypedFailure()
        {
            var fake = new FakeLegacyConnection
            {
                BrowseHandler = () =>
                {
                    Task.Delay(200).Wait();
                    return (0, new List<VarInfo>());
                }
            };
            var client = CreateClient(fake, requestTimeoutMs: 20);

            var ex = await Assert.ThrowsAsync<S7CommPlusTimeoutException>(() => client.BrowseAsync());

            Assert.Equal("Browse", ex.Operation);
            Assert.True(ex.IsTransient);
        }

        [Fact]
        public async Task ReadReconnectsOnceAfterTransientDisconnect()
        {
            var fake = new FakeLegacyConnection();
            fake.ReadHandler = _ =>
            {
                if (fake.ReadCount == 1)
                {
                    return (S7Consts.errTCPDataReceive, new List<object?>(), new List<ulong>());
                }
                return (0, new List<object?> { new ValueInt(7) }, new List<ulong> { 0 });
            };
            var client = CreateClient(fake);

            var result = await client.ReadAsync(new[] { new ItemAddress("8A0E0001.F") });

            Assert.Equal(2, fake.ConnectCount);
            Assert.Equal(2, fake.ReadCount);
            Assert.True(result.Items[0].IsSuccess);
        }

        [Fact]
        public async Task ProtocolErrorDoesNotReconnect()
        {
            var fake = new FakeLegacyConnection
            {
                ReadHandler = _ => (S7Consts.errIsoInvalidPDU3, new List<object?>(), new List<ulong>())
            };
            var client = CreateClient(fake);

            var ex = await Assert.ThrowsAsync<S7CommPlusConnectionException>(() => client.ReadAsync(new[] { new ItemAddress("8A0E0001.F") }));

            Assert.Equal(S7Consts.errIsoInvalidPDU3, ex.ErrorCode);
            Assert.False(ex.IsTransient);
            Assert.Equal(1, fake.ConnectCount);
        }

        [Fact]
        public async Task PartialBatchReadReturnsPerItemErrors()
        {
            var fake = new FakeLegacyConnection
            {
                ReadHandler = addresses => (0,
                    new List<object?> { new ValueInt(1), null },
                    new List<ulong> { 0, 0xDEAD })
            };
            var client = CreateClient(fake);

            var result = await client.ReadAsync(new[] { new ItemAddress("8A0E0001.F"), new ItemAddress("8A0E0001.10") });

            Assert.True(result.Items[0].IsSuccess);
            Assert.False(result.Items[1].IsSuccess);
            Assert.Equal((ulong)0xDEAD, result.Items[1].ItemError);
        }

        [Fact]
        public async Task ConcurrentReadsAreSerialized()
        {
            var fake = new FakeLegacyConnection
            {
                ReadHandler = _ =>
                {
                    Task.Delay(50).Wait();
                    return (0, new List<object?> { new ValueInt(1) }, new List<ulong> { 0 });
                }
            };
            var client = CreateClient(fake);
            var addresses = new[] { new ItemAddress("8A0E0001.F") };

            await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.ReadAsync(addresses)));

            Assert.Equal(1, fake.MaxConcurrentReads);
            Assert.Equal(5, fake.ReadCount);
        }

        [Fact]
        public async Task WritesAreBlockedByDefault()
        {
            var fake = new FakeLegacyConnection();
            var client = CreateClient(fake);

            await Assert.ThrowsAsync<S7CommPlusWriteDisabledException>(() =>
                client.WriteAsync(new[] { new ItemAddress("8A0E0001.F") }, new PValue[] { new ValueInt(1) }));
        }

        private static S7CommPlusClient CreateClient(FakeLegacyConnection fake, int requestTimeoutMs = 5000)
        {
            return new S7CommPlusClient(
                new S7CommPlusClientOptions
                {
                    Address = "127.0.0.1",
                    RequestTimeout = TimeSpan.FromMilliseconds(requestTimeoutMs),
                    ConnectTimeout = TimeSpan.FromMilliseconds(500),
                    DisconnectTimeout = TimeSpan.FromMilliseconds(100)
                },
                () => fake);
        }
    }
}
