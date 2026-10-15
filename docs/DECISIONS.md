# Decisions

## 2026-10-10: Product renamed to QIK Suite; internal names unchanged

**Decision.** The product shown to users is now **QIK Suite** (short form **QIK**). The executable is `QIKSuite.exe` (`AssemblyName` QIKSuite). The C# namespaces, project names, solution name and type names keep the old `Nexora` prefix for now: `Nexora.Domain`, `Nexora.Application`, `Nexora.Infrastructure`, `Nexora.Accounting`, `Nexora.Printing`, `Nexora.Desktop`, `Nexora.Tests`, `Nexora.sln`, `NexoraDbContext`, and `AddNexoraApplication`, `AddNexoraInfrastructure`, `AddNexoraAccounting`.

**Why.**
- **Users never see these names.** Namespaces and project names don't appear in the interface, the installer or the data folder.
- **A full rename is risky.** It touches every file, every EF migration's namespace and the migration snapshot. That would make the history hard to read and could break `dotnet ef` tooling, for no user benefit.

**What did change internally.** These all affect runtime behaviour or are visible on disk:

| Item | Before | After |
|---|---|---|
| Executable | `Nexora.exe` | `QIKSuite.exe` |
| WPF resource URIs | `/Nexora;component/...` | `/QIKSuite;component/...` (a test fails if any URI does not match the `AssemblyName`) |
| Single-instance mutex | `Local\Nexora.BusinessSuite.Desktop` | `Local\QIKSuite.Desktop` |
| Data folder | `%LocalAppData%\Nexora\BusinessSuite` | `%LocalAppData%\QIKSuite` |
| Database file | `nexora.db` | `qiksuite.db` |
| New log files | `nexora-YYYYMMDD.jsonl` | `qiksuite-YYYYMMDD.jsonl` |
| Manifest identity | `Nexora.BusinessSuite.Desktop` | `QIKSuite.Desktop` |

**Upgrade.** On startup, `DataDirectory.Prepare` (Infrastructure, tested on any OS) handles installations made before the rename:
1. If the old folder exists and the new one does not, it copies everything into `QIKSuite.migrating`, then renames that to `QIKSuite`.
2. The database and its write-ahead log are renamed together, so the copy stays a valid SQLite database. The stale `-shm` file is skipped.
3. The old folder is left untouched as a backup.
4. An interrupted copy is redone on the next start.

**Later.** The solution can be fully renamed (namespaces, projects, `QikDbContext`, and so on) as a separate, mechanical change. Do it in one dedicated commit, with no other edits, on a clean tree. Regenerate nothing: keep the existing migration IDs and only change namespaces. Then rebuild and run all tests.

## 2026-10-11: Printing is platform-neutral; Windows printing lives in the desktop app (Phase 6)

**Decision.** `Nexora.Printing` now targets plain `net10.0` (it was a placeholder `net10.0-windows` WPF project). It holds everything that can be tested without Windows:
- barcode and QR encoding;
- layout and pagination, and the document and label templates;
- PDF output;
- the ESC/POS command writer;
- `PrintingService` (preferences, rendering, reprint records, test prints).

The Windows parts live in `Nexora.Desktop/Services/Printing`:
- the printer gateway, using System.Printing and the spooler;
- the WPF preview and printing renderer.

They sit behind `Nexora.Printing.IPrinterGateway`, and tests use a fake.

**Why.** The brief asks for layout, pagination and totals to be unit-tested on macOS. The test project targets `net10.0` and cannot reference a `-windows` project.

**Libraries.**
- **PDFsharp 6.2.4 (MIT).** Chosen over QuestPDF (whose community licence is revenue-limited) and iText (AGPL or commercial).
- **ZXing.Net 0.16.11 (Apache-2.0)** for QR encoding. It has no dependencies on modern .NET. QRCoder was rejected because its net6 target pulls in System.Drawing.Common.
- **Code 128 and EAN-13** are encoded in our own code, small and verified by decoding with ZXing in tests.
- **Font:** the Noto Sans font (SIL OFL 1.1) is embedded. It is used for measuring, for PDF and for Windows printing, so every output paginates the same and includes ₹.

**One rendering path.** Templates produce `RenderedDocument` page items in points. The PDF renderer and the WPF renderer draw those items and nothing else.

## 2026-10-15: Reporting and data protection (Phase 8)

`Nexora.Reporting` is a platform-neutral project for report queries, typed CSV/Excel exports, imports and the dashboard. Open XML SDK 3.5.1 is MIT. Report access and cost visibility are enforced in services. Exports rerun the last completed report request with paging removed. Source navigation preserves report dates and uses the existing permission-checked services.

Whole-workspace backups use SQLite’s online backup API, gzip, SHA-256 and optional chunked AES-256-GCM with PBKDF2. Brief SQLITE_BUSY/LOCKED errors retry for up to ten seconds, including restore/rollback copies, following SQLite’s [backup lock handling guidance](https://www.sqlite.org/backup.html). A concurrent pool-cleanup test reproduced the original intermittent lock failure. Automatic checks run on startup and every 15 minutes while the desktop is open. Automatic and safety copies remain unencrypted until Phase 9 provides protected secret storage.

No domain model changed in this continuation; the existing `ReportsCostCentresBank` and `RestoreLedgerTriggers` migrations cover Phase 8.
