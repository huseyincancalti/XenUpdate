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
| **Guided Advisor** | Detects your GPU/CPU, checks whether a newer driver actually exists (live NVIDIA check), and walks you through manual updates (BIOS, firmware, GPU) step by step |
| **Scan All** | One button kicks off all three scans simultaneously |
| **System Restore** | Creates a restore point automatically before driver/system installs |
| **Offline awareness** | Dims the UI and shows a badge in the title bar when the network is down |
| **Blacklist** | Hide specific packages you never want to see in update results |
| **Tray mode** | Minimize to system tray; background checks run silently |
| **Drop-in languages** | Add a new locale by dropping a translated `xx.json` into `Assets/Locales` — no rebuild needed |
| **Light / Dark theme** | Toggle anytime from Settings |
| **Self-updating** | Checks its own GitHub releases and notifies you when a new version is out |

---

## Architecture

XenUpdate is a WPF/.NET 8 app built on a clean layered MVVM architecture.

**Tech stack**
- C# / .NET 8 · WPF
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM, commands, messaging
- [MaterialDesignInXamlToolkit](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) — UI components
- [H.NotifyIcon.Wpf](https://github.com/HavenDV/H.NotifyIcon) — system tray
- WUApiLib (COM) — Windows Update API
- `winget` CLI — third-party app updates

**Project layout**

```
XenUpdate.sln
├── XenUpdate.App             # WPF shell, views, view-models, themes, tray, localization
├── XenUpdate.Core            # Interfaces, models, enums (no dependencies)
├── XenUpdate.Infrastructure  # winget, WUA, drivers, NVIDIA, system restore, storage
└── XenUpdate.Tests           # Unit tests (~60)
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
