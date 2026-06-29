# S7CommPlusDriver

Production-oriented .NET communication library for Siemens S7-1200/1500 PLCs using S7CommPlus over TLS, with optional legacy challenge authentication for pre-V17/pre-TLS CPUs on `net8.0` and later.

The public API for new applications is `S7CommPlusClient`. Low-level protocol/session types are internal implementation details; production code should use the client surface.

## What The Production Client Provides

- Async connect, disconnect, browse, read, write, active-alarm, subscription, and legitimation methods
- Serialized request execution for safe concurrent callers
- Typed exceptions with PLC endpoint, operation, error code, and transient/non-transient classification
- Connection-state and communication-error events
- Read/browse reconnect retry support
- Explicit write enablement so accidental PLC writes are blocked by default
- Bounded disconnect behavior and better socket disconnect detection
- TLS and legacy S7CommPlus challenge authentication modes
- No library writes to `Console`

## Requirements

### PLC / CPU

The default mode supports CPUs and projects that allow secure PG/HMI communication over TLS:

- S7-1200 firmware V4.3 or newer, TLS 1.3 from V4.5
- S7-1500 firmware V2.9 or newer
- Software controllers supported by the upstream protocol implementation

The PLC project must also be configured with a TIA Portal version that supports secure communication, typically TIA Portal V17 or newer.

For older S7-1200/1500 CPUs that do not support TLS, use `S7CommPlusSecurityMode.LegacyChallenge` or `Auto` on `net8.0`/`net9.0`. Legacy mode uses HarpoS7-derived challenge authentication and packet digests. `net6.0` builds remain TLS-only and fail fast if legacy mode is requested.

### TLS Backend

TLS communication uses the managed BouncyCastle backend by default. The older OpenSSL backend remains available through `S7CommPlusClientOptions.TlsBackend = S7CommPlusTlsBackend.OpenSsl`, but it depends on native runtime files and may be less portable across PLC firmware/OpenSSL combinations.

The package includes the required native OpenSSL runtime files for supported platforms and copies them to the output directory when the OpenSSL backend is selected. If OpenSSL is installed system-wide, make sure the matching native binaries are available on the process path.

Windows runtime files include:

- `libcrypto-3.dll` / `libssl-3.dll` for x86
- `libcrypto-3-x64.dll` / `libssl-3-x64.dll` for x64
- `libcrypto-3-arm64.dll` / `libssl-3-arm64.dll` plus `vcruntime140.dll` for ARM64

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
var cpuCulture = await client.GetCpuCultureInfoAsync();
var variables = await client.BrowseAsync();

var tag = await client.GetTagBySymbolAsync("MyDb.MyValue");
var read = await client.ReadAsync(new[] { tag });

if (read.Items[0].IsSuccess)
{
    Console.WriteLine(tag.ToString());
}

await client.DisconnectAsync();
```

`GetCpuCultureInfoAsync()` reads the CPU text-container LCIDs and exposes both
the raw language IDs and resolved .NET cultures:

```csharp
foreach (var culture in cpuCulture.Cultures)
{
    Console.WriteLine($"{culture.LCID}: {culture.Name}");
}
```

The built-in connection defaults match Siemens S7CommPlus HMI communication:
ISO-on-TCP port `S7CommPlusDefaults.IsoTcpPort` (`102`), local TSAP
`S7CommPlusDefaults.LocalTsap` (`0x0600`), and remote TSAP
`S7CommPlusDefaults.RemoteTsapHmi` (`SIMATIC-ROOT-HMI`). Project/engineering
captures sometimes use `S7CommPlusDefaults.RemoteTsapEs`
(`SIMATIC-ROOT-ES`). Remote TSAP values are validated as ASCII COTP
parameters before a socket is opened.

## Password Legitimation

Initial session authentication and PLC password legitimation are separate
steps. If the PLC requires a password for the desired access level, either pass
credentials in the options so they are used during connect, or call
`LegitimateAsync` after connecting:

```csharp
await using var client = new S7CommPlusClient(new S7CommPlusClientOptions
{
    Address = "10.0.110.120",
    Password = "plc-password"
});

await client.ConnectAsync();

// Or, for an already connected session:
await client.LegitimateAsync("plc-password", username: "");
```

Legitimation is a session-security operation, not a PLC signal write, so it
does not require `WriteEnabled = true`. Failed legitimation attempts throw
`S7CommPlusLegitimationException`.

## Active Alarms

Active alarms are exposed through the same serialized read pipeline and can
therefore use the normal timeout, reconnect, logging, and typed-error behavior:

```csharp
var alarms = await client.GetActiveAlarmsAsync(languageId: 1033);
```

Use this call to get alarms that are already active when your application
connects. Alarm subscriptions report new notification frames on that session;
they should not be used as the only source for pre-existing alarms.

## PLC Text Lists and Alarm Text Formatting

PLC alarm texts can contain TIA-style text-list placeholders such as
`@2%t#519K@`. Load the PLC text-list catalog once and pass it to the alarm APIs
when you want those placeholders expanded with the same text lists that are
stored in the CPU:

```csharp
var textLists = await client.GetTextListsAsync();
var alarms = await client.GetActiveAlarmsAsync(languageId: 1031, textLists);

foreach (var alarm in alarms)
{
    Console.WriteLine(alarm.AlarmTexts?.AlarmText);
}
```

