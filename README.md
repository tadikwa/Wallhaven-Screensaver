# Wallhaven Screensaver

Wallhaven Screensaver is a native Windows screen saver powered by the public **SFW** Wallhaven API.
It rotates high-resolution artwork automatically, adapts Wallhaven searches to the display resolution/aspect ratio, and keeps a local cache plus anti-repeat history.

> This project is not affiliated with or endorsed by Wallhaven.

> **Status:** `0.1.0` preview, tested successfully as a Windows screen saver.

## Configuration preview

![Wallhaven Screensaver options](assets/screenshots/wallhaven-screensaver-options.png)

### Default options

| Setting | Default |
| --- | --- |
| Selection | Random |
| Category | All |
| Rotation | 1 minute |
| Transition | 750 ms fade |
| Scaling | Fill / crop |
| Multi-monitor | Same image on all displays |
| Display-aware API filtering | Enabled |
| Offline cache fallback | Enabled |
| Cache limit | 50 images / 500 MiB |
| Anti-repeat history | 1,000 Wallhaven IDs |

## Features

- Native Windows screen saver modes: `/s`, `/c` and `/p <HWND>`
- Random, trending, popular and newest Wallhaven selections
- General, Anime, People or All categories
- **SFW-only** requests (`purity=100`)
- Display-aware resolution and aspect-ratio filtering
- 1 to 120 minute rotation interval
- Optional fade transitions
- Fill/crop or fit/letterbox rendering
- Multi-monitor support
- Persistent anti-repeat history
- Bounded local image cache with offline fallback
- Seven-day rotating text logs
- No project telemetry or analytics

## Runtime data

Per-user data is stored under:

```text
%LOCALAPPDATA%\WallhavenScreensaver
├── settings.json
├── history.json
├── cache\
└── logs\
```

## Install

Download the current Windows x64 ZIP from the GitHub Releases page, extract it, then run:

```powershell
.\Install.ps1
```

The installer copies `WallhavenScreensaver.scr` to the current user's local application data and selects it as the current screen saver. Administrator rights are not required for the normal installation path.

To open the configuration window or start it manually:

```powershell
.\WallhavenScreensaver.scr /c
.\WallhavenScreensaver.scr /s
```

## Build from source

Requirements:

- Windows 10/11 x64
- .NET 10 SDK

Publish the self-contained Windows x64 executable directly from the source project:

```powershell
dotnet publish .\src\WallhavenScreensaver\WallhavenScreensaver.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:PublishTrimmed=false `
  -o .\build

Copy-Item .\build\WallhavenScreensaver.exe .\WallhavenScreensaver.scr
```

A Windows `.scr` file is an executable using the screen-saver extension; the application implements the standard screen-saver command modes directly.

## Privacy

The runtime contacts Wallhaven only for image search and image downloads. It does not include project telemetry, analytics, advertising or tracking endpoints.

## Release signing

The `0.1.0` preview is unsigned. Verify the SHA-256 file provided with the release before use.

## License

MIT. See [LICENSE](LICENSE).