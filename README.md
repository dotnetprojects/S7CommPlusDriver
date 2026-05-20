# S7CommPlusDriver

Production-oriented .NET communication library for Siemens S7-1200/1500 PLCs using S7CommPlus over TLS.

The recommended API for new applications is `S7CommPlusClient`. The older `S7CommPlusConnection` API is still present for existing tools and low-level protocol work, but production code should prefer the new client.

## What The Production Client Provides

- Async connect, disconnect, browse, read, and write methods
- Serialized request execution for safe concurrent callers
- Typed exceptions with PLC endpoint, operation, error code, and transient/non-transient classification
- Connection-state and communication-error events
- Read/browse reconnect retry support
- Explicit write enablement so accidental PLC writes are blocked by default
- Bounded disconnect behavior and better socket disconnect detection
- No library writes to `Console`

## Requirements

### PLC / CPU

The driver supports CPUs and projects that allow secure PG/HMI communication over TLS:

- S7-1200 firmware V4.3 or newer, TLS 1.3 from V4.5
- S7-1500 firmware V2.9 or newer
- Software controllers supported by the upstream protocol implementation

The PLC project must also be configured with a TIA Portal version that supports secure communication, typically TIA Portal V17 or newer.

### OpenSSL

TLS communication uses OpenSSL. The package includes the required native OpenSSL runtime files for supported platforms and copies them to the output directory. If OpenSSL is installed system-wide, make sure the matching native binaries are available on the process path.

Windows runtime files include:

- `libcrypto-3.dll` / `libssl-3.dll` for x86
- `libcrypto-3-x64.dll` / `libssl-3-x64.dll` for x64

## Quick Start

Create one `S7CommPlusClient` per PLC endpoint and reuse it for reads. The client serializes operations internally, so multiple callers can safely share the same instance.

```csharp
await using var client = new S7CommPlusClient(new S7CommPlusClientOptions
{
    Address = "10.0.110.120",
    RequestTimeout = TimeSpan.FromSeconds(5),
    AutoReconnect = true
});

client.ConnectionStateChanged += (_, e) =>
{
    Console.WriteLine($"{e.OldState} -> {e.NewState}");
};

client.CommunicationError += (_, e) =>
{
    Console.WriteLine($"{e.Exception.Operation}: 0x{e.Exception.ErrorCode:X8}");
};

await client.ConnectAsync();

var cpuInfo = await client.GetCpuInfoAsync();
var variables = await client.BrowseAsync();

var tag = await client.GetTagBySymbolAsync("MyDb.MyValue");
var read = await client.ReadAsync(new[] { tag });

if (read.Items[0].IsSuccess)
{
    Console.WriteLine(tag.ToString());
}

await client.DisconnectAsync();
```

## Write Safety

Writes are disabled by default. This is intentional for production services and tests.

```csharp
await using var client = new S7CommPlusClient(new S7CommPlusClientOptions
{
    Address = "10.0.110.120",
    WriteEnabled = true
});

await client.ConnectAsync();
await client.WriteAsync(new[] { tag });
```

If `WriteEnabled` is `false`, write calls throw `S7CommPlusWriteDisabledException`.

## Error Handling

Operations throw `S7CommPlusException` subclasses instead of returning only integer error codes. The exception includes:

- `Operation`
- `Endpoint`
- `ErrorCode`
- `IsTransient`

Read and browse operations retry once after a transient communication failure when `AutoReconnect` is enabled. Writes are not retried automatically.

## Logging And Diagnostics

`S7CommPlusClientOptions.Logger` accepts an `ILogger`. If no logger is provided, `NullLogger` is used.

The library no longer writes diagnostics to `Console`. Lower-level diagnostics are written via `System.Diagnostics.Trace`.

TLS key logging for Wireshark analysis is still available through the low-level `S7Client.WriteSslKeyToFile` and `S7Client.WriteSslKeyPath` settings. Use this only in controlled diagnostic environments.

## Testing

Run the normal build and unit tests:

```powershell
dotnet build src\S7CommPlusDriver.slnx /nodeReuse:false
dotnet test src\S7CommPlusDriver.Tests\S7CommPlusDriver.Tests.csproj --no-restore /nodeReuse:false
```

The live PLC smoke test is opt-in and read-only by default:

```powershell
$env:S7COMMPLUS_LIVE_HOST = "10.0.110.120"
dotnet test src\S7CommPlusDriver.Tests\S7CommPlusDriver.Tests.csproj --filter LivePlcReadOnlySmokeTest
```

By default the live test only connects, reads CPU info, browses, and disconnects. To read explicit tags, provide semicolon-separated tag symbols:

```powershell
$env:S7COMMPLUS_LIVE_TAGS = "MyDb.MyValue;OtherDb.Counter"
```

Never use the live smoke test for writes.

## Tested Communication

Known tested targets include:

- S7 1211 firmware V4.5
- TIA PLCSIM V17 with NetToPLCSim
- TIA PLCSIM V18 with NetToPLCSim
- read-only smoke test against a real PLC at `10.0.110.120`

## Supported Data Types

The `PlcTag` classes convert PLC values into .NET-friendly types. Supported data types include scalar and array variants for common Siemens types such as `Bool`, `Byte`, `Word`, `Int`, `DInt`, `Real`, `LReal`, `String`, `WString`, `Date`, `Date_And_Time`, `DTL`, `Time`, `LTime`, pointer-like types, hardware identifiers, counters, and timers.

For exact mappings, see the implementations in `src/S7CommPlusDriver/ClientApi/PlcTag.cs` and `src/S7CommPlusDriver/ClientApi/PlcTags.cs`.

## License

Unless otherwise noted, all source code is licensed under LGPL-3.0-or-later.

## Authors

- Thomas Wiens - initial work - https://github.com/thomas-v2
- DotNetProjects contributors
