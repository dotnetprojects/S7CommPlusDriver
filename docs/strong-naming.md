# Strong-name builds

Normal builds sign the driver, tests, and example applications with the repository
key for every target, including .NET Framework 4.8.

The resolved compile and runtime assemblies of all driver NuGet dependencies,
including transitive dependencies, have been checked for public key tokens across
net48, net6.0, net8.0, and net9.0. In particular, HarpoS7 1.2.0 and
HarpoS7.PublicKeys 1.2.0 use token `1d80a2671b863b8d`, and ZlibCore 2.0.2
(`zlib.net.dll`) uses token `d3655c1a090d0873`. BouncyCastle.Cryptography 2.7.0
and the Microsoft and CommunityToolkit dependencies are also strong-named.
Signed driver builds treat references to unsigned assemblies (CS8002) as errors.

To explicitly produce unsigned assemblies instead, build with:

```powershell
dotnet build src/S7CommPlusDriver/S7CommPlusDriver.csproj -p:S7CommPlusStrongName=false
```

Build consuming projects and friend assemblies with the same option. Signed and
unsigned assemblies have different identities, so consumers of earlier unsigned
packages must rebuild against the signed package. Keep the resulting distributions
separate and do not replace an existing package version with the other variant.
All builds use published NuGet dependencies and require no adjacent repositories.
