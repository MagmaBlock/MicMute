# Changelog

*LTR — Long-Term Release · one-click self-update built in.*

All notable changes to MicMute are documented here.

## [2.1.12] - 2026-04-25

### Security
- **Custom icon/sound paths from network shares are now rejected at the file-picker.** Picking an icon or sound from a UNC path (`\\server\share\foo.ico`), forward-slash UNC variant (`//server/share/foo.ico`), or `file://` URI used to silently authenticate to the remote host the moment you clicked the file in the picker — leaking an NTLMv2 challenge that an attacker on the SMB endpoint can capture and crack offline. Settings now shows a clear "Network paths are not allowed" message instead, and the same gate runs again on Apply as defense-in-depth. Local paths are unaffected.
- **SHA256SUMS verification hardened against future cleanup.** The self-update integrity check previously relied on a downstream `.Trim()` chain to strip carriage returns from CRLF-formatted checksum files (which is exactly what the release CI produces on Windows). A future code cleanup that removed those `.Trim()` calls would have silently broken self-update verification on the very files our own CI generates. Splitting on both `\r` and `\n` upfront removes that fragility — no visible change for normal updates.

## [2.1.11] - 2026-04-23

### Fixed
- **The in-app updater now works again.** GitHub recently started serving release-asset downloads from a new CDN host (`release-assets.githubusercontent.com`) alongside the legacy one (`objects.githubusercontent.com`). MicMute's self-updater follows redirects manually and validates each hop against an explicit allowlist — the new host wasn't on it, so clicking *Upgrade Now* failed with `URL is not from an allowed origin`. Both hosts are now allowlisted. **Affected version:** v2.1.10 — that build shipped with the old allowlist and cannot self-update to this fix. To get past it, run `winget upgrade itsnateai.MicMute` or download `MicMute.exe` from the GitHub release page once and replace your copy. After v2.1.11 the self-updater works again as normal.

## [2.1.10] - 2026-04-18

### Added
- **SHA256 integrity check on self-update** — updates from v2.1.10 onward verify the downloaded exe against a signed `SHA256SUMS` file before replacing your install. If the file is missing or the hash doesn't match, the update aborts and tells you to download manually. Old releases without the hash file are grandfathered so you can still update to this version.
- **Crisp UI on high-DPI and mixed-DPI setups** — Settings, Update, and Help dialogs now render natively at each monitor's DPI instead of being bitmap-stretched. Text is sharper; layouts don't go blurry when you drag a dialog across monitors. If you're on a 4K laptop + 1080p desktop, this is the big visible change.
- **Dialog text bumped from 9 pt to 9.5 pt** — paired with the DPI change so text stays readable when the bitmap stretch goes away.
- **Survives laptop sleep / resume** — MicMute now re-initialises the audio endpoint, re-registers both the Toggle and Deafen hotkeys, and refreshes the tray icon when Windows comes back from S3/S4. Before this, you had up to 15 seconds of stale state (hotkeys bound to a mic that no longer exists) until the next sync tick recovered.
- **"Use it anyway" hotkey-conflict remembered** — if you confirm a hotkey conflict once, MicMute stops re-asking on every Apply/Save for that same combo. Change the combo and it re-probes fresh.