`GetTextListsAsync()` discovers the CPU languages from the PLC text container
and loads all available localized text-list libraries, plus the
language-independent system text-list library. To load only selected languages,
pass LCIDs explicitly:

```csharp
var textLists = await client.GetTextListsAsync(new[] { 1031, 2057 });
```

The catalog remains a single API surface, but each `S7CommPlusTextList` exposes
`TextListType` so callers can distinguish runtime user lists from system lists:

```csharp
var userLists = textLists.TextLists
    .Where(list => list.TextListType == S7CommPlusTextListType.User);

var systemLists = textLists.TextLists
    .Where(list => list.TextListType == S7CommPlusTextListType.System);
```

The online PLC payload does not expose the full engineering project object
kind, so the driver classifies text lists by Siemens runtime list-id
conventions. The
catalog resolver still uses all lists, because system diagnostic texts and user
alarm texts can reference each other recursively.

Associated-value placeholders are formatted as well. For example, `@2W%d@`
uses the second associated value as a `WORD` and formats it as signed decimal,
matching TIA's standard element-type syntax.

## Communication Limits

PLC communication limits are available through the production client. This is
useful before creating subscriptions or planning large batch reads:

```csharp
var resources = await client.GetCommunicationResourcesAsync();

Console.WriteLine(resources.TagsPerReadRequestMax);
Console.WriteLine(resources.PlcSubscriptionsFree);
```

## Subscriptions

Subscriptions are exposed as long-running, disposable objects. They own the
client operation pipeline while active because notification frames arrive on the
same PLC session as normal request/response traffic. Stop or dispose a
subscription before issuing other reads or writes on the same client.

```csharp
var tag = await client.GetTagBySymbolAsync("MyDb.MyValue");

await using var subscription = await client.SubscribeTagsAsync(
    new[] { tag },
    new S7CommPlusSubscriptionOptions
    {
        CycleTimeMilliseconds = 250,
        NotificationTimeout = TimeSpan.FromSeconds(5)
    });

subscription.NotificationReceived += (_, e) =>
{
    foreach (var item in e.Notification.Items)
    {
        if (item.IsSuccess)
        {
            Console.WriteLine(item.Tag);
        }
    }
};

subscription.CommunicationError += (_, e) =>
{
    Console.WriteLine($"{e.Exception.Operation}: 0x{e.Exception.ErrorCode:X8}");
};
```

Alarm notifications use the same lifecycle and credit handling:

```csharp
await using var snapshotClient = new S7CommPlusClient(options);
var textLists = await client.GetTextListsAsync(new[] { 1033 });
await using var alarmSession = await client.SubscribeAlarmsWithSnapshotAsync(snapshotClient, 1033, textLists);

foreach (var activeAlarm in alarmSession.ActiveAlarms)
{
    Console.WriteLine(activeAlarm.AlarmTexts?.AlarmText);
}

alarmSession.Subscription.NotificationReceived += (_, e) =>
{
    foreach (var alarm in e.Notification.Alarms)
    {
        Console.WriteLine(alarm.ToString());
    }
};
```

Subscriptions can share one `S7CommPlusClient` with reads, metadata calls, and
other subscriptions. The client keeps one foreground request in flight per
physical PLC connection and routes notifications by PLC subscription object id.
Use a second client only when you explicitly want a second physical PLC
connection, for example for true parallel large metadata transfers.

Alarm snapshots require explicit connection ownership: create another
`S7CommPlusClient` and pass it to `SubscribeAlarmsWithSnapshotAsync` when you
want a live alarm subscription plus an initial active-alarm snapshot on a
separate physical connection. Stopping an alarm subscription deletes only that
subscription and keeps the client session available for later requests.

Idle notification waits are not treated as failures by default. Set
`MaxConsecutiveTimeoutsBeforeFault` when a quiet subscription should become a
typed communication failure after a fixed number of empty waits. Write
protection is unchanged: subscriptions do not enable PLC signal writes.

## Older PLCs / Legacy Challenge Auth

TLS remains the default to avoid accidental security downgrades. Enable the older Siemens challenge authentication explicitly:

```csharp
await using var client = new S7CommPlusClient(new S7CommPlusClientOptions
{
    Address = "10.0.110.120",
    SecurityMode = S7CommPlusSecurityMode.LegacyChallenge,
    RequestTimeout = TimeSpan.FromSeconds(5)
});

await client.ConnectAsync();
Console.WriteLine(client.Options.NegotiatedSecurityMode);
```

`S7CommPlusSecurityMode.Auto` tries TLS first and then falls back to legacy challenge authentication. Reconnect and write safety behave the same in all modes: read/browse may reconnect once, writes are never retried automatically, and writes still require `WriteEnabled = true`.

The HarpoS7 `1.1.0` NuGet package currently declares unpublished dependency package IDs, so this repository references the required HarpoS7 source projects directly under `src/HarpoS7`. HarpoS7 source is MIT licensed; this project remains LGPL-3.0-or-later unless noted otherwise.

Legacy packet-capture notes, auth frame variants, and live PLC observations are tracked in `docs/legacy-s7commplus-memory.md`.

Recent legacy auth work parses `ServerSessionVersion` from the CreateObject
response where available, so S7-1500 V3.x authentication-frame selection is
response-driven before falling back to capture-compatible heuristics.

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

To exercise legacy auth or auto fallback in the live test:

```powershell
$env:S7COMMPLUS_LIVE_SECURITY_MODE = "LegacyChallenge" # or "Auto"
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
