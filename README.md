<div align="center">

<img src="img/logo.png" alt="XenUpdate Logo" width="110" />

# XenUpdate

**One place for every Windows update.**

Windows scatters its updates across a dozen tools — the Microsoft Store, `winget`, Windows Update, Device Manager, GPU driver pages, BIOS download portals. XenUpdate pulls all of it into a single, clean interface. It also goes further: for updates that can't be automated (GPU drivers, BIOS, firmware), it detects your exact hardware and walks you through the process step by step.

[![.NET 8](https://img.shields.io/badge/.NET-8.0-blueviolet.svg)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/UI-WPF-blue.svg)]()
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

</div>

---

## Download

Grab the latest release from the [Releases page](../../releases/latest).

**XenUpdate ships self-contained** — the .NET 8 runtime is bundled inside the zip. Nothing to install. Unzip and run.

> **Run as Administrator** is recommended. Some operations (driver installs, Windows Updates, System Restore points) require elevated privileges.

### Windows SmartScreen

The first time you launch XenUpdate, SmartScreen may show a "Windows protected your PC" warning. This is normal for new open-source apps that haven't built up a download history yet.
Click **"More info" → "Run anyway"** to proceed. The full source code is available here if you want to verify anything.

---

## What it does

| Feature | Details |
|---|---|
| **App updates** | Scans via `winget`, shows live download sizes, updates selected apps |
| **Windows Updates** | Lists pending OS updates via the Windows Update API, installs selected |
| **Driver updates** | Scans via Windows Update for pending driver packages |
| **Python package updates** | Scans installed `pip` packages and updates selected ones |
| **Update Queue** | A Steam-download-queue-style window: shows every update from every source in one flat, live-progress list — freely drag-reorder which installs next, even across sources, and reach it anytime via the always-visible floating button |
| **Guided Advisor** | Detects your GPU/CPU, checks whether a newer driver actually exists (live NVIDIA check), and walks you through manual updates (GPU, Visual Studio) step by step. A separate "Problem fixes" shelf covers issues that can follow an update (e.g. a driver clean-reinstall) — kept clearly apart from the update guides so it never reads as a pending task on a healthy machine |
| **Scan All / Select All / Update All** | One button each to scan every source, select everything found, and install it all — in whatever order you leave the Update Queue in |
| **System Restore** | Creates a restore point automatically before driver/system installs |
| **Offline awareness** | Dims the UI and shows a badge in the title bar when the network is down |
| **Blacklist / Whitelist** | Hide packages you never want to see, or pre-approve ones to auto-update the moment you're online |
| **Tray mode** | Minimize to system tray; background checks run silently |
| **Drop-in languages** | Add a new locale by dropping a translated `xx.json` into `Assets/Locales` — no rebuild needed. English/Turkish string and guide-catalog parity is enforced by an automated test |
| **Custom theming** | A glassmorphism UI with a built-in color picker (drag a saturation/hue square, no OS dialog) — pick any Primary/Secondary/Background trio, save individual colors or whole themes for later, and optionally set a blurred background photo with an adjustable cursor spotlight |
| **Light / Dark theme** | Auto-syncs to whatever background color you pick, or toggle manually from Settings |
| **Self-updating** | Checks its own GitHub releases and notifies you when a new version is out |

---

## Architecture

XenUpdate is a WPF/.NET 8 app built on a clean layered MVVM architecture.

**Tech stack**
- C# / .NET 8 · WPF
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM, commands, messaging
- [MaterialDesignInXamlToolkit](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) — UI components
- [H.NotifyIcon.Wpf](https://github.com/HavenDV/H.NotifyIcon) — system tray
- [gong-wpf-dragdrop](https://github.com/punker76/gong-wpf-dragdrop) — Update Queue drag-and-drop reordering
- WUApiLib (COM) — Windows Update API
- `winget` / `pip` CLIs — third-party app and Python package updates

**Project layout**

```
XenUpdate.sln
├── XenUpdate.App             # WPF shell, views, view-models, themes, tray, localization
├── XenUpdate.Core            # Interfaces, models, enums (no dependencies)
├── XenUpdate.Infrastructure  # winget, WUA, drivers, NVIDIA, system restore, storage
└── XenUpdate.Tests           # Unit tests (~85)
```

**Build from source**

Requirements: Windows 10/11 · Visual Studio 2022 · .NET 8 SDK · winget installed

```bash
git clone https://github.com/huseyincancalti/XenUpdate.git
cd XenUpdate
# Open XenUpdate.sln in Visual Studio 2022, set XenUpdate.App as startup project, run as Admin
```

Or from the command line:

```bash
dotnet build XenUpdate.sln -c Release
```

Self-contained portable publish (replicates the release zip):

```bash
dotnet publish XenUpdate.App/XenUpdate.App.csproj -c Release -r win-x64 --self-contained true
```

---

## License

MIT — see [LICENSE](LICENSE).

---

<div align="center">

**Developed by Hüseyin Can Çaltı**

[karakedidub.com](https://karakedidub.com) · [hsyncalti2@gmail.com](mailto:hsyncalti2@gmail.com) · [@huseyincancalti](https://github.com/huseyincancalti)

</div>