### Changed
- **OSD duration default: 1500 ms → 800 ms** — snappier mute/unmute toast that gets out of the way faster.
- **Sound feedback default: on → off** — fresh installs are quiet by default. Turn it back on in Settings if you liked the beeps.
- **Tray right-click menu reshuffled** — the "MicMute v…" header line is now a greyed, non-interactive label (like Caps Lock's tray). The hotkey combo below it is the clickable toggle. One fewer accidental click on the header.
- **Settings label renamed** — "On startup:" → "Mic mode On Startup:" to make it clearer what the dropdown controls.
- **Help window text + README** — dropped the "AHK syntax" phrasing; the `#^!+` shorthand is now described as "combo shorthand".
- **Faster cold start** — removed a 250 ms legacy-mutex back-compat wait that existed for v2.1.5 → v2.1.6 upgrade handoff. v2.1.5 is four versions old; the wait was cost with no remaining benefit.

### Fixed
- **Self-update no longer bricks your install if the new exe fails to launch** — the old `.old` copy is now kept across the restart so you can fall back. Previously, if Windows Defender or AV quarantined the freshly swapped binary right after the rename, you'd have no MicMute at all until you re-downloaded.
- **Self-update swap is atomic** — uses `File.Replace` instead of two separate renames. Closes a tiny window where a power loss or `taskkill` between the two renames could leave disk without `MicMute.exe` entirely.
- **Exit cleanup no longer double-fires** — clicking Exit twice quickly (or during a sync tick) used to log a stale-COM error and could leave a zombie tray icon.
- **Sticky Push-to-Talk no longer lies when an external app mutes your mic** — if Discord, Zoom, OBS, or the sound control panel mutes your mic while sticky-PTT is active, the "PTT — mic listening" bubble now goes away. Previously it stayed on screen while your mic was actually muted, and you'd need two tray clicks to recover.
- **Custom icons that fail to load now tell you** — if your configured icon path is broken (AV quarantine, file moved) and MicMute falls back to the built-in icon, you get a tooltip once per session instead of silently wrong icons.
- **Hotkey registration failures are logged** — if another app already owns your combo, the Win32 error code lands in `micmute.log` so you can diagnose "my hotkey stopped working" reports.
- **Custom icon + log path fallback** — if `%LOCALAPPDATA%\MicMute\` is locked down by policy, logs now fall back through `%TEMP%\MicMute\` to the exe directory; if the primary log breaks mid-session, writes go to `%TEMP%\micmute-emergency.log` so diagnostic breadcrumbs survive.
- **Config robustness** — INI file is written with a per-call unique temp name + filesystem flush-to-disk + retry on transient AV locks. UTF-16LE detection tightened so a UTF-8 file whose second byte happens to be `0x00` is no longer misread and corrupted. Invalid INI values (malformed Mode, non-numeric OSD duration) now log the bad value instead of silently reverting to defaults.
- **Settings → Tab key works in hotkey capture** — pressing Tab inside a capturing hotkey field now commits and advances focus like a normal field, instead of being swallowed with nothing visible happening.
- **Duplicate-hotkey check compares combos, not raw strings** — a hand-edited INI with the same combo in different modifier order (e.g. `^+a` vs `+^a`) is now caught as a duplicate instead of silently registering both and losing one.
- **Settings dialog no longer hosts 4 invisible zero-width TextBoxes** — replaced with a cleaner in-memory pattern. No behaviour change; fewer phantom controls in the focus tree.

### Under the hood (no user-visible change)
- Audit swarm cleared 154 potential issues across COM, resources, lifecycle, error handling, UI patterns, config, self-update, and hotkey paths.
- Release build clean with TreatWarningsAsErrors; semgrep 0/217 findings held.

### Notes
- Existing config + hotkey bindings survive the upgrade untouched.
- If you had `SoundFeedback=1` or `OSD_Duration=1500` saved in your INI from a prior version, those stay on the values you chose — the new defaults only apply to fresh installs.

## [2.1.9] - 2026-04-17

### Added
- **Sticky Push-to-Talk via tray click** — in Push-to-Talk mode, left-click the tray icon (or the "Toggle Mute" menu item) to unmute the mic and pause the hotkey. A persistent "PTT — mic listening" bubble stays on screen until you left-click again to re-mute. Useful for holding the mic open during a long conversation without keeping a key held.
- **Inline hotkey capture in Settings** — click the Toggle Mute or Deafen hotkey field, the box goes yellow, press your combo — captured in place. No more separate pop-up window. Escape cancels, Enter commits, click away commits.
- **Hotkey validation at Save** — MicMute now warns you if your Toggle and Deafen hotkeys are the same (they can't both fire), if another app already has your combo claimed via Windows' global-hotkey system, if your combo isn't valid for the current mode, or (in PTT mode) if it's a key you'd press during normal typing.
- **Custom file validation** — picking a bogus `.ico` (e.g. a PNG renamed) or a compressed WAV (MP3/ADPCM/µ-law) is rejected up front with an explanation instead of silently failing later. Very large files warn with a soft prompt.

### Changed
- **Push-to-Talk works in fullscreen games by default** — the "Low-latency PTT" opt-in toggle is gone. PTT now uses the passive polling path unconditionally, so fullscreen-exclusive games no longer eat your hotkey, and bare modifier keys (Right-Ctrl alone, etc.) work the way Discord does them. Nothing for game anti-cheat to flag — same hardware-state API games read themselves.
- **Push-to-Talk always starts muted** — no more needing to press your hotkey once at launch before anything seems to happen. Settings → On startup notes this when you're in PTT mode.
- **Tray menu refreshed** — MicMute title is centered at the top. The hotkey line is now just the combo itself, bold, bracketed by separators — click to open Settings. Settings moved above Exit in its own section.
- **Settings window refreshed** — Hotkeys and Custom Files pack into tidy two-column grids; OSD Duration and "On startup:" share rows with their checkboxes; Mute Lock hint sits inline; OSD Duration is a spin box (min 500 ms, step 100); the Save button replaces OK. Dialog is shorter top-to-bottom without losing anything.
- **Mute Lock label made honest** — now reads "reverts external mute changes every 15 seconds (not instant)" so you know what it actually does. It's a 15-second fight-back, not a real-time veto — if Discord or Zoom sets mute mid-call, MicMute waits up to 15 s and flips it back.

### Fixed
- **OSD Duration field doesn't silently swallow bad input** — now a proper spin box (500–10 000 in 100 ms steps).
- **Hotkey stays working after mic unplug/replug in PTT mode** — before, the polling path would die on mic disconnect and never restart; Settings or a mode switch was the only way back. Auto-reconnect now re-arms the hotkey.
- **Change Hotkey window button sizes and alignment** — consolidated onto a shared factory so every button in every dialog is the same height, same style, same alignment. Addresses the "every tab has slightly different buttons" drift pattern.

### Notes
- Existing hotkey bindings and config survive the upgrade.
- The old `LowLatencyPtt=0/1` line in your MicMute.ini is now ignored — polling is always-on in PTT mode. It'll disappear from the INI the next time Settings writes.

## [2.1.8] - 2026-04-17

### Added
- **Low-latency push-to-talk (opt-in)** — new "Low-latency PTT" setting lets push-to-talk work over fullscreen games and accepts bare keys like Right-Ctrl alone (the way Discord's PTT handles it). Enable in Settings → Behavior, switch Mode to Push-to-Talk, then pick your key. Uses passive key-state reading, no keyboard hook — nothing for game anti-cheat to flag.
- **PTT risk warning** — if you bind a key you'd press during ordinary typing (bare letter, Space, Enter, Ctrl+A/C/V/X/Z/S/F, Shift+letter, Ctrl+Shift+letter) as your low-latency PTT key, MicMute now warns you first so the mic doesn't open unexpectedly while you're working.

### Changed
- **Settings window redesign** — Custom Files rows line up cleanly with the hotkey row above. Bottom toolbar: GitHub / Help / Check-for-updates are subtle left-aligned links, OK / Apply / Cancel are right-aligned action buttons. Enter commits, Esc cancels.
- **Help window rewrite** — actually readable now. Section headers stand out, body text no longer opens as one solid blue selection block, and the window is sized so you can read the first screen without scrolling.
- **OSD bubble palette softened** — the mute/unmute toast uses calmer greys and a softer accent dot. Less jarring when it appears.
- **Tray menu hotkey line tightened** — the old "Hotkey: Win + Ctrl + Shift + A" line is now "Change Hotkey…" with the current binding right-aligned, matching standard Windows menu style. No more menu stretching wide for long hotkeys.
- **Deafen hotkey field shows its state** — empty binding now reads "(not set)" in grey instead of an instruction that looks like an error; the field highlights yellow while you're capturing a new key.

### Fixed
- **Custom tray icons no longer feel laggy** — left-click toggling with a user-chosen icon file used to feel slightly slower than the default icons. All icons are pre-sized to the actual taskbar size at load time, so every mute/unmute swap is the same speed regardless of where the icon came from.
- **Double-blink on mode change removed** — the tray icon no longer flashes twice when you toggle mute. The error-case flash (PTT re-mute failure) is preserved — that's a real warning signal.
- **Mute state no longer lies** — when `SetMute` silently failed (rare device race), the tray and OSD used to update as if it had worked. The actual audio state is now verified and any failure surfaces a tooltip so you can reinit the mic.
- **Settings saves are now atomic** — if Windows crashes or the app is force-killed during a settings save, your config can no longer end up half-written and blanked on next launch. Writes go through a temp file and rename into place.
- **Self-update checksum file is origin-verified** — the accompanying `SHA256SUMS` file now gets the same github.com / objects.githubusercontent.com origin check that the binary download has always had.
- **Held-key safety during Settings → Apply** — if you pressed Apply while holding your PTT key, the mic could briefly stay hot while the hotkey re-registered. It's defensively re-muted first now.

### Notes
- Low-latency PTT defaults OFF — existing users see no behaviour change unless they opt in.
- Your existing hotkey bindings and config are preserved on upgrade.

## [2.1.6] - 2026-04-16

### Fixed
- **Push-to-talk stays muted when you change your hotkey or swap modes mid-hold** — the 2.1.4 fix for stuck-unmuted PTT missed the rebind path. If you ever held your PTT key and then picked a new hotkey from the tray menu, your mic stayed open until the next toggle. Same was true when middle-clicking the tray to swap between Toggle and Push-to-Talk while holding the key. Both paths now properly re-mute.
- **Exit still unmutes the mic even if you were holding PTT** — an earlier draft of the PTT fix could leave the mic muted in Discord/Zoom after you exited MicMute. Exit now owns the mic state cleanly regardless of what you were doing when you clicked it.
- **Deafen recovers when your mic is unplugged** — if you were deafened and then your mic dropped (USB headset yanked, sleep/resume), the speakers used to stay muted with no way to recover from the tray. MicMute now restores your speakers and clears the deafen state automatically when the mic disappears.
- **Hotkey dialog rejects bare letter keys** — "Type manually" will no longer accept a single letter like `a` as a hotkey (which would have hijacked that key in every app). At least one of Ctrl / Alt / Shift / Win is now required, except for F1–F24 which are safe to bind alone. Existing legacy configs (e.g. `Hotkey=Pause`) are auto-upgraded to `Ctrl+Shift+Pause` on first launch so nobody's custom hotkey silently dies during upgrade.
- **Startup/hotkey error notifications stay visible long enough to read** — pre-existing 3-second cap silently shortened every "Invalid hotkey" / "Microphone disconnected" message. Notifications now display for the duration the code asks for (typically 5 seconds for errors).
- **WinGet machine-scope installs detected correctly** — `winget install --scope machine` installs under `%ProgramFiles%` rather than user AppData; MicMute's winget-managed detection now recognizes both scopes so settings go to the right place.
- **Clean handoff when upgrading from 2.1.5** — the 2.1.5 → 2.1.6 upgrade briefly used a different single-instance lock namespace, which could have produced two tray icons for a moment during the transition. The new build waits for a running 2.1.5 to exit before taking over.
- **Deafen recovery is more conservative** — if restoring your speakers fails when a mic unplugs during deafen, MicMute now keeps the deafen state tracked instead of silently dropping it (so you can still un-deafen manually and the app knows what to restore).
- **Fast-user-switching works** — MicMute's single-instance lock is now per-user instead of machine-wide. Family PCs and terminal servers: each logged-in user gets their own tray app instead of the second user's launch silently failing.
- **Self-update verifies the checksum file origin** — the binary download already required a GitHub release URL; the accompanying `SHA256SUMS` file now gets the same check so both halves of the verify step are equally trusted.
- **Startup mute state always matches reality** — if Windows rejected the "start muted / start unmuted" request during launch (rare device-init race), the tray icon used to lie for up to 15 seconds. It now re-reads the actual mic state immediately.

### Changed
- **Update dialog respects high-DPI scaling** — the Update window now matches the Settings window in DPI handling so buttons stay aligned on 150%/200% displays.
- **Self-update size-caps every download** — GitHub JSON, the `SHA256SUMS` file, and `MicMute.exe` all have reasonable upper bounds now. A hypothetically compromised or misconfigured release can't fill your disk or exhaust memory.
- **On-screen display is crash-resistant** — if anything goes wrong while drawing the notification bubble, MicMute logs it and moves on instead of propagating the error.

## [2.1.5] - 2026-04-16

### Fixed
- **Crash logs capture the earliest startup errors** — the global error-handling and logging is now the very first thing MicMute does at launch, so the rare failure that happens in the first millisecond of startup still leaves a record in the log file instead of disappearing silently.

## [2.1.4] - 2026-04-16

### Fixed
- **Duplicate tray icons when launched twice** — a second launch now exits cleanly instead of spawning a second icon that fought with the first over the hotkey and settings file.
- **Punctuation-key hotkeys now work** — binds like Shift+`\`, Ctrl+`]`, `;`, `'`, `,`, `.`, `/`, `-`, `=`, `` ` `` register correctly. Previously these silently failed and dropped you into tray-only mode.
- **Keep your old hotkey if a new one won't register** — if you pick a combination Windows refuses, MicMute now rolls back to your previous working hotkey instead of leaving you with nothing.
- **Update integrity check is now enforced** — when a release ships with checksums, the downloaded update is verified before installing. Anything that can't be verified is rejected.
- **Push-to-talk no longer gets stuck unmuted** — re-binding the hotkey while holding the key now cleans up the poll instead of leaving the mic open.

