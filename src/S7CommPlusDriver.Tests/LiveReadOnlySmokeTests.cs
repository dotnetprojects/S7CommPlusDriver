using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class LiveReadOnlySmokeTests
    {
        [Fact]
        public async Task LivePlcReadOnlySmokeTest()
        {
            var host = Environment.GetEnvironmentVariable("S7COMMPLUS_LIVE_HOST");
            if (string.IsNullOrWhiteSpace(host))
            {
                return;
            }

            await using var client = new S7CommPlusClient(new S7CommPlusClientOptions { Address = host });
            await client.ConnectAsync();
            var cpuInfo = await client.GetCpuInfoAsync();
            var vars = await client.BrowseAsync();

            Assert.NotNull(cpuInfo);
            Assert.NotNull(vars);

            var tagNames = Environment.GetEnvironmentVariable("S7COMMPLUS_LIVE_TAGS");
            if (!string.IsNullOrWhiteSpace(tagNames))
            {
                var requested = tagNames.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var tagTasks = requested.Select(symbol => client.GetTagBySymbolAsync(symbol)).ToArray();
                var tags = await Task.WhenAll(tagTasks);
                var readResult = await client.ReadAsync(tags);
                Assert.NotEmpty(tags);
                Assert.All(readResult.Items, item => Assert.True(item.IsSuccess, $"Tag {item.Tag.Name} read failed with item error {item.ItemError}."));
            }

            await client.DisconnectAsync();
        }
    }
}
