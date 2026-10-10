# QIK Suite design system

Direction: **calm, dense, precise**. QIK Suite should feel like a careful accounting tool or code editor, not a marketing page. Information, amounts and keyboard navigation come first.

The source of truth is in `src/Nexora.Desktop/Themes/`. `tests/Nexora.Tests/Design/DesignRuleTests.cs` enforces the rules below on every build, and each rule has been checked to fail when it is broken. Visual quality on Windows has not been verified yet. See `QA_WINDOWS.md`.

## Files

| File | Contents |
|---|---|
| `Themes/Light.xaml`, `Themes/Dark.xaml` | Colour tokens (`*Color`) and brushes (`*Brush`), plus the one `PopupShadow`. Both files define exactly the same keys. |
| `Themes/Typography.xaml` | Font families, the type scale, radius and height tokens, and text styles. |
| `Themes/Controls.xaml` | Control library: buttons, inputs, combo box, check box, radio, scroll bars, tooltip, panels, badges, skeleton, navigation rows, data grid. |
| `Controls/` | Attached behaviours (`FieldAssist.HasError`, `Behaviors.FocusOnVisible`, `Behaviors.AnimateIn`), `Motion`, `TransitionPresenter` and converters. |
| `Views/GalleryView.xaml` | Hidden component gallery (**Ctrl+Shift+G**) showing every control and state. |

## Colour

The palette is neutral: warm-grey surfaces, near-black text and hairline borders. **One accent (teal)** is used only for primary actions, selection and focus. Semantic colours are muted and used only for status text, small badges and chart series where the colour carries meaning.

| Token | Light | Dark |
|---|---|---|
| Background | `#F6F5F2` | `#16171A` |
| Surface | `#FFFFFF` | `#1E2024` |
| SurfaceSubtle (sidebar, table header) | `#FAF9F7` | `#1A1C1F` |
| Hover / Pressed | `#F0EEEA` / `#E8E6E0` | `#272A30` / `#2E3138` |
| Border / BorderStrong (inputs) | `#E3E1DB` / `#CFCCC4` | `#2D3036` / `#3C4048` |
| Text / TextSecondary / TextDisabled | `#1C1D1F` / `#6B6E73` / `#A3A5A9` | `#E8E9EB` / `#9A9DA3` / `#5E626A` |
| Accent / OnAccent | `#0F5F63` / `#FFFFFF` | `#5FAEB1` / `#0E1517` |
| AccentSubtle (selection) | `#E3EEEE` | `#1F3335` |
| Success / Warning / Danger | `#2E6A4F` / `#87591A` / `#A13A35` | `#7DB699` / `#D2A55E` / `#E08C86` |

The dark theme was designed separately, not inverted. Surfaces get lighter as they come forward. The accent becomes a lighter tint with dark text on accent buttons, and semantic colours are desaturated.

**Enforced:** both themes have the same keys, and the brief's values are exact. Neutral tokens have chroma below 0.06, so no second accent can creep in. Text pairs meet WCAG AA 4.5:1 in both themes, including text on accent and on danger, and badge text on its tinted background. Views contain no hex colours or named colours. Theme brushes are always `DynamicResource`, so switching themes updates the window in place.

## Typography

- `Segoe UI Variable Text`, falling back to `Segoe UI`. Icons come from `Segoe Fluent Icons`, falling back to `Segoe MDL2 Assets`. The code points used exist in both fonts.
- The scale is 12 (tables and captions), 13 (body and controls), 15 (section headings) and 20 (page titles and KPI figures). **Enforced:** a `FontSize` literal outside `Typography.xaml` fails the build tests.
- Hierarchy comes from weight (SemiBold) and spacing, not colour.
- Amounts use `Text.Amount` and `Cell.Amount`, which give tabular figures and right alignment. Formatting comes from `IndianNumberFormat`, using lakh/crore grouping (₹18,42,350) independent of OS culture data.

## Shape, spacing and depth

- Corner radius is 4 on controls and 6 on panels and dialogs. **Enforced:** the only literal radius values allowed are 0, 4 and 6.
- Spacing follows an 8px grid. Controls and table rows are 28 and 30px high.
- Borders group content. The only shadow is `PopupShadow`, used by popups, tooltips, toasts and dialogs. **Enforced:** `DropShadowEffect` may appear only in the theme files. No gradients and no blur anywhere.

## Motion

`Controls/Motion.cs` holds the timing rules: 120, 160 or 200 ms with a cubic ease-out.

- Page change: 160 ms fade plus an 8px upward slide (`TransitionPresenter`).
- Sidebar collapse: animated width.
- Dialogs, toasts, the palette and wizard steps: fade and slide in (`Behaviors.AnimateIn`).
- Theme switch: a short fade from 85% opacity.
- Button press: an instant 2% scale-down.

All motion is off when Windows "Animation effects" is off (`SystemParameters.ClientAreaAnimation`). Animations never block input. There is no looping, bouncing or spinning motion. Chart first-load draw animation is **not implemented**.

## Application shell

- **Sidebar:** grouped destinations with icon plus label, a 3px accent indicator on the selected item, and modules not yet built marked "Soon". Ctrl+B collapses it to icons (64px).
- **Top bar:** search, which opens the command palette (Ctrl+K: type to filter, Up/Down, Enter, Esc). Also the company switcher with financial-year label, notifications, theme toggle (Ctrl+Shift+L) and user menu (sign out).
- **Dashboard:** KPI figures in one bordered panel with hairline dividers (no coloured tiles), revenue vs expenses bars (teal and neutral), receivables ageing, top products, low stock and recent activity, plus quick actions.
  - A real company shows "—" and plain empty-state text, never ₹0, so nothing looks like a computed balance.
  - The demonstration company shows fixed sample figures with a "Demonstration data" badge.
- **States:** loading uses static skeleton blocks (no shimmer). Empty states give short text and at most one action. Errors say what happened and what to do, with Retry. Confirmations use a modal dialog where focus starts on Cancel and destructive actions get the danger button.
- **Modules not built yet** open a page that states the planned phase and that nothing can be recorded there.

## Data tables

The implicit `DataGrid` style gives compact 30px rows, a sticky header, horizontal hairlines and no zebra striping. Sorting (with a glyph), column resize and reorder, and keyboard row navigation come from the native grid. A totals row is shown in the gallery as a pattern. **Not built yet:** column hiding, filtering, saved column layouts, and a reusable totals-row control. These are needed by the list views from Phase 4 onward.

## Accessibility

Inputs and icon buttons have `AutomationProperties.Name`. Login errors use a live region, and toasts are polite live regions. Every control has a keyboard focus ring (2px accent). Screen-reader behaviour, Narrator output and high-contrast mode are **unverified**.