### Added
- **Log file for troubleshooting** — MicMute keeps a small rolling log at `%LOCALAPPDATA%\MicMute\micmute.log` capturing errors, update activity, and startup events. Helpful if something misbehaves after a Windows update or device change.
- **Crash-resistant tray** — unexpected errors in background timers now get logged instead of taking the tray down.

### Changed
- **Release pipeline now publishes a `SHA256SUMS` asset** alongside `MicMute.exe` so the self-update flow can verify downloads.
- **GitHub Actions pinned to specific commits** so the release build is reproducible.

## [2.0.0] - 2026-03-18

### New Features
- **Complete C# rewrite** — ported from AutoHotkey v2 to C# .NET 8 WinForms for better maintainability and performance.
- All features from v1.8.3 preserved: toggle/PTT modes, deafen, mute lock, OSD, sound feedback, custom icons, device selection, hotkey customization, startup control.

### Code Quality
- No memory creep over long sessions.
- Snappier tray — no hitches when opening menus or flashing the icon on toggle.
- Click-through OSD overlay with Win11 rounded corners.
- Settings and Help windows never open twice if you click the menu rapidly.

### Removed
- AutoHotkey dependency — runs on .NET 8 runtime or as a standalone exe.
- Original AHK script moved to `legacy/` folder.

