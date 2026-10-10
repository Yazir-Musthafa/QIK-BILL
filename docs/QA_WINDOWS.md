# Windows QA checklist: Phase 1

Phase 1 was built and tested on macOS. Nothing in this list has been performed yet. Run it on Windows 10 22H2 and Windows 11 before calling Phase 1 visually accepted. Record the date, machine, scaling and result for each item.

## Setup

1. Install the .NET SDK version in `global.json`.
2. Run `./scripts/build-windows.ps1`, or start `artifacts/publish/win-x64/Nexora.exe`.
3. Data is stored in `%LOCALAPPDATA%\Nexora\BusinessSuite\` (`nexora.db`, and `logs\nexora-*.jsonl`). Delete that folder to repeat first-run setup.

## First run and sign-in

- [ ] The first launch shows the setup wizard. Step 1 has focus in Company name.
- [ ] Each step shows inline errors for an empty name, no state selected, a short password and mismatched passwords. Next does not advance until they are fixed.
- [ ] Back keeps the values already entered. The review step shows them correctly.
- [ ] "Create workspace" opens the dashboard with a success toast. The UI stays responsive while the password is hashed (about 0.5 s).
- [ ] With the demonstration option ticked, the company switcher lists the demo company with a "Demo" badge.
- [ ] Close and reopen. The sign-in screen appears with focus in Username.
- [ ] A wrong password shows "The username or password is incorrect.", and the password box clears and takes focus.
- [ ] Five wrong passwords lock the account for 15 minutes, with a clear message.
- [ ] A second launch while Nexora is open shows "already running".

## Shell

- [ ] Ctrl+B collapses and expands the sidebar smoothly. Labels and group names hide when collapsed, and tooltips show labels.
- [ ] Ctrl+K opens the palette with focus in the search box. Typing filters, Up/Down moves, Enter runs, and Esc or clicking outside closes.
- [ ] The company switcher changes company, shows a toast and reloads the page. Sign out, then sign in, returns to the last company.
- [ ] Notifications and the user menu open and close, including clicking the button a second time.
- [ ] Ctrl+Shift+L and the toolbar button switch theme in place without losing state. The choice survives a restart.
- [ ] F1 opens Help. Ctrl+1 opens the Dashboard. Ctrl+Shift+G opens the component gallery.
- [ ] Every "Soon" module opens a page stating its phase. "Back to dashboard" works.
- [ ] Settings → Companies → Delete is offered only for the demo company. It is refused while the demo company is active, and works after switching.

## Visual

- [ ] Both themes: no hard-to-read text, no white flashes, popups and tooltips use theme colours, scroll bars are slim and themed.
- [ ] The dashboard shows "—" for a real company and sample figures with the "Demonstration data" badge for the demo company. Amounts are right-aligned with Indian grouping.
- [ ] The gallery renders every control and state. Disabled, error and focus states are visible.
- [ ] 1366×768 at 100%: the dashboard is usable with no clipped header actions, and the KPI grid stays at 4 columns.
- [ ] 1920×1080 at 125% and 150%, and 3840×2160 at 200%: text is crisp and the layout is not blurry (per-monitor DPI v2). Moving the window between monitors with different scaling re-renders it correctly.
- [ ] Segoe UI Variable renders on Windows 11, and Segoe UI on Windows 10. Icons render on both (Segoe Fluent Icons or MDL2 fallback).
- [ ] With Windows "Animation effects" off, no transitions play.
- [ ] Long company name (120 characters): truncates with an ellipsis in the switcher and does not break the layout.

## Keyboard and accessibility

- [ ] The whole flow (setup, sign-in, navigation, palette, theme, sign-out) works without a mouse.
- [ ] The Tab order is logical on every screen. The focus ring is always visible.
- [ ] Narrator reads field names, button names, login errors and toasts.
- [ ] High-contrast mode is at least readable. (Known gap: there is no dedicated high-contrast theme.)

## Robustness

- [ ] Make `%LOCALAPPDATA%\Nexora` read-only. Startup shows the "could not start" message, and the log records only the error type.
- [ ] The log files contain no passwords, usernames typed at sign-in, or database paths in error messages.
