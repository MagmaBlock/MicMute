# MicMute DPI-Scaling Rebuild — Design & Plan

**Date:** 2026-06-04
**Branch:** `dpi-scaling-rebuild`
**Baseline:** v2.2.7

## Goal

Make every MicMute GUI surface **scale-independent by construction** — 100% and
150% display scale proportionally identical (same spacing, same consistency, just
scaled). Replace absolute-pixel layout with WinForms layout containers. Verify at
**real 150%** on the Tiny11 Hyper-V lab. Ship one release.

**Acceptance bar (hard):** for every form, the 100% and 150% offscreen renders are
proportionally identical. Not "done" until *seen* at 150%. This mirrors the
workspace law in `memory/feedback_scale_independence_is_the_acceptance_bar.md`.

## Why now

- MicMute's dialogs — `SettingsDialog` above all — are absolute-pixel layouts
  (`int y` cursor, `new Point(x,y)`, hand-computed columns) with an
  `AutoScaleMode.Dpi` retrofit bolted on. **Four shipped versions (v2.2.4 → v2.2.7)**
  hand-patched DPI clipping and still carry band-aids (literal `"\n"` hard-breaks,
  `MaximumSize` wrap hacks, "design-space-96 vs live-monitor-DPI" coordinate-mixing
  notes). Same disease that shipped EQSwitch broken to real 150% users.
- Proven cure: **EQSwitch v3.24.33** rebuilt its SettingsForm on layout containers
  (`eqswitch/UI/CardLayout.cs`) — real-150%-verified. Pattern:
  `memory/reference_winforms_dpi_layout_container_rebuild.md`.
- New capability: the **Tiny11 lab** does real 150% headlessly
  (`lab.ps1 dpi 150` → registry + reboot → `GetDpiForMonitor=144`). I can self-verify
  with no human hardware in the loop — the bottleneck that made EQSwitch need Suzy's
  laptop is gone.

## DPI-mode decision (surface the conflict, don't blend)

**MicMute stays `PerMonitorV2`** (already declared in `app.manifest` + `csproj`;
`Program.cs` calls `ApplicationConfiguration.Initialize()`). It's a pure tray app —
no injected/child windows — so PerMonitorV2 is correct and already the floor.

> **Do NOT copy EQSwitch's `SystemAware` here.** EQSwitch keeps SystemAware *only*
> because PerMonitorV2 breaks its injected EQ game-window pixel math
> (`memory/reference_eqswitch_dpi_systemaware_carveout.md`). MicMute has no such
> windows. This is a deliberate divergence between the two apps.

**Consequence for the port:** under `PerMonitorV2` + `AutoScaleMode.Dpi` the framework
*does* scale control `Bounds` and fixed widths via `PerformAutoScale`. So when porting
`CardLayout`, **drop `DpiScale.SizeFitFields`** — that helper exists only because
SystemAware leaves widths unscaled; under PerMonitorV2 it would **double-scale**.
Confirm empirically via the 150% render, not by reasoning.

## The three hazard classes (per-form audit checklist)

1. **`Bounds` + `DataGridView` columns** → framework scales them (keep the
   `AutoScaleDimensions=(96,96)` + `AutoScaleMode.Dpi` baseline; **never** hand-scale —
   double-scale re-clips). *MicMute has no DataGridView.*
2. **Non-`Bounds` ints** (`ComboBox.ItemHeight`, `TabControl.ItemSize`) → AutoScale
   skips them; font-derive after the handle exists. *MicMute: no TabControl; watch the
   OSD-duration `NumericUpDown` + Theme/Startup combos.*
3. **Owner-draw paint geometry** (`FillEllipse`/`DrawString`/`DrawLine`/`EM_SETMARGINS`
   literals) → painted in raw device px, never scaled; wrap in `LogicalToDeviceUnits`.
   *MicMute: `OsdForm` (dot ellipse, text x-offset, padding), `MenuRenderer`
   (separator inset, check pen).*

## Per-surface plan

| Surface | Current | Action | Hazard |
|---|---|---|---|
| `SettingsDialog.cs` (1485 ln) | Absolute-pixel + `AutoScaleMode.Dpi` retrofit | **Full container rebuild** (CardStack/Card/Fields/Bars) | 1, 2 |
| `UpdateDialog.cs` | Absolute-pixel, fixed `ClientSize(420,180)`, `BtnRowY()` | **Small container rebuild** (label/label/progress/button-bar; keep relative progress fill) | 1 |
| `OsdForm.cs` | Owner-draw pill, measure-driven size | `LogicalToDeviceUnits` the paint literals (dot `11`/`8`, text `24`, padding `10`/`12`); keep `Shell_TrayWnd` anchoring | 3 |
| `MenuRenderer.cs` | Owner-draw `ToolStripProfessionalRenderer` | Verify @150%; scale separator inset (`4`) + check pen only if it reads off | 3 |
| `HelpWindow.cs` | Single all-anchored `RichTextBox`, point fonts | Light/no touch (already correct); optional `Dock.Fill`+`Padding` cleanup | — |
| `UiFactory.cs` | Builders take `(x,y)` — the anti-pattern enabler | Refactor to **height-free, unparented** builders (drop `x,y`); mirror EQSwitch `Fields` | — |
| `UiTokens.cs` | Width/size consts + point fonts | Keep fonts/colors; retire absolute-layout width constants | — |
| `Program.cs` / `app.manifest` / `csproj` | PerMonitorV2 + `ApplicationConfiguration.Initialize()` | **Unchanged** — floor is correct | — |

