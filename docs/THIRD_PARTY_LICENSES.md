# Third-party licences

Status: Phase 1, 10 October 2026. Generated from `packages.lock.json` and each package's `.nuspec` licence expression in the local NuGet cache. Versions are pinned in `Directory.Packages.props`.

**Summary:** every dependency is MIT or Apache-2.0. These licences allow commercial redistribution without a paid licence at any revenue level. They do require the licence and copyright notices to be included with the distributed product. Phase 9 must put those notices in the installer.

No paid-licence packages are used. FluentAssertions 8 and later needs a paid commercial licence, so it was avoided on purpose. Tests use xUnit's built-in `Assert` (Apache-2.0).

## Shipped with the application

These DLLs appear in the self-contained `win-x64` publish output.

| Package | Version | Licence | Purpose |
|---|---|---|---|
| CommunityToolkit.Mvvm | 8.4.0 | MIT | MVVM source generators |
| Microsoft.EntityFrameworkCore (+ .Abstractions, .Relational) | 10.0.12 | MIT | ORM |
| Microsoft.EntityFrameworkCore.Sqlite (+ .Core) | 10.0.12 | MIT | SQLite provider |
| Microsoft.Data.Sqlite.Core | 10.0.12 | MIT | ADO.NET SQLite driver |
| SQLitePCLRaw.bundle_e_sqlite3, .core, .provider.e_sqlite3, .lib.e_sqlite3 | 2.1.12 | Apache-2.0 | Native SQLite binding. SQLite itself is public domain |
| Microsoft.Extensions.DependencyInjection (+ .Abstractions) | 10.0.12 | MIT | Dependency injection |
| Microsoft.Extensions.Logging (+ .Abstractions) | 10.0.12 | MIT | Logging abstractions |
| Microsoft.Extensions.Caching.*, .Configuration.Abstractions, .DependencyModel, .Options, .Primitives | 10.0.12 | MIT | Transitive dependencies of EF Core |
| Serilog | 4.3.0 | Apache-2.0 | Structured logging |
| Serilog.Extensions.Logging | 10.0.0 | Apache-2.0 | Serilog provider for Microsoft.Extensions.Logging |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 | Rolling file sink |
| .NET runtime and WPF (self-contained) | 10.0 | MIT | Runtime |

## Build-time only (not shipped)

Microsoft.EntityFrameworkCore.Design 10.0.12 is referenced with `PrivateAssets=all` for `dotnet ef` migrations. It is MIT, and so are its transitive dependencies: Microsoft.CodeAnalysis.* 5.0.0, Humanizer.Core 2.14.1, Mono.TextTemplating 3.0.0, Newtonsoft.Json 13.0.4, System.CodeDom and System.Composition.*. I confirmed these DLLs are absent from the publish output. The `dotnet-ef` 10.0.12 local tool is MIT.

## Tests only (not shipped)

| Package | Version | Licence |
|---|---|---|
| xunit, xunit.assert, xunit.core, xunit.extensibility.*, xunit.analyzers | 2.9.3 / 1.18.0 | Apache-2.0 |
| xunit.abstractions | 2.0.3 | Apache-2.0 (licence URL in nuspec) |
| xunit.runner.visualstudio | 3.1.5 | Apache-2.0 |
| Microsoft.NET.Test.Sdk, Microsoft.TestPlatform.*, Microsoft.CodeCoverage | 18.0.0 | MIT |
| coverlet.collector | 6.0.4 | MIT |

## Pending decisions (later phases)

The PDF library and the barcode/QR library (Phase 6) are not chosen yet. Several popular .NET PDF libraries have revenue-based or AGPL licences, for example QuestPDF's community licence and iText. Check each candidate against the expected revenue before adopting it, and record it here.

`dotnet list package --vulnerable --include-transitive` reported no known vulnerabilities on 10 October 2026.
