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

            var securityModeName = Environment.GetEnvironmentVariable("S7COMMPLUS_LIVE_SECURITY_MODE");
            var securityMode = S7CommPlusSecurityMode.Tls;
            if (!string.IsNullOrWhiteSpace(securityModeName))
            {
                Assert.True(Enum.TryParse(securityModeName, ignoreCase: true, out securityMode), $"Invalid S7COMMPLUS_LIVE_SECURITY_MODE value '{securityModeName}'.");
            }

            var tlsBackendName = Environment.GetEnvironmentVariable("S7COMMPLUS_LIVE_TLS_BACKEND");
            var tlsBackend = new S7CommPlusClientOptions().TlsBackend;
            if (!string.IsNullOrWhiteSpace(tlsBackendName))
            {
                Assert.True(Enum.TryParse(tlsBackendName, ignoreCase: true, out tlsBackend), $"Invalid S7COMMPLUS_LIVE_TLS_BACKEND value '{tlsBackendName}'.");
            }

            await using var client = new S7CommPlusClient(new S7CommPlusClientOptions
            {
                Address = host,
                SecurityMode = securityMode,
                TlsBackend = tlsBackend,
                RequestTimeout = ReadOptionalTimeout("S7COMMPLUS_LIVE_REQUEST_TIMEOUT_SECONDS", TimeSpan.FromSeconds(5)),
                ConnectTimeout = ReadOptionalTimeout("S7COMMPLUS_LIVE_CONNECT_TIMEOUT_SECONDS", TimeSpan.FromSeconds(5))
            });
            await client.ConnectAsync();
            var cpuInfo = await client.GetCpuInfoAsync();
            var cultureInfo = await client.GetCpuCultureInfoAsync();
            var vars = await client.BrowseAsync();

            Assert.NotNull(cpuInfo);
            Assert.NotNull(cultureInfo);
            Assert.NotNull(cultureInfo.LanguageIds);
            Assert.NotEmpty(cultureInfo.LanguageIds);
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

        private static TimeSpan ReadOptionalTimeout(string environmentVariable, TimeSpan fallback)
        {
            var value = Environment.GetEnvironmentVariable(environmentVariable);
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            Assert.True(double.TryParse(value, out var seconds) && seconds > 0, $"Invalid {environmentVariable} value '{value}'.");
            return TimeSpan.FromSeconds(seconds);
        }
    }
}
