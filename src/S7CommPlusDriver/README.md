# DotNetProjects.S7CommPlusDriver

Production-oriented S7CommPlus communication library for Siemens S7-1200/1500 PLCs.

Use `S7CommPlusClient` for new applications. It provides async connect, browse, read, write, alarm, and subscription operations with request serialization, typed exceptions, connection-state events, reconnect support for read operations, and an explicit write-enable safety gate.

TLS is the default security mode, using the managed BouncyCastle TLS backend unless `S7CommPlusClientOptions.TlsBackend` is set explicitly. On `net8.0` and later, older S7-1200/1500 CPUs can use `S7CommPlusSecurityMode.LegacyChallenge` or `Auto` for HarpoS7-derived legacy challenge authentication. `net6.0` remains TLS-only.

Default connection parameters are exposed through `S7CommPlusDefaults`: ISO-on-TCP port `102`, local TSAP `0x0600`, HMI remote TSAP `SIMATIC-ROOT-HMI`, and engineering remote TSAP `SIMATIC-ROOT-ES`. Remote TSAP values are validated as ASCII COTP parameters before connecting.

## Older PLCs / Legacy Challenge Auth

Siemens OMS names the old non-TLS mode `SecurityTypeCSI`. This library exposes it as `S7CommPlusSecurityMode.LegacyChallenge` and keeps TLS as the default, so there is no silent security downgrade.

```csharp
await using var client = new S7CommPlusClient(new S7CommPlusClientOptions
{
    Address = "10.0.98.34",
    SecurityMode = S7CommPlusSecurityMode.LegacyChallenge,
    WriteEnabled = false
});

await client.ConnectAsync();
var cpuInfo = await client.GetCpuInfoAsync();
await client.DisconnectAsync();
```

Legacy support uses the PLC fingerprint to resolve a Siemens public-key family. Known mappings are S7-1500 (`00`), S7-1200 (`01`), and PLCSIM/VPLC (`03`). The implementation uses HarpoS7 for challenge/key/digest primitives while this driver still owns transport, request ordering, timeouts, reconnect behavior, and write protection.

## Subscriptions

Variable and alarm subscriptions are available through disposable production objects:

```csharp
var tag = await client.GetTagBySymbolAsync("MyDb.MyValue");
await using var subscription = await client.SubscribeTagsAsync(new[] { tag });

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
```

An active subscription owns the client request pipeline until it is stopped or disposed, because notification frames use the same PLC session as request/response calls. Alarm subscriptions use `SubscribeAlarmsAsync(languageId)` and expose `NotificationReceived`, `CommunicationError`, `StateChanged`, and `Completion` in the same way. Use `SubscribeAlarmsAsync()` or `GetActiveAlarmsAsync()` without a language id to request every alarm text language returned by the PLC; the legacy `AlarmTexts` property contains the first returned language, and `AlarmTextsByLanguage` contains the full set. To avoid missing alarm transitions while starting up, prefer `SubscribeAlarmsWithSnapshotAsync(languageId)` or `SubscribeAlarmsWithSnapshotAsync()`: it creates the subscription first, automatically opens a temporary second `S7CommPlusClient` with the same options to read the active alarm snapshot, disposes that temporary client, and buffers early notification frames. Use the overload with an explicit snapshot client only when you need to own the second connection yourself.

Communication limits are exposed through `GetCommunicationResourcesAsync()`, including max read/write batch sizes, available PLC subscription slots, and subscription memory.
