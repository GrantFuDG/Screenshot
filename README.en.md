# ScreenshotManager

A lightweight screenshot and pin-to-desktop tool for Windows. It runs as a single tray-resident process: a hotkey brings up a capture overlay, and from there you can annotate, copy, save — or pin the image onto your desktop.
Built with WPF on .NET 8.

[简体中文](README.md) | **English**

## Features

- **Region capture**: press `F1` for the overlay, then drag to select a region — multiple monitors are supported.
  Without dragging, a single **click** selects the window under the cursor, and a **double click** inside the selection confirms it.
- **Full-screen capture**: `Ctrl+F1` captures the monitor under the cursor and saves / copies it according to your settings.
- **Hover window detection**: whatever window the cursor rests on gets highlighted (the desktop, the taskbar and transparent overlays are ignored).
- **Annotations**: rectangle, ellipse, line, arrow, pen, highlighter, text, mosaic, blur and eraser, with adjustable color and thickness, plus undo / redo.
- **Magnifier color picker**: while capturing, the pixels and color under the cursor are shown magnified; the mouse wheel changes the zoom (1.5×–12×).
- **Output**: copy to the clipboard, or save as png / jpg / bmp with a configurable file-name template.
- **Pin to desktop**: `F3` pins the clipboard image on the desktop (resizable, adjustable opacity); `Shift+F3` pastes the clipboard image straight into the active window.
- **Capture history**: `Ctrl+Shift+H` shows a thumbnail list where you can copy / pin / open / reveal an item; the item count and retention can be limited.
- **Tray resident**: hotkeys, save folder, history policy, start-with-Windows, DPI and notification options all live in the settings window and take effect immediately — no restart needed.

## Hotkeys

| Key | Action |
| --- | --- |
| `F1` | Region capture |
| `Ctrl+F1` | Full-screen capture (current monitor) |
| `F3` | Pin the clipboard image onto the desktop |
| `Shift+F3` | Paste the clipboard image into the active window |
| `Ctrl+Shift+H` | Capture history |
| drag / click / double click | Draw a selection / select the window under the cursor / confirm the current selection |
| `Shift` + drag | Square selection |
| Arrow keys | Nudge the selection |
| `Enter` | Confirm (copy and save, per settings) |
| `Esc` | Step by step: drop the annotation in progress → clear the selection → leave the overlay |
| Right click | Leave the overlay in **one** step (while typing text it ends the text first) |
| `Ctrl+C` / `Ctrl+S` / `Ctrl+T` | Copy / Save / Pin |
| `Ctrl+Z` / `Ctrl+Y` / `Ctrl+A` | Undo / Redo / Select all |
| `R` `E` `L` `A` `P` `H` `T` `M` `B` `X` | Switch annotation tool (rectangle / ellipse / line / arrow / pen / highlighter / text / mosaic / blur / eraser) |
| Mouse wheel | Change the magnifier zoom |

All global hotkeys can be changed under **Settings → Hotkeys**.

## Requirements

- Windows 10 / 11 (x64)
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (`Microsoft.WindowsDesktop.App 8.0`)
- The .NET 8 SDK to build from source

## Build and run

```powershell
# Debug build
dotnet build "src\ScreenshotManager\ScreenshotManager.csproj" -c Debug

# Framework-dependent publish (the target machine needs the .NET 8 Desktop Runtime)
dotnet publish "src\ScreenshotManager\ScreenshotManager.csproj" -c Release -o dist

# Self-contained publish (no .NET installation required on the target machine)
dotnet publish "src\ScreenshotManager\ScreenshotManager.csproj" -c Release -r win-x64 --self-contained true -o dist
```

Run `dist\ScreenshotManager.exe`. It is a tray application with **no main window**, so look for its icon in the notification area of the taskbar.

> If the program runs somewhere that restricts writes (for example `%APPDATA%` is not writable), it automatically falls back to a portable mode that stores its data in the `data\` folder next to the executable.

## Data and privacy

- **Everything happens locally: the program does not connect to the network and never uploads anything.**
- Settings, history, thumbnails and the log live in `%APPDATA%\ScreenshotManager\` by default:
  `settings.json`, `history.json`, `thumbs\`, `screenshotmanager.log`.
- If that folder is not writable, the app falls back to `data\` next to the executable (portable mode) and says so in the log.
- To clean up completely, just delete that folder (or the `data\` folder).

## Project layout

```
src/ScreenshotManager/
├─ Program.cs                  Entry point: global exception handling, startup log, self-test switch
├─ AppHost.cs                  Wiring: tray / hotkeys / capture flow / saving / history / settings window
├─ ScreenshotManager.csproj
├─ app.manifest                Run as invoker, PerMonitorV2 DPI awareness
├─ Models/                     AppSettings, ScreenshotItem, Annotations (geometry and annotation model)
├─ Services/                   Screen capture, clipboard, image composition, history, hotkeys, icons, settings, autostart, tray, logging
├─ Interop/                    Win32 P/Invoke: window enumeration, DPI, hotkeys, clipboard, cursor
└─ Views/                      Capture overlay, annotation toolbar, pin window, history window, settings window
```

## Known limitations

- Windows only (WPF + Win32).
- A few "invisible overlays" (from games, screen recorders, streaming tools or GPU drivers) can interfere with hover window detection; the app already filters out transparent click-through windows, the desktop window and windows covering the whole screen.
- Mixed-DPI multi-monitor setups are handled with PerMonitorV2; unusual combinations may still show small coordinate offsets.
- Global hotkeys rely on the system `RegisterHotKey` call; if another program already owns a hotkey, registration fails and the log plus the settings window will tell you.

## Authorship

- **Requirements, design trade-offs and acceptance**: GrantFuDG
- **All source code**: written, debugged and fixed by the AI assistant inside DeepSeek Harness (model `deepseek-v4.1-flash`) from those requirements, verified item by item on a real machine, and accepted by GrantFuDG

The commit message credits this AI collaborator with a `Co-authored-by` trailer. GitHub only renders an avatar and a profile link when the address is attached to an account; this one has no account, so it appears as plain text.

## Disclaimer

- This is a personal / learning open-source project provided "as is", **without warranty of any kind**, express or implied. Any direct or indirect damage caused by using this software (data loss, mistakes in operation, privacy leaks, and so on) is the responsibility of the user.
- The software never uploads any data on its own; however, **screenshots may themselves contain private or sensitive information**, so where they are stored, whether they are shared and how they are distributed is entirely up to the user.
- All UI icons are drawn in code (see `Services/IconFactory.cs`) and system fonts are used, so the repository contains no third-party asset files. Any other product names mentioned are for description or comparison only, and the respective names and trademarks belong to their owners.

## License

[MIT](LICENSE)