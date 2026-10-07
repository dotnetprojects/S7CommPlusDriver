# Optional strong-name builds

Normal builds retain the existing unsigned assembly identity. To sign the modern
.NET driver, tests, and example applications with the repository key, build with:

```powershell
dotnet build src/S7CommPlusDriver/S7CommPlusDriver.csproj -p:S7CommPlusStrongName=true
```

The .NET Framework 4.8 target remains unsigned, including when this option is set.
Its published HarpoS7 and zlib dependencies are unsigned; signing the driver would
make the .NET Framework loader reject those dependencies at runtime. Full .NET
Framework strong-name support requires compatible signed versions of the entire
dependency chain first. Modern .NET allows the unsigned dependencies.

Build consuming projects and friend assemblies with the same option. Signed and
unsigned assemblies have different identities, so keep the resulting distributions
separate and do not replace an existing package version with the other variant.
All builds use published NuGet dependencies and require no adjacent repositories.