## [1.8.3] - 2026-03-13

### Fixed
- **Mute-lock no longer gets stuck in a toggle war** — the protection that stops other apps from fighting MicMute over your mic state now actually engages during the background sync and when you exit deafen mode.
- **Header version matches the real version** — the About text and the tooltip now agree on which build you're running.
- **Snappier startup and faster device menu** — initializing the mic and listing capture devices no longer does redundant work.
- **Menu never silently breaks if a device enumeration errors** — the tray stays responsive even if Windows returns an error while listing mics.
- **Settings file survives a sudden power loss** — preferences are written safely so an unexpected shutdown mid-save can't blank your config.
- **Reliability improvements to shutdown cleanup.**

### Added
- **Tray icon recovers after Explorer restarts** — if Explorer crashes or restarts, the MicMute icon comes back automatically instead of vanishing until you relaunch.

### Removed
- **Internal cleanup** — dead remnants from the old LED-sync feature removed.

## [1.8.1] - 2026-03-12

### Fixed
- **Help window hotkey text** — default hotkey shown as "Right-Alt + Comma" corrected to "Win+Shift+A"
- **OSD duration default** — initial value of `g_osdDuration` corrected from 800ms to 1500ms to match INI default, README, and Settings GUI
- **Help text OSD position** — description updated from "top-right corner" to "above the taskbar" (accurate to actual placement)
- **FINAL_REPORT filename casing** — corrected `micmute.ahk` reference to `MicMute.ahk`

