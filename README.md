# Dramatic Adhan

A Windows desktop app that loudly and visually announces Islamic prayer times using real location data, background imagery, and WAV audio — then hides itself in the system tray like a polite menace.

This is not a simple clock. It is a **dramatic event scheduler** for prayer times.

## What This App Actually Does

Dramatic Adhan:
- Automatically detects your **location via IP** (city, country, latitude, longitude).
- Fetches **accurate prayer times** from the AlAdhan API.
- Tracks the **next upcoming prayer** in real time.
- Shows a live **countdown timer** (hours, minutes, seconds).
- Triggers a **visual + audio warning window** when prayer time hits.
- Runs quietly in the **system tray** instead of hogging your screen.

If you close it, it doesn’t quit — it hides. On purpose.

## Features

- Automatic location detection (IP-based).
- Manual city/country override.
- Background refresh of prayer times every 6 hours.
- System tray integration with Show / Exit options.
- Dramatic warning screen with:
  - Random background images (`.png`, `.jpg`)
  - Random audio (`.wav`)
- Secret debug shortcut:  
  **Ctrl + Shift + D** → force the warning screen.
- Escape key minimizes instead of exiting (unless you explicitly quit).

## Requirements

- Windows
- .NET (whatever version this project targets — check the `.csproj`)
- Internet connection (for location + prayer time API)
- Speakers (unless you enjoy silent drama)

## Assets Folder

Place your assets here:
/assets
├── *.png / *.jpg (background images)
└── *.wav (audio for the warning)


If this folder is empty, the app still runs — it’s just less dramatic.

## How It Works (Short Version)

- Location is detected using `ip-api.com`.
- Prayer times come from `api.aladhan.com`.
- Times are refreshed automatically and cached in memory.
- A 1-second UI timer updates the countdown.
- When the countdown hits zero → **WarningForm appears**.

No background Windows services. No registry junk. Just a tray app doing its job.

## Controls

- Minimize window → app goes to tray
- Tray icon double-click → restore
- Tray menu → Exit (this actually exits)
- `Ctrl + Shift + D` → test the dramatic warning
- `Esc` → minimize (not quit)

## What This Is NOT

- Not a full prayer calculator.
- Not mobile-friendly.
- Not optimized for low-end machines.
- Not subtle.

It’s intentionally theatrical.

## License

CC0-1.0  
Do whatever you want. Break it, remix it, ship it, or delete it.

---

Built because normal Adhan apps aren’t dramatic enough.
