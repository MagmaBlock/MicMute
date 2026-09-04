# MicMute (MagmaBlock fork)

[![GitHub Release](https://img.shields.io/github/v/release/MagmaBlock/MicMute)](https://github.com/MagmaBlock/MicMute/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6)](https://github.com/MagmaBlock/MicMute)
[![GitHub Downloads](https://img.shields.io/github/downloads/MagmaBlock/MicMute/total)](https://github.com/MagmaBlock/MicMute/releases)

Global hotkey microphone mute/unmute with push to talk for Windows.

A lightweight system tray utility that lets you mute and unmute your microphone from anywhere using a hotkey or tray icon click. Works at the Windows audio level — affects all apps at once (Zoom, Discord, Teams, etc.).

> [!NOTE]
> **This is a personal, self-maintained fork** of [itsnateai/MicMute](https://github.com/itsnateai/MicMute), kept for my own use. It adds a few features I wanted and tracks upstream occasionally. It is **not** the official distribution — for that, use the upstream repo and its WinGet package. This fork doesn't accept issue reports or pull requests.
>
> **Fork addition — mouse buttons as Push-to-Talk.** Bind XButton1/XButton2 (mouse "back"/"forward") or the middle button as your PTT key. It rides the same hookless `GetAsyncKeyState` polling path as keyboard PTT — no mouse hook, nothing new for game anti-cheat to flag. It works in most games but is **not 100% game-compatible**: mouse-only input paths (exclusive fullscreen, Raw Input/DirectInput, Steam Input or driver-level remaps like G HUB/Synapse) can consume side-button presses before Windows' global key state sees them. If a game eats the button, bind a keyboard key instead or read [How mouse buttons travel](#how-mouse-buttons-travel) below.

## Features

- **Global hotkey**: `Win + Shift + Ctrl + A` (configurable) toggles mic mute system-wide
- **Push-to-Talk mode**: Hold key to unmute, release to re-mute. Fullscreen-safe and accepts bare modifier keys (Right-Ctrl alone, etc.) the way Discord does — no keyboard hook, nothing for game anti-cheat to flag.
- **Mouse side buttons (fork)**: Bind XButton1/XButton2 (mouse "back"/"forward") or the middle button as your Push-to-Talk key — same hookless polling path, no mouse hook installed. Mouse buttons are Push-to-Talk only (Windows' global hotkey system never fires them, so Toggle/Deafen can't use them), and side-button presses still pass through to the focused app, so unbind them in-game if a game also uses them. If your mouse software (G HUB, Synapse) rewrites side buttons to keystrokes at the driver level, bind that key instead — MicMute won't see 0x05/0x06.
- **Sticky PTT**: Left-click the tray in Push-to-Talk mode to hold the mic open without holding the hotkey. A persistent "mic listening" bubble stays on screen so you can't forget. Click again to resume normal PTT.
- **Deafen mode**: Mute both mic and speakers simultaneously (separate hotkey)
- **Tray icon**: Green = active, Red = muted. Left-click to toggle.
- **On-screen display**: Floating dark bubble above the taskbar shows mute state
- **Mute Lock**: Reverts external mute changes on the next 15-second sync tick. Catches drive-by changes from meeting apps or OS sound settings. Not instant — apps that actively manage mute mid-call (Discord PTT, etc.) will win in the moment.
- **Mic source selection**: Pick which microphone to control
- **Sound feedback**: Audible tone on mute/unmute (custom .wav support)
- **Custom icons**: Replace default tray icons with your own .ico files
- **Run at startup**: One-click toggle via Settings
- **Startup state control**: Start muted, unmuted, or remember last session
- **Explorer restart recovery**: Tray icon survives Explorer crashes
- **Auto-detect**: Automatically reconnects when you plug in a new mic

## Screenshots

| Active (Unmuted) | Muted | Tray Menu | Settings |
|:---:|:---:|:---:|:---:|
| ![Active](screenshots/micicon1.png) | ![Muted](screenshots/micicon2.png) | ![Menu](screenshots/micmutemenu.png) | ![Settings](screenshots/micmutesettings.png) |

## Requirements

- Windows 10/11

## Installation

### Option 1: Download

Grab **[MicMute.exe](https://github.com/MagmaBlock/MicMute/releases/latest)** from this fork's releases — single file, self-contained, no .NET runtime needed.

> [!IMPORTANT]
> If you installed upstream via WinGet (`itsnateai.MicMute`), don't install this fork over it — upstream updates would overwrite the fork binary. Uninstall first (`winget uninstall itsnateai.MicMute`) or keep the portable exe separate.

### Build from source

```bash
git clone https://github.com/MagmaBlock/MicMute.git
cd MicMute

# Framework-dependent (~280KB, requires .NET 8 runtime)
dotnet publish -c Release -r win-x64

# Self-contained single-file (~147MB, no runtime needed) — matches the release exe
dotnet publish -c Release --self-contained true -r win-x64 -p:PublishSingleFile=true
```

Output: `bin/Release/net8.0-windows/win-x64/publish/MicMute.exe`

### Self-update integrity

Releases publish a `SHA256SUMS` file alongside the exe. The in-app **Update** button downloads it, verifies the hash, and fails closed if anything is missing or doesn't match. Unverified updates never land on disk.

## Usage

### Modes

**Toggle** (default): Press the hotkey to mute, press again to unmute. Left-click the tray for the same effect.

**Push-to-Talk**: Hold the hotkey to unmute. Release to re-mute. Switch via tray menu or middle-click the icon. Push-to-Talk always starts muted at launch.

**Sticky PTT**: In Push-to-Talk mode, left-click the tray icon to unmute and pause the hotkey. A persistent indicator stays on screen until you left-click again to re-mute. Useful for holding the mic open during a long conversation without holding a key.

**Deafen**: Assign a separate hotkey in Settings. Mutes both mic and speakers. Press again to restore both.

### Tray Menu

Right-click the tray icon for the full menu:
- Toggle mute
- Current hotkey combo (click to change — combo shorthand: `#` Win, `^` Ctrl, `!` Alt, `+` Shift)
- Switch between Toggle and Push-to-Talk modes
- Select mic source
- Open Settings, Help, Sound Settings
- Reinitialise mic (if device changed)

## Customization

Settings are stored in `MicMute.ini` (auto-created next to the exe):

```ini
[General]
Hotkey=#^+a
SoundFeedback=0
Mode=toggle
OSD_Enabled=0
OSD_Duration=800
MuteLock=0
MiddleClickToggle=1
StartMuted=no
DeafenHotkey=
```

| Key | Default | Description |
|-----|---------|-------------|
| `Hotkey` | `#^+a` | Main mute hotkey (`#`=Win, `^`=Ctrl, `!`=Alt, `+`=Shift) |
| `SoundFeedback` | `0` | Play tone on mute/unmute |
| `Mode` | `toggle` | `toggle` or `push-to-talk` |
| `OSD_Enabled` | `0` | Show on-screen mute indicator |
| `OSD_Duration` | `800` | OSD display time in ms |
| `MuteLock` | `0` | Revert external mute changes on the 15s sync tick |
| `MiddleClickToggle` | `1` | Middle-click tray to switch modes |
| `StartMuted` | `no` | `no`, `yes`, `unmuted`, or `last` |
| `DeafenHotkey` | *(empty)* | Hotkey for deafen mode |
| `DeviceId` | *(empty)* | Specific mic device (empty = system default) |
| `IconMuted` / `IconActive` | *(empty)* | Custom .ico file paths |
| `MuteSound` / `UnmuteSound` | *(empty)* | Custom .wav file paths |

## How mouse buttons travel

Mouse buttons become "visible" to MicMute the same way keyboard keys do: through Windows' global async key state, which MicMute polls hooklessly via `GetAsyncKeyState`. A game or driver can break that chain in a few ways:

| Scenario | Why the side button stops working | Fix |
|---|---|---|
| Driver-level remap (G HUB, Synapse, Steam Input) | The button never reaches Windows as 0x05/0x06 — it becomes a keystroke or controller input | Bind the remapped key, or restore default button behavior in the driver software |
| Exclusive fullscreen / Raw Input / DirectInput | The game reads mouse input straight from the device and the global key state table doesn't update | Use borderless windowed mode, or bind a keyboard key for that game |
| Remote desktop / streaming session | MicMute polls the **local** machine's key state — it can't see the remote end | Run MicMute on the machine where the game actually runs |
| Admin-elevated games (rare) | Not a permission problem — `GetAsyncKeyState` is UIPI-independent | See the row above; usually the culprit is Raw Input, not elevation |

Practical tip: if the side button works on the desktop but not inside a game, the game is consuming it — check driver software first, then window mode.

## Project Structure

| Path | Description |
|------|-------------|
| `MicMute.csproj` | .NET 8 project file |
| `Program.cs` | Entry point — single-instance enforcement |
| `TrayApp.cs` | Main app — tray icon, hotkeys, mute logic, menus |
| `AudioManager.cs` | Core Audio COM interop — mute, enumerate, speaker control |
| `Config.cs` | INI config reader/writer with hotkey parsing |
| `OsdForm.cs` | On-screen display overlay (click-through, auto-dismiss) |
| `SettingsDialog.cs` | Settings GUI — includes inline hotkey capture |
| `HelpWindow.cs` | Help text window |
| `NativeMethods.cs` | Win32 P/Invoke declarations |
| `ShortcutHelper.cs` | Windows .lnk shortcut creation for startup |
| `mic_on.ico` / `mic_off.ico` | Tray icons (embedded as resources) |

## Supporting This Project

This fork is a personal project and does not accept donations. If MicMute helps you, **support the upstream author instead**:

- **[Buy Me a Coffee (upstream author)](https://buymeacoffee.com/itsnate)** — one-time support

---

## License

[MIT](LICENSE) — same as upstream.