**SettingsDialog — preserve every behavior** (rebuild layout, keep logic):
inline hotkey capture + reject-animation timers, `ValidateHotkeysBeforeApply`
(parse/dup/conflict-probe/risky-PTT), atomic apply ordering, file-row browse +
`ValidateCustomFile`, theme restart-to-apply, all event wiring, and the `Dispose`
sweeps (probe-id unregister, font disposal, reject-timer sweep).

**UpdateDialog — layout only.** Do NOT touch the async/CTS/redirect-validation/
SHA256/File.Replace logic. The rebuild is the control geometry, nothing else.

## Verification harness

- Add `UI/DiagRender.cs` + `Program.cs` flag `--diag-render-form <Name> [--all]
  --offscreen --out <dir>` (`#if DEBUG`, out of Release). Mirrors EQSwitch's
  `DiagRender.cs`.
- **100% baseline:** render on the **Asus host** (real 96 DPI = the reference). No VM.
- **150%:** render in **one locked Tiny11 window** (deploy Debug exe → `lab.ps1 dpi 150`
  → render-all → `shot`/scp PNGs back → release lock).
- Diff PNGs; iterate to proportional-identical. `DeviceDpi` in the log confirms scale
  (144 = 150%). The render is ground truth — it beats any static "this will/won't clip"
  claim (EQSwitch lesson: 3 reviewers wrong, the PNGs right).
- `dotnet test` (`SettingsDialogValidationTests`, `ConfigParseHotkeyTests`, etc.) — the
  layout rebuild must not regress validation logic.

## Tiny11 shared-VM protocol (multi-terminal safe)

DPI on the VM is **global + reboot-required**, so it MUST be serialized — other
terminals share this lab.

- **Lock file** `D:\Hyper-V\Tiny11Lab\.in-use`, one line:
  `<ISO-8601 timestamp> | <session-id> | <holder description> | target=<dpi>`
- Before any `lab.ps1 dpi/deploy/shot/restore/up/down`: read `.in-use` +
  `_.claude/_comms/active-work.md`. A **fresh** claim (<15 min) by another holder →
  **queue**; never change DPI or reboot under them. Else write the lock + note it in
  `active-work.md`.
- **100% from host (no VM)** → the VM is needed only at 150% → one reboot, minimal hold.
- **Release:** delete `.in-use` + clear the `active-work.md` note; leave VM DPI as-found
  (or set 100%).
- Stale lock (timestamp >15 min old) → reclaim.
- *Follow-up (not now):* bake this check into `lab.ps1` so it's enforced, not courtesy —
  deferred to avoid editing the tool other terminals are mid-using.

## Phases

0. **Harness + baseline** — `DiagRender` + flag; capture all forms at host-100% and
   locked-Tiny11-150%.
1. **Primitive** — port `CardStack/Card/Fields/Bars` into MicMute rewired to
   `Theme`/`UiTokens`, **no `SizeFitFields`**; refactor `UiFactory`.
2. **Rebuild** — `SettingsDialog` → `UpdateDialog` → `OsdForm`/`MenuRenderer` owner-draw
   → `HelpWindow` light touch.
3. **Prove** — re-render at 150%, diff to proportional-identical; `dotnet test`.
4. **Ship** — branch → release once renders are clean. No Claude attribution in git.

## Risks / watch-items

- **Double-scale:** porting `SizeFitFields` under PerMonitorV2. Mitigation: drop it;
  verify via render.
- **Behavior regression in SettingsDialog** (1485 ln of layout + logic intertwined):
  rebuild layout, keep logic verbatim; lean on the test suite + a manual smoke (capture
  a hotkey, Apply, validate, theme-flip restart).
- **OSD anchoring:** only scale paint literals; don't disturb the `Shell_TrayWnd` /
  working-area anchoring.
- **LTR status:** a significant change to an LTR app — justified as a correctness fix
  for real non-100% users; shipped as one verified release.

## Out of scope (follow-ups)

- Promote MicMute-local `CardLayout` to a shared, theme-agnostic primitive feeding the
  by-default WinForms scaffold (`memory/project_winforms_dpi_by_default_enforcement.md`).
- Bake lock enforcement into `lab.ps1`.