## [1.8.0] - 2026-03-10

### Added
- **Help Window** — comprehensive in-app help accessible from Settings GUI. Covers all features: modes, deafen, hotkeys, settings, custom files, and troubleshooting. Resizable window with scrollable content.

### Fixed
- **Default hotkey restored** — default hotkey reverted to Win+Shift+A (`#+a`) as intended.
- **Unmute-on-exit bug** — fixed cleanup edge case from audit.
- **CHANGELOG gaps** — filled missing documentation from prior releases.

## [1.7.0] - 2026-03-10

### Changed
- **Removed scroll-to-volume** — mic volume scroll via mouse wheel over tray icon removed. It required a system-wide mouse hook, causing ~0.5% idle CPU overhead and interfering with normal scroll behaviour. Middle-click mode toggle is now handled via a zero-overhead tray notification callback instead.
- **Sync timer** — periodic mute-state sync interval increased from 3 s to 5 s (reduces background wakeups while remaining responsive).
- Version bumped to 1.7.0

## [1.6.0] - 2026-03-09

### Changed
- **ToolTip notifications** — all user-facing notifications now use floating ToolTip bubbles instead of Windows toast notifications (TrayTip). Non-intrusive and auto-dismiss.
- **Embedded icons** — .ico files are embedded as PE resources in the compiled .exe via `@Ahk2Exe-AddResource`. No external icon files needed for standalone use.
- **Icon fallback chain** — custom INI path → .ico on disk → embedded PE resource → Windows built-in icons. Works in all scenarios.
- **Settings GUI** — title bar shows version, GitHub button opens repo page.
- Version bumped to 1.6.0

