# Easy Notch

A notch at the top of your screen, for Windows and Linux. Closed, it shows the time, what's playing
(with a small equalizer) or your focus countdown. Click it to open; scroll over it,
or click the dots on the right, to move between pages. Click outside, the handle at
the top, or press Esc to close it.

## Install

Downloads are on the [releases page](https://github.com/danny19862025/easy-notch/releases/latest).

### Windows

Needs Windows 10 (version 1809) or later, 64-bit.

1. Download **EasyNotch.exe**. It's a single file and doesn't need .NET installed.
2. Move it somewhere permanent, for example `Documents\Easy Notch`. Windows starts it from that spot
   each time you sign in, so don't leave it in Downloads if you clean that folder.
3. Double-click it. The notch appears at the top of your main screen, and an icon appears in the tray
   (click the **^** arrow on the taskbar if it's hidden).
4. Windows SmartScreen may warn because the file isn't code-signed: choose **More info → Run anyway**.

Easy Notch starts with Windows from then on. Switch that off in Settings → **Start with Windows**.

- **Update:** right-click the tray icon → **Quit**, replace `EasyNotch.exe` with the new one, and run it.
  Your notes and settings are kept.
- **Uninstall:** turn off **Start with Windows** in Settings, quit from the tray icon, and delete
  `EasyNotch.exe`. To also delete your notes, clipboard history and settings, delete
  `%LOCALAPPDATA%\EasyNotch` (paste that into File Explorer's address bar).

### Linux

Needs a 64-bit desktop running X11, or Wayland with XWayland (installed by default on KDE Plasma and GNOME).
No root is needed for Easy Notch itself; only the optional packages below use `sudo`.

1. Install the packages for your distribution (see below).
2. Download **EasyNotch-linux-x64.tar.gz** and install it from a terminal:

   ```sh
   cd ~/Downloads
   tar -xzf EasyNotch-linux-x64.tar.gz
   ./EasyNotch-linux-x64/install.sh
   ```

   This installs to `~/.local/opt/easy-notch`, adds **Easy Notch** to your app menu, makes it start when
   you log in, and starts it. Use `./install.sh --no-autostart` to skip starting at login, or
   `--no-start` to install without starting it now.
3. Run `easy-notch` from a terminal or open it from the app menu any time. If `easy-notch` isn't found,
   add `~/.local/bin` to your `PATH` (most distributions already do).

- **Update:** download the new archive and run its `install.sh` again. It closes the running copy,
  replaces it, and keeps your notes and settings.
- **Uninstall:** `~/.local/opt/easy-notch/uninstall.sh`. Add `--purge` to also delete your notes,
  clipboard history and settings (`~/.local/share/easy-notch`).

#### Packages by distribution

Easy Notch runs without these, but the **Volume Mixer** and **Focus noise** need `pactl` and `pacat`
(PulseAudio's tools, which also control PipeWire). Most desktops include them already; to check, run
`pactl info`. On GNOME, the tray icon needs the AppIndicator extension.

| Distribution | Command |
|---|---|
| **Ubuntu, Linux Mint, Pop!_OS, Zorin, elementary, Debian** | `sudo apt install pulseaudio-utils` |
| **Fedora** (Workstation, KDE) | `sudo dnf install pulseaudio-utils` |
| **Arch, Manjaro, EndeavourOS, CachyOS** | `sudo pacman -S --needed libpulse` |
| **openSUSE** (Tumbleweed, Leap) | `sudo zypper install pulseaudio-utils` |

Notes for each desktop:

- **KDE Plasma** (any distribution): everything works out of the box: tray icon, on every virtual desktop,
  above other windows. This is where the Linux version was tested (Plasma 6, Wayland).
- **GNOME** (Ubuntu, Fedora Workstation, Debian): the tray icon needs the AppIndicator extension. Ubuntu
  ships it enabled. On Fedora run `sudo dnf install gnome-shell-extension-appindicator`, on Debian
  `sudo apt install gnome-shell-extension-appindicator`, on Arch
  `sudo pacman -S gnome-shell-extension-appindicator`, then log out and back in and turn it on in the
  **Extensions** app. Without it, open Easy Notch from the app menu or with `easy-notch --open settings`.
- **Cinnamon** (Linux Mint) and **XFCE** (4.16 or later): the tray icon shows in the panel's system tray.
- **Tiling compositors** (Sway, Hyprland, i3): add a rule to float the window whose class is `easy-notch`,
  or it gets tiled like a normal window.
- **Immutable systems** (Fedora Silverblue/Kinoite, Bazzite): `install.sh` only writes to your
  home folder, so it works as is. Install `pulseaudio-utils` with `rpm-ostree install` if `pactl` is missing.

## Pages

| | |
|---|---|
| **Clock** | Big time and date, plus a list of places (London, New York and Tokyo to start). Click a place to show its time big. |
| **Weather** | Current temperature, conditions, feels-like, today's high/low and wind, plus the next seven hours and the next six days together, each with rain chance. Click the place name to change it; it starts at your time-zone city. Click the C/F next to the temperature (or use Settings) to switch between °C and °F. The closed notch shows the temperature next to the time. |
| **Now Playing** | Artwork, title, seek bar with time remaining, shuffle, previous / play / next, repeat. Works with any app that reports to Windows (Spotify, browsers, Media Player…). |
| **Focus** | Pick minutes, then start. *Pomodoro* cycles focus and breaks; *Zen* is one session. Optional white, pink or brown noise, generated on the fly. |
| **Notes** | Note cards with search and pinning. Lines starting with `[ ]` become checkboxes you can tick right on the card. |
| **Clipboard** | Cards for text, links, images and files you copy, with search, pinning and a pause switch. Click to copy again; right-click to open a link, show a file, pin or delete. Skips anything apps mark as private (password managers). |
| **Volume Mixer** | Master knob (drag or scroll), a fader per app with its icon (click the icon to mute), speaker and mic mute, and the output device name. |
| **About** | Version, a link to the GitHub repo, and *Check for updates* (also checked automatically once a day; it only tells you and links to the release page, it never downloads anything). |

Settings open in their own window (gear buttons, the About page, or the tray icon): turn pages on/off and reorder them, add clock places (any city worldwide), start with Windows, tuck away over full-screen apps, automatic update checks.

The notch stays out of the way: it's a tool window (not in Alt+Tab or the taskbar),
its transparent area passes clicks through, animations follow Windows' "Animation
effects" setting, and over a full-screen app it shrinks to a thin line until you hover it.
The tray icon has Open, Settings and Quit.

## Your data

Everything is stored only in `%LOCALAPPDATA%\EasyNotch` (settings, notes, clipboard history)
and never leaves your PC. Network use is limited to:

- **Weather**: the chosen place's name (when searching) and coordinates go to
  [Open-Meteo](https://open-meteo.com) (free, no account or key), at most every 20 minutes.
  The last forecast is cached in `weather.json` for offline use.
- **Clock place search**: the typed city name goes to Open-Meteo's geocoding service.
- **Update check**: GitHub's public releases API for danny19862025/easy-notch, at most once a day.
- Album art and images that apps hand to Windows.

Easy Notch starts with Windows by default (set once on first launch; switch it off in
Settings and it stays off).

## Mixer notes

Per-app volume uses Windows Core Audio. Calls run on a background (MTA) thread because the
session objects don't answer from WPF's UI thread. Some Windows builds (seen on 26300) no longer
expose the usual per-app control (`ISimpleAudioVolume`); there Easy Notch uses the per-channel
control (`IChannelAudioVolume`), which has no mute switch, so muting saves the level and restores
it on unmute. On those builds a mute from Easy Notch shows in Windows' own mixer as volume 0.

## Build and run (Windows)

Needs the .NET 10 SDK.

```powershell
dotnet run -c Release
```

Single self-contained `.exe` (runs without .NET installed):

```powershell
dotnet publish -c Release -o out
```

`EasyNotch.exe --open <clock|media|focus|notes|clipboard|mixer|settings>` starts with that page open.

## Linux details

The Linux version has the same pages, settings and look, built with [Avalonia](https://avaloniaui.net).
It runs through X11 or XWayland, so it works on Wayland desktops too. It was tested on KDE Plasma 6 (Wayland).

See [Install → Linux](#linux) for setup on each distribution.

### What it uses

| | |
|---|---|
| **Now Playing** | MPRIS, the D-Bus interface Linux players share (Spotify, Firefox, Chrome, VLC, Elisa, mpv with mpv-mpris…). |
| **Volume Mixer** | `pactl`, for PulseAudio or PipeWire (`pipewire-pulse`). Per-app faders show each app's icon when its theme has a PNG. |
| **Focus noise** | Streamed to the sound server with `pacat` (or `pw-cat`). The end-of-phase chime is the freedesktop "complete" sound. |
| **Clipboard** | Notices copies through XWayland/X11. Skips anything password managers mark as secret (`x-kde-passwordManagerHint`, which KeePassXC and others set). |
| **Full-screen tuck** | Works for X11/XWayland apps (most games). On Wayland, KDE and GNOME already show an active full-screen window above the notch. |
| **Tray icon** | StatusNotifierItem (KDE, and GNOME with the AppIndicator extension). |

Data lives in `~/.local/share/easy-notch` (same files as on Windows). Network use is the same as on Windows,
plus album art: when a player gives a web address for its artwork (Spotify does), Easy Notch downloads that image.
Weather units follow your system locale, as on Windows (`en_US` shows °F).

`easy-notch --open <clock|weather|media|focus|notes|clipboard|mixer|about|settings>` opens a page; if Easy Notch is
already running, the running copy opens it.

### Build

Needs the .NET 10 SDK.

```sh
dotnet run --project linux
linux/publish.sh            # artifacts/EasyNotch-linux-x64.tar.gz (self-contained, no .NET needed)
```

Notes, Weather and update checks share their code with the Windows app (`Services/`); the window,
pages and Linux services live in `linux/`.

## Not built yet

- **Notifications inbox**: reading Windows notifications needs the app to be packaged
  (MSIX) with the `userNotificationListener` capability.
- **Claude Code status page**: would read session state written by Claude Code hooks.
- **Output device switching** and **per-monitor notches** (the notch lives on the main screen).
