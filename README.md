# Easy Notch

A notch at the top of your Windows screen. Closed, it shows the time, what's playing
(with a small equalizer) or your focus countdown. Click it to open; scroll over it,
or click the dots on the right, to move between pages. Click outside, the handle at
the top, or press Esc to close it.

## Pages

| | |
|---|---|
| **Clock** | Big time and date, plus a list of places (London, New York and Tokyo to start). Click a place to show its time big. |
| **Weather** | Current temperature, conditions, feels-like, today's high/low and wind, plus the next seven hours and the next six days together, each with rain chance. Click the place name to change it; it starts at your time-zone city. The closed notch shows the temperature next to the time. |
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

## Build and run

Needs the .NET 10 SDK.

```powershell
dotnet run -c Release
```

Single self-contained `.exe` (runs without .NET installed):

```powershell
dotnet publish -c Release -o out
```

`EasyNotch.exe --open <clock|media|focus|notes|clipboard|mixer|settings>` starts with that page open.

## Not built yet

- **Notifications inbox**: reading Windows notifications needs the app to be packaged
  (MSIX) with the `userNotificationListener` capability.
- **Claude Code status page**: would read session state written by Claude Code hooks.
- **Output device switching** and **per-monitor notches** (the notch lives on the main screen).