### Added
- **Show Tray Icon** section in README — instructions for pinning MicMute to the Windows taskbar tray.

## [1.5.0] - 2026-03-09

### Added
- **Settings GUI** — full settings window (`Settings…` in tray menu) for all options: behavior, OSD, hotkeys, custom files. Replaces manual INI editing. Includes OK / Apply / Cancel buttons with ToolTip feedback on Apply.
- **Startup mute options** — 4-option dropdown: "Don't change", "Always muted", "Always unmuted", "Remember last". Persists mute state across restarts when set to "Remember last".
- **Mic volume scroll** — scroll wheel over tray icon adjusts microphone input volume in 5% steps, event-driven via tray hover detection.
- **Middle-click mode toggle** — middle-click tray icon to switch between Toggle and Push-to-Talk modes with a single distinct tone per mode (1175Hz for Toggle, 1568Hz for PTT).
- **Deafen hotkey capture** — Settings GUI uses a proper Hotkey capture control (press Alt+L and it shows the combo) plus a "WinKey…" popup button for manual Win key combo entry.
- **Browse/Clear buttons** — custom icon and sound file paths use Browse/Clear buttons with filename labels instead of raw Edit boxes.

### Fixed
- **Settings now actually save** — several options (especially the "start muted" preference) were silently failing to save. They persist correctly now.
- **No more crash on rapid middle-clicks** — middle-clicking the tray icon a few times in a row no longer crashes MicMute.
- **Middle-click works on the first try** — previously it only worked after you'd right-clicked the tray icon first.
- **Switching PTT to Toggle takes effect immediately** — the hotkey now switches cleanly instead of staying in PTT mode until restart.
- **Long device names no longer blow out the menu** — microphone names in the Mic Source submenu are truncated at 40 characters.

### Removed
- **LED sync (F-16)** — keyboard LED indicator feature removed entirely. Was unreliable and interfered with actual key function (CapsLock, ScrollLock, NumLock).
- **Hybrid mode (F-06)** — removed in favor of middle-click mode switching between Toggle and PTT.

### Changed
- Version bumped to 1.5.0
- Tray menu reorganized: Mode/Mic Source submenus, Settings item, separators
- Settings GUI layout tightened — less wasted space for file selectors
- Only Toggle and Push-to-Talk modes available (removed Hybrid and Push-to-Mute)

## [1.3.0] - 2026-03-08

