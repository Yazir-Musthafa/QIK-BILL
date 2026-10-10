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
