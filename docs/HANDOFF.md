# Handoff: end of Phase 1 (10 October 2026)

Read this first. Then read `PROJECT_BRIEF.md` (the full spec, which stands in for the missing `AGENTS.md`) and `PROGRESS.md` (evidence for each claim).

## Starting point

Codex stopped partway through Phase 1. Its work is preserved in commit `a514918` ("codex: WIP, stopped here"). The folder was not a git repository, so I ran `git init` before that commit.

What existed then:

- A 7-project solution with central package pins and lock files. `EnableWindowsTargeting` was set.
- An `App.xaml.cs` with logging, DI and error handling, and one contracts file.

What was wrong:

- **The Desktop project did not build.** It referred to `SqliteWorkspaceService`, `MainWindow`, `App.xaml` and `app.manifest`, and none of them existed.
- **The SDK was not on this machine.** `PROGRESS.md` said .NET 10.0.401 was installed in a local folder, but it wasn't here.
- **There were 0 tests.** `.github/` was empty. `QA_WINDOWS.md` was referenced but didn't exist.

## What I did

Each item is one or more `claude:` commits.

1. **Installed the SDK.** .NET SDK 10.0.401 now lives in `~/.dotnet`, via Microsoft's `dotnet-install.sh`.
2. **Domain.** Added `Company`, `User` (with lockout), `FinancialYear` (April–March), GST state codes, `Username` and `AppSetting`.
3. **Application.** Implemented the existing `IWorkspaceService` contract as `WorkspaceService`. It sits on top of `IWorkspaceStore`, `IPasswordHasher` and `IClock`, so business rules stay out of Infrastructure. Also added the PBKDF2 hasher (600k iterations), password policy, setup validator and `IndianNumberFormat`.
4. **Infrastructure.** `NexoraDbContext` doesn't depend on a database provider. SQLite is plugged in through `IDatabaseProvider`. `EfWorkspaceStore` uses a short-lived context per call. Added the `InitialWorkspace` migration and a local `dotnet-ef` tool manifest.
5. **Desktop.** This covers:
   - theme tokens (light and dark), typography, and the control library;
   - the shell (grouped collapsible sidebar, top bar, command palette, company switcher, notifications, theme toggle, user menu);
   - the dashboard (honest empty state for real companies, labelled demo data);
   - pages for modules not built yet, plus settings, help, and the hidden component gallery;
   - the dialog and toast layers, motion that respects reduced-motion settings, and a per-monitor DPI v2 manifest;
   - the composition root in `App.ConfigureServices`.
6. **Tests: 221 passing.** They cover domain rules, security, SQLite integration and XAML design rules. I checked the tests can fail by breaking the code on purpose.
7. **Build and CI.** Added `scripts/build-windows.ps1` and `.github/workflows/windows.yml`. `RuntimeIdentifiers=win-x64` is now set for all projects; without it, publishing broke locked-mode restore.
8. **Docs.** Wrote `README.md`, `PROGRESS.md`, `DESIGN_SYSTEM.md`, `QA_WINDOWS.md` and `THIRD_PARTY_LICENSES.md`.

## Files changed

- **Root:** `Directory.Build.props`, `Directory.Packages.props`, `dotnet-tools.json`, `README.md`, `.github/workflows/windows.yml`, `scripts/build-windows.ps1`.
- **Domain:** `src/Nexora.Domain/**` (all new).
- **Application:** `src/Nexora.Application/**`. `WorkspaceContracts.cs` moved to `Workspace/` with its content unchanged.
- **Infrastructure:** `src/Nexora.Infrastructure/**` (all new, including `Persistence/Migrations/Sqlite/`).
- **Desktop:** `App.xaml`, `App.xaml.cs` (rewired DI, theme applied before the window is shown), `MainWindow.*`, `app.manifest`, `Themes/*`, `Controls/*`, `Services/*`, `ViewModels/*`, `Views/*`.
- **Tests:** `tests/Nexora.Tests/**` (`Domain/`, `Security/`, `Infrastructure/`, `Workspace/`, `Design/`).
- **Docs:** `PROGRESS.md`, `DESIGN_SYSTEM.md`, `QA_WINDOWS.md`, `THIRD_PARTY_LICENSES.md`, `HANDOFF.md`.
- **Lock files:** all `packages.lock.json` files were regenerated.

## Known problems and risks

- **The WPF app has never been run.** It compiles, including the XAML markup compile, but nothing checks runtime XAML on macOS. Expect a first pass of fixes on Windows. Likely trouble spots:
  - resource lookup across merged dictionaries;
  - `ComboBox` and `DataGrid` templates;
  - popup placement offsets;
  - `FocusOnVisible` timing;
  - sidebar width animation.
- **`App.ConfigureServices` isn't covered by a test.** The test project targets `net10.0` and can't reference the `net10.0-windows` Desktop project. Application and Infrastructure registrations are validated with `ValidateOnBuild` in the integration tests.
- **The build script and CI workflow have never run.** There's no PowerShell and no Git remote here.
- **The data table is incomplete.** Column hiding, filtering, saved layouts and a reusable totals-row control are missing.
- **Users have no roles yet.** Everyone is an admin of every company. There's no password reset, session timeout or audit log.
- **Demo data is display-only.** The demo company has fixed dashboard figures, not stored sample transactions.
- **`ShellViewModel` builds page view models itself** instead of resolving them from DI. This is acceptable for now. Move to a page factory when pages gain more dependencies.
- **`AGENTS.md` doesn't exist.** `docs/PROJECT_BRIEF.md` is the spec.

## Next steps

1. **Run Phase 1 on Windows.** Push to a GitHub remote so `windows.yml` runs, then work through `docs/QA_WINDOWS.md` on a Windows 11 machine. Fix runtime XAML issues before starting Phase 2.
2. **Finish the data-table component.** Build a reusable list control with column show/hide, filtering, saved per-user layouts and a totals row. Phase 4 list views depend on it.
3. **Phase 2 data model.** Add `CompanyId` to every business table, EF global query filters driven by the current session, and tests proving one company can't read another's data.
4. **Phase 2 users and roles.** Add the six roles from the brief, per-module and per-action permissions checked in the Application layer, user management and password reset/change. Then add the tamper-evident audit trail (old and new values).
5. **Small Phase 1 gaps.** Add a page factory resolved through DI, and consider a chart draw-in animation (respecting reduced motion) and a high-contrast theme.
