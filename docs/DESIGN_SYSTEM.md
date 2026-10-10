# Nexora design system — Phase 1

Nexora is a compact Windows workspace: calm, dense and precise. Information, amounts and table navigation lead the interface. Decorative graphics, gradients, emoji, glass effects and coloured KPI tiles are excluded.

## Colour and hierarchy

All view colours resolve through resource dictionaries. Light and dark dictionaries expose the same token keys; controls consume dynamic resources so switching does not require recreating data or a session.

| Role | Light | Dark |
|---|---|---|
| Background | `#F6F5F2` | `#16171A` |
| Surface | `#FFFFFF` | `#1E2024` |
| Border | `#E3E1DB` | `#2D3036` |
| Primary text | `#1C1D1F` | `#E8E9EB` |
| Secondary text | `#6B6E73` | `#9A9DA3` |
| Primary action / focus | `#0F5F63` | restrained light teal |

Muted semantic colours are reserved for warnings, errors and meaningful status indicators. Charts use teal and neutral series; series have explicit labels. Amounts are right aligned and formatted in the Indian numbering convention.

## Typography and geometry

- Segoe UI Variable with Segoe UI fallback; one consistent Segoe Fluent Icons family.
- Table text: 12. Body and controls: 13. Section headings: 15. Page titles: 20.
- Eight-unit spacing grid. Controls have a four-unit radius; panels and dialogs use six.
- Borders provide grouping. Avoid large shadows or oversized rounded cards.
- Compact native data grids provide keyboard navigation, sorting, resizing and column reordering.

## Interaction

The sidebar groups workspace destinations; the selected row has a restrained accent. It collapses to icons. The top bar contains search, company selection, theme and session controls. Commands expose keyboard equivalents in Help. A hidden gallery provides a place to inspect controls and loading, empty, error and confirmation states.

Transitions, where implemented, last 160 ms and respect `SystemParameters.ClientAreaAnimation`. Windows runtime behaviour must be checked on Windows; a successful cross-build does not establish visual quality or accessibility.

## Data truth

Real companies start with an honest empty workspace. Financial modules have not been implemented in Phase 1, so their dashboard amounts must not be interpreted as accounting balances. Optional demonstration data is stored separately, labelled on the dashboard, and can be deleted. Demo values are illustrative presentation samples, not posted transactions or reconciled reports.

Planned module destinations must state their phase and availability. Quick actions must never imply that a document was saved or posted when no corresponding workflow exists.

## Verification gate

The Windows checklist in `QA_WINDOWS.md` is the authority for visual acceptance. Test both themes, keyboard-only use, 1366×768 and 4K at 100–200% scale, screen-reader names, contrast, reduced animations, long company names, error messages and data-grid overflow. Visual acceptance remains unverified until those checks are performed.
