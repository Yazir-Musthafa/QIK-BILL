# Handoff: end of Phase 2 (10 October 2026)

Read this first. Then read `PROJECT_BRIEF.md` (the full spec; there is no `AGENTS.md`) and `PROGRESS.md` (evidence for each claim). Phase 3 (accounting engine) starts next.

## State

- **Build and tests:** the solution builds with 0 warnings. **341 tests pass** (`dotnet test tests/Nexora.Tests`).
- **Migrations:** `InitialWorkspace` and `CompaniesUsersRolesAudit`.
- **Not yet run:** the WPF UI, printing and the installer. Windows testing is planned after Phase 3.

## How Phase 2 is built

- **Isolation.** Company-owned entities implement `ICompanyOwned`. `NexoraDbContext` gives each one a query filter, a Companies foreign key and an index. A write guard refuses any write that targets another company. Companies and Users have their own filters. References between company-owned rows use composite keys `(CompanyId, Id)`, so the database rejects cross-company links. Only `EfWorkspaceStore` may call `IgnoreQueryFilters` or set `SystemScope`; a test scans the source to enforce this.
- **Persistence for services.** Services use `IUnitOfWork` from Application. It's provider-neutral: plain LINQ expressions plus Add, Remove, Record and Commit. A writable unit begins a transaction (`BEGIN IMMEDIATE` on SQLite) and tracks entities, so the audit trail sees real old values. Installation-level work (setup, sign-in, provisioning, upgrades) goes through `IWorkspaceStore`.
- **Permissions.** Permission keys live in `Domain/Security/Permissions.cs`. The session (`ISessionContext`) holds the role's permissions. `IAuthorizer.Demand` is called first in every protected service method. Two rules prevent escalation: you can't grant or assign permissions you don't hold, and every company keeps at least one administrator.
- **Audit.** `AuditRecorder` runs inside `SaveChangesAsync`. It records changes to `IAudited` entities and explicit `AuditEvent`s, and seals each entry with a sequence number and a SHA-256 hash chain per company. `AuditService.VerifyAsync` re-checks the chain.
- **Provisioning and upgrade.** `CompanyProvisioner` seeds roles, the creator's admin membership, numbering rules, and data from any `ICompanySeedContributor`. On startup, `UpgradeAsync` adds missing seed data to existing companies. Phase 3 should register its chart of accounts as a contributor so existing companies get one.

## Files added or changed in Phase 2

- **Domain:** `Common/ICompanyOwned.cs`, `Companies/{Company,Locations,TaxIdentifiers}.cs`, `Numbering/*`, `Security/{Permissions,Role}.cs`, `Audit/AuditEntry.cs`, `Users/User.cs`.
- **Application:** `Abstractions/{ISessionContext,IUnitOfWork,IWorkspaceStore}.cs`, `Security/Authorizer.cs`, `Workspace/{WorkspaceService,CompanyProvisioner,WorkspaceContracts}.cs`, `Companies/CompanyService.cs`, `Users/{UserService,RoleService}.cs`, `Audit/AuditService.cs`, `DependencyInjection.cs`.
- **Infrastructure:** `Persistence/{NexoraDbContext,AuditRecorder,EfUnitOfWork,EfWorkspaceStore}.cs`, `Configurations/EntityConfigurations.cs`, the new migration.
- **Desktop:** `ViewModels/{PageViewModel,ShellViewModel,MainViewModel,Navigation}.cs`, `ViewModels/Settings/*`, `Views/SettingsView.xaml`, `Views/Settings/*`, `Views/ShellView.xaml`, `Controls/Converters.cs`, `App.xaml(.cs)`.
- **Tests:** `Domain/Phase2DomainTests.cs`, `Companies/{CompanyIsolationTests,AuditTrailTests}.cs`, `Security/PermissionTests.cs`.

## Known problems

- **The UI has never been run.** All WPF screens are unverified.
- **Roles take effect at the next session.** Session permissions are cached until sign-in, company switch or refresh. That's fine for a single-user desktop app, but a role edit only applies to the editor's own session immediately.
- **Users can be listed by username.** User accounts are installation-wide, so `AddExisting` can tell you a username exists in another company. That's intended, since usernames are global.

## Next steps

1. Phase 3, the accounting engine (in progress next).
