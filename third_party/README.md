# Third-party packages

Vendored so a clone builds without network access (ARCHITECTURE.md §5). `NuGet.config` at the repository root maps
these package IDs to `third_party/packages`, and only there; nothing else in the build uses third-party code.

| Package | Version | Licence | Used by | Notes |
|---|---|---|---|---|
| LiteNetLib | 2.1.4 | MIT | `Hollow.Net` | UDP transport: reliable and sequenced channels, fragmentation, MTU discovery. |
| JoltPhysicsSharp | 2.23.0 | MIT | `Hollow.Simulation` | C# binding of Jolt Physics (through joltc). |
| JoltPhysics.Native | 1.1.0 | MIT | (dependency of JoltPhysicsSharp) | joltc native libraries. **Trimmed** to `win-x64/joltc.dll` and `linux-x64/libjoltc.so`. |
| Microsoft.Data.Sqlite | 10.0.12 | MIT | `Hollow.Persistence` | SQLite ADO.NET provider. |
| Microsoft.Data.Sqlite.Core | 10.0.12 | MIT | (dependency) | |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.12 | Apache-2.0 | (dependency) | Wires the provider to the bundled SQLite. |
| SQLitePCLRaw.core | 2.1.12 | Apache-2.0 | (dependency) | |
| SQLitePCLRaw.provider.e_sqlite3 | 2.1.12 | Apache-2.0 | (dependency) | |
| SQLitePCLRaw.lib.e_sqlite3 | 2.1.12 | Apache-2.0 (SQLite itself is public domain) | (dependency) | Native SQLite. **Trimmed** to `win-x64` and `linux-x64`. |
| System.Memory | 4.5.3 | MIT | (dependency of SQLitePCLRaw.core) | The .NET 10 SDK prunes it (the framework has it); kept for restores that don't prune. |

All were downloaded from nuget.org on 12 October 2026. The two trimmed packages keep their id, version and
licence files; only the other platforms' native files and the repository signature (which no longer matches) were
removed. To add a platform, replace the package with the original from nuget.org.

## Adding or updating a package

1. Download the `.nupkg` (and any new dependency) from nuget.org into `third_party/packages/`.
2. Add its ID to the `hollow-third-party` mapping in `NuGet.config`.
3. Add a row above, with the licence, and build once with network access to be sure nothing else is pulled.
