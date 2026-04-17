# Changelog

*LTR — Long-Term Release · one-click self-update built in.*

All notable changes to MicMute are documented here.

## [2.1.6] - 2026-04-16

### Fixed
- **Push-to-talk stays muted when you change your hotkey or swap modes mid-hold** — the 2.1.4 fix for stuck-unmuted PTT missed the rebind path. If you ever held your PTT key and then picked a new hotkey from the tray menu, your mic stayed open until the next toggle. Same was true when middle-clicking the tray to swap between Toggle and Push-to-Talk while holding the key. Both paths now properly re-mute.
- **Self-update verifies the checksum file origin** — the binary download already required a GitHub release URL; the accompanying `SHA256SUMS` file now gets the same check so both halves of the verify step are equally trusted.

### Changed
- **Update dialog respects high-DPI scaling** — the Update window now matches the Settings window in DPI handling so buttons stay aligned on 150%/200% displays.

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
