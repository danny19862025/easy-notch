# Easy Notch

A small island at the top of your Windows screen. It shows the time, the song
that's playing, or your focus countdown. Click it to open the widgets; click
anywhere else (or press Esc) to tuck it back.

## Widgets

| | |
|---|---|
| **Now Playing** | Whatever app is playing (Spotify, browsers, Media Player…): artwork, play/pause, skip, seek. |
| **Focus Timer** | Focus/break cycles (25/5 by default, adjustable) with optional white, pink or brown noise generated on the fly. Pops open when a phase ends. |
| **Clipboard** | Text and images you copy, searchable, with pinning. Click to copy again. Skips anything an app marks as private (password managers). |
| **Notes** | A scratchpad that saves as you type. |
| **Clock** | Local time plus any places you add, with the hour difference. |
| **Volume Mixer** | Output and microphone levels, plus a slider per app that's playing sound (with its icon). Click the icon to mute; scroll over a row to nudge it. |
| **Settings** | Turn widgets on/off and reorder them, start with Windows, tuck away over full-screen apps. |

The island stays out of the way: it's a tool window (not in Alt+Tab or the taskbar),
its transparent area passes clicks through, animations follow Windows' "Animation
effects" setting, and over a full-screen app it shrinks to a thin line until you hover it.
The tray icon has Open, Settings and Quit.

## Your data

Everything is stored only in `%LOCALAPPDATA%\EasyNotch` (settings, notes, clipboard history)
and never leaves your PC. Easy Notch has no network code at all.

## Mixer notes

Per-app volume uses Windows Core Audio. Calls run on a background (MTA) thread because the
session objects don't answer from WPF's UI thread. Some Windows builds (seen on 26300) no longer
expose the usual per-app control (`ISimpleAudioVolume`); there Easy Notch uses the per-channel control
(`IChannelAudioVolume`), which has no mute switch, so muting saves the level and restores it on
unmute. On those builds a mute from Easy Notch shows in Windows' own mixer as volume 0.

## Build and run

Needs the .NET 10 SDK.

```powershell
dotnet run -c Release
```

Single self-contained `.exe` (runs without .NET installed):

```powershell
dotnet publish -c Release -o out
```

`EasyNotch.exe --open <media|focus|clipboard|notes|clock|settings>` starts with that widget open.

## Not built yet

- **Notifications inbox**: reading Windows notifications needs the app to be packaged
  (MSIX) with the `userNotificationListener` capability.
- **Grey-screen Zen mode** and **per-monitor islands** (the island lives on the main screen).
