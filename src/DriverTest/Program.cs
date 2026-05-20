using S7CommPlusDriver;
using S7CommPlusDriver.ClientApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DriverTest
{
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            var hostIp = args.Length >= 1 ? args[0] : "10.0.98.100";
            var password = args.Length >= 2 ? args[1] : string.Empty;
            var username = args.Length >= 3 ? args[2] : string.Empty;

            Console.WriteLine("Main - START");
            Console.WriteLine("Main - connecting to: " + hostIp);

            var options = new S7CommPlusClientOptions
            {
                Address = hostIp,
                Password = password,
                Username = username,
                WriteEnabled = true
            };

            await using var client = new S7CommPlusClient(options);
            try
            {
                await client.ConnectAsync().ConfigureAwait(false);
                Console.WriteLine("Main - connected");

                Console.WriteLine("Main - browsing variables...");
                var vars = await client.BrowseAsync().ConfigureAwait(false);
                Console.WriteLine("Main - browse count=" + vars.Count);

                await ReadBrowsableTagsAsync(client, vars).ConfigureAwait(false);

                // The full typed tag write/read regression test is intentionally disabled by default.
                // It writes PLC memory and therefore requires a matching test project and WriteEnabled=true.
                // var test = new TestPlcTag();
                // var errors = test.DoTests(client, nrandom: 10, testPointers: false);
                // Console.WriteLine("TestPlcTag errors=" + errors);
            }
            catch (S7CommPlusException ex)
            {
                Console.WriteLine($"Main - PLC communication failed: {ex.Message} ({ex.ErrorCode})");
            }
            finally
            {
                await client.DisconnectAsync().ConfigureAwait(false);
            }

            Console.WriteLine("Main - END. Press any key.");
            Console.ReadKey();
        }

        private static async Task ReadBrowsableTagsAsync(S7CommPlusClient client, IReadOnlyList<VarInfo> vars)
        {
            Console.WriteLine("Main - reading variable values");

            var tags = new List<PlcTag>();
            foreach (var variable in vars)
            {
                try
                {
                    tags.Add(await client.GetTagBySymbolAsync(variable.Name).ConfigureAwait(false));
                }
                catch (S7CommPlusException ex)
                {
                    Console.WriteLine($"Skipping {variable.Name}: {ex.Message}");
                }
            }

            if (tags.Count == 0)
            {
                Console.WriteLine("No readable tags found.");
                return;
            }

            await client.ReadAsync(tags).ConfigureAwait(false);
            Console.WriteLine("====================== VARIABLES ======================");

            const string format = "{0,-80}{1,-30}{2,-20}{3,-20}";
            Console.WriteLine(string.Format(format, "SYMBOLIC-NAME", "ACCESS-SEQUENCE", "TYPE", "QC: VALUE"));
            foreach (var tag in tags)
            {
                var datatype = Softdatatype.Types.TryGetValue(tag.Datatype, out var name) ? name : tag.Datatype.ToString();
                Console.WriteLine(string.Format(format, tag.Name, tag.Address.GetAccessString(), datatype, tag.ToString()));
            }
        }
    }
}
