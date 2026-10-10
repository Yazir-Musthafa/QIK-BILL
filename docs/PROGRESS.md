# Nexora implementation progress

## Phase 1: complete on macOS, Windows runtime unverified (10 October 2026)

**Scope:** solution structure, application shell, design system, light and dark themes, dependency injection, logging, database setup and migrations, login, and the first-run wizard. Phases 2–9 have not started.

**Environment:** macOS 26.2 on Apple Silicon, .NET SDK 10.0.401 in `~/.dotnet`, `EnableWindowsTargeting=true`. **The WPF UI, printing and the installer cannot be run or tested on this machine.**

### Verified by build or test on macOS

| Area | Evidence |
|---|---|
| Solution builds | `dotnet build Nexora.sln`: 0 warnings, 0 errors, with warnings treated as errors. Includes the XAML markup compile, which checks types, properties and element names. |
| Locked restore | `dotnet restore --locked-mode` passes, both before and after a `win-x64` publish. |
| Self-contained publish | `dotnet publish -r win-x64 --self-contained` produces `Nexora.exe`, the native `e_sqlite3.dll` and the WPF runtime (149 MB). The output was not run. |
| Tests | **221 passed, 0 failed** (`dotnet test tests/Nexora.Tests`). |
| Domain rules | Financial year (April–March, labels, boundaries), GST state codes, company and username validation, lockout after 5 failures for 15 minutes. |
| Password security | PBKDF2-HMAC-SHA256 with 600,000 iterations and a unique 16-byte salt. Comparison is fixed-time. Older hashes are upgraded at sign-in. Malformed hashes fail without throwing. Password policy has a 10-character minimum and rejects passwords containing the username. |
| Database | A migration creates the schema on a new file. Re-running it is safe and keeps data. No model changes lack a migration. WAL journal mode. The plaintext password is absent from the database and WAL files. |
| Workspace flows | Setup is atomic, can only run once and stores nothing on invalid input. Sign-in matches usernames case-insensitively. Unknown user and wrong password give the same message. Sign-in returns to the last company. Company switching works. The demo company can be deleted, but not while it is active, and real companies cannot be deleted this way. Preferences persist. Operations require a session. |
| Formatting | Indian digit grouping (₹12,34,56,789). |
| Design rules (XAML source) | Both themes have the same keys. The brief's colours are exact. Neutral tokens stay neutral. WCAG AA contrast passes for 26 text-on-background pairs across the two themes. No hard-coded colours, gradients, blur or extra shadows. Font sizes and corner radii come from tokens. Every resource key is defined. Theme brushes are dynamic. No emoji. Each rule was checked to fail on a planted violation. |
| Dependencies | All MIT or Apache-2.0 (`THIRD_PARTY_LICENSES.md`). No known vulnerabilities. |
| Tests can fail | Disabling the lockout code made the lockout test fail. Planting design violations failed 6 rules. |

### Built but unverified (needs Windows)

- The app starting at all: DI composition in `App.ConfigureServices`, XAML resource lookup at runtime, theme dictionary swapping, pack URIs.
- How every screen looks and behaves: setup wizard, sign-in, shell, sidebar collapse, command palette, company switcher, popups, dashboard, settings, help, component gallery, dialogs, toasts.
- Keyboard shortcuts, Tab order, focus rings, Narrator, high contrast.
- Animations, and turning them off when Windows animation effects are off.
- DPI scaling from 1366×768 to 4K, and font fallback (Segoe UI Variable, Fluent Icons or MDL2).
- Writing logs and the database under `%LOCALAPPDATA%`, the single-instance mutex, and the startup error message.
- `scripts/build-windows.ps1` and `.github/workflows/windows.yml`: neither has been run (no PowerShell here, no Git remote).

`docs/QA_WINDOWS.md` is the checklist for these.

### Not done in Phase 1 (known gaps)

- **Data table:** column hiding, filtering, saved column layouts and a reusable totals row are not built. The native grid handles sorting, resizing, reordering and keyboard navigation.
- No chart first-load draw animation.
- No dedicated high-contrast theme.
- Users are installation-wide administrators with access to every company. Roles, permissions, per-company access, password reset and audit come in Phase 2.
- Session timeout and auto-lock are not implemented (Phase 9 security).
- The demonstration company has no stored sample records, only fixed illustrative dashboard figures. Phase 4/5 should seed real demo transactions.
- No installer (Phase 9).

This is an implementation checkpoint, not release approval. It claims no commercial readiness, accounting correctness or GST compliance.
