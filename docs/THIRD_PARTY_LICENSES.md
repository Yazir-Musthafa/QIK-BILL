# Third-party licences

Status: Phase 8, 15 October 2026. Phase 8 adds DocumentFormat.OpenXml and pins the security-patched System.Security.Cryptography.Pkcs package (checked against `Directory.Packages.props`, the project files and the lock files). Generated from `packages.lock.json` and each package's `.nuspec` licence expression in the local NuGet cache. Versions are pinned in `Directory.Packages.props`.

**Summary:** every dependency is MIT or Apache-2.0, and the bundled font is under the SIL Open Font License. These licences allow commercial redistribution without a paid licence at any revenue level. They do require the licence and copyright notices to be included with the distributed product. Phase 9 must put those notices in the installer.

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
| PDFsharp | 6.2.4 | MIT | PDF export (Phase 6). Copyright the PDFsharp Team / empira Software GmbH. Its cryptography dependency is explicitly pinned below |
| DocumentFormat.OpenXml (+ .Framework) | 3.5.1 | MIT | CSV/Excel transfer and report workbook output; local package licence expressions verified |
| System.IO.Packaging | 10.0.2 | MIT | Open XML transitive dependency |
| System.Security.Cryptography.Pkcs (+ System.Formats.Asn1) | 10.0.13 | MIT | PDFsharp dependency override; avoids vulnerable Pkcs 8.0.1 |
| ZXing.Net | 0.16.11 | Apache-2.0 | QR code encoding (Phase 6). Copyright Michael Jahn and the ZXing authors. Apache-2.0 also requires keeping its NOTICE text with redistributions |
| .NET runtime and WPF (self-contained) | 10.0 | MIT | Runtime |

## Fonts shipped with the application

| Font | Version | Licence | Purpose |
|---|---|---|---|
| Noto Sans Regular and Bold (`src/Nexora.Printing/Fonts`) | Noto Sans hinted TTF from the Noto Project (copyright 2022 The Noto Project Authors) | SIL Open Font License 1.1, full text in `src/Nexora.Printing/Fonts/OFL.txt` (copied to the output as `licenses/NotoSans-OFL.txt`) | Printed documents, PDF and print preview. Embedded in the Nexora.Printing assembly and in exported PDFs (subset). The OFL allows bundling and embedding in commercial software; the font may not be sold on its own, and modified versions may not use the name "Noto". |

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
| ZXing.Net (also shipped, above) | 0.16.11 | Apache-2.0: the tests also use its barcode readers to decode what QIK Suite encodes |

## Pending decisions (later phases)

**Phase 6 choices (done).** PDFsharp (MIT) was chosen for PDF because the common alternatives have revenue-based or AGPL licences (QuestPDF's community licence, iText). ZXing.Net (Apache-2.0) encodes QR codes; QRCoder (MIT) was not used because its .NET 6 target depends on System.Drawing.Common. Code 128 and EAN-13 are encoded in QIK Suite's own code. Phase 9 must include the MIT, Apache-2.0 (with NOTICE) and OFL texts in the installer.

`dotnet list package --vulnerable --include-transitive` reported no known vulnerabilities on 10 October 2026.

On 15 October 2026, locked restore identified Pkcs 8.0.1 under [Microsoft’s October advisory](https://github.com/dotnet/announcements/issues/454). The explicit 10.0.13 reference updates the printing, reporting, desktop and test lock files without suppressing NuGet audit warnings.

The all-project transitive NuGet vulnerability audit reported no known vulnerable packages on 15 October 2026 after the update.