### Added
- **F-02**: On-screen display — floating MUTED/ACTIVE overlay on toggle, click-through, configurable position and duration
- **F-04**: Custom sound files — replace default beep with .wav files via MuteSound/UnmuteSound config, with beep fallback
- **F-06**: Hybrid PTT/Toggle mode — short press (<300ms) toggles, long press activates push-to-talk, eliminates mode switching
- **F-10**: Unmute on exit — auto-unmutes mic in Cleanup() before releasing COM, prevents "dead mic" after quitting
- **F-11**: Mute lock — prevents external apps from changing mute state, with debounce to avoid infinite toggle war
- **F-13**: Live hotkey rebinding — "Change Hotkey..." dialog in tray menu, supports both standard and Win key combos
- **F-16**: Keyboard LED sync — sync ScrollLock/CapsLock/NumLock with mute state, saves and restores initial LED state
- **F-17**: Accessible icon colors — configurable .ico paths via IconMuted/IconActive for colorblind users
- **F-20**: Deafen mode — separate hotkey mutes mic + speakers simultaneously, restores speaker state on un-deafen

### Changed
- Version bumped to 1.3.0
- Tray menu now includes Change Hotkey, Mute Lock, On-Screen Display items
- Mode submenu now includes Hybrid (PTT/Toggle) option
- MicMute.ini now stores 12 additional config keys (all backward-compatible defaults)
- SoundBeep calls replaced with PlayFeedback() function supporting custom WAV files
- Header comment block updated with full feature list

## [1.2.0] - 2026-03-08

### Added
- **P2-01**: Push-to-talk / push-to-mute mode — hold hotkey to temporarily unmute (PTT) or mute (PTM), with 30s safety timeout
- **P2-02**: Audio device selector — tray submenu enumerates capture devices via COM IMMDeviceEnumerator, persists choice in INI
- **P4-03**: Tray icon flash on toggle — 3-cycle flash animation draws attention to mute state changes

### Changed
- Version bumped to 1.2.0
- Tray menu now includes Mode submenu (Toggle / Push-to-Talk / Push-to-Mute) and Microphone submenu (device picker)
- MicMute.ini now stores Mode and DeviceId settings
- InitMicEndpoint supports specific device ID via IMMDeviceEnumerator::GetDevice, falls back to system default
- ExtractKeyName strips all AHK prefix characters (~*$<> in addition to #^!+)
- FlashIcon restarts cleanly on overlapping toggles instead of dropping the second flash

## [1.1.0] - 2026-03-08

### Fixed
- **No crash at startup if no mic is connected** — MicMute now starts in a degraded state and lets you recover from the tray menu once you plug a mic in.
- **Tray icon no longer shows the wrong mute state** when a device has gone stale — the icon now accurately reflects your actual mic state.
- **Invalid hotkey in config no longer crashes at startup** — MicMute falls back to tray-only mode with an error message instead.
- Internal refactor for maintainability.

### Added
- **P1-01**: Auto-detect mic plug/unplug — periodic check reconnects automatically without user intervention
- **P1-02**: Periodic mute state sync — tray icon stays accurate when other apps change mic mute
- **P1-03**: TrayTip confirmation after successful mic reinitialisation
- **P1-05**: Version string (v1.1.0) displayed in tray menu and tooltip
- **P2-03**: Sound feedback — audible beep on toggle (low tone = muted, high tone = active), toggleable via tray menu
- **P2-04**: Run at Startup — tray menu toggle to create/remove Windows startup shortcut
- **P2-05**: Config file support — settings stored in MicMute.ini (auto-created when changed via tray menu)
- **P4-02**: Mute on lock — optional auto-mute when PC locks (Win+L), enable via MicMute.ini
- **P4-04**: Added note about HotkeyToReadable() duplication with MWBToggle

### Changed
- OnExit(Cleanup) moved before first COM call to prevent resource leaks
- Error dialogs now suggest Tray → Reinitialise Mic instead of crashing
- Tray tooltip shows version number and current mute state
- Tray menu expanded with Sound Feedback, Run at Startup, and version display

## [1.0.0] - 2026-03-06

### Added
- Initial release
- Global hotkey mute/unmute toggle (Win+Shift+A default)
- System tray icon (green = active, red = muted)
- Left-click tray to toggle, right-click for menu
- Manual mic reinitialisation via tray menu
- Custom icon support (mic_on.ico / mic_off.ico)
