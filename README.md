<div align="center">

<img src="img/logo.png" alt="XenUpdate Logo" width="120" />

# ⚡ XenUpdate

**The guided way to keep Windows up to date.**
*Most updaters just list what's outdated. XenUpdate also **guides you through the updates tools can't automate** — GPU drivers, BIOS, firmware — by detecting your hardware, checking whether a newer version really exists, and walking you through it step by step. Apps, Windows Updates, and drivers too, all from one clean interface.*

[![.NET 8](https://img.shields.io/badge/.NET-8.0-blueviolet.svg)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/UI-WPF%20Glassmorphism-blue.svg)]()

</div>

---

## 📥 Download & Install

Experience the seamless update management. Download the latest compiled version directly from our Releases page:

👉 **[Download XenUpdate (Latest Release)](../../releases/latest)**

> **💡 Pro Tip:** XenUpdate integrates deeply with your system. For the best experience and to avoid access errors, right-click the `.exe` and select **Run as Administrator**.

### ⚠️ Windows SmartScreen Warning
When you run XenUpdate for the first time, Windows SmartScreen may display a blue warning screen saying "Windows protected your PC". This is completely normal for new, open-source applications that haven't built up a long download history yet.
**To run the app:** Click **"More info"** and then **"Run anyway"**.
*(Since this project is entirely open-source, you can review all the code in this repository if you have any security concerns!)*

---

## ✨ Why XenUpdate? (Features)

XenUpdate isn't just another updater; it's designed with a "Power User" mentality and AAA quality in mind.

- 🧭 **Guided Update Advisor:** For the things Windows *can't* update on its own — GPU drivers, BIOS, firmware — XenUpdate detects your actual hardware, checks whether a newer version genuinely exists (e.g. a live NVIDIA driver check), and walks you through it step by step. If the vendor's own tool (e.g. the NVIDIA App) is installed, it launches it for you.
- 🚀 **Unified Engine:** Scan and update third-party apps (via **Winget**, with live download sizes), **Windows Updates**, and **Drivers** — all from one place.
- 🧩 **No Setup Headaches:** Ships **self-contained** — the .NET 8 runtime is bundled, so there is nothing to install. Download, run, done.
- 🎨 **Premium UI/UX:** Stunning Glassmorphism design, smooth animations, and Light/Dark theme support.
- 🥷 **Ninja Mode:** Starts minimized in your System Tray and silently checks for updates in the background.
- 🛡️ **Safe & Smart:** Automatically creates **System Restore Points** before driver/system installations.
- 🌍 **Drop-in Localization:** JSON-based languages you can switch on the fly — and add your own by dropping a translated `xx.json` into the `Assets/Locales` folder. No rebuild required.
- 🛑 **Blacklist Manager:** Hide specific updates you don't want to see again.
- 🐛 **Advanced Diagnostics:** Built-in Log Viewer and a custom Crash Reporter for easy debugging.
- 🔄 **Self-Updating:** XenUpdate checks its own GitHub repository and notifies you when a new version is out!

---

## 🏗️ Architecture & Tech Stack

XenUpdate is built on a clean, layered MVVM architecture, ensuring separation of concerns between UI logic, system integration, and data storage.

### 💻 Technologies Used
- **C# / .NET 8** & **WPF**
- **CommunityToolkit.Mvvm** (For robust MVVM implementation)
- **MaterialDesignInXamlToolkit** (For modern UI components)
- **Winget & WUApiLib** (For fetching updates)
- **H.NotifyIcon.Wpf** (For System Tray integration)

### 📂 Project Structure
```text
XenUpdate.sln
├── XenUpdate.App              # WPF UI, Views, ViewModels, Themes, Tray Icon, LocalizationManager, Guide Center
├── XenUpdate.Core             # Interfaces, Models, Enums, update DTOs
├── XenUpdate.Infrastructure   # Winget, Windows Update, Drivers, Hardware/NVIDIA, Guides, Storage, SystemRestore
└── XenUpdate.Tests            # Unit tests (60)
```

### 🛠️ Build from Source
If you want to contribute or build the project yourself:

**Requirements**
- Windows 10 / Windows 11
- Visual Studio 2022
- .NET 8 SDK
- Winget (App Installer)

**Steps**
1. Clone the repository:
   ```bash
   git clone https://github.com/huseyincancalti/XenUpdate.git
   cd XenUpdate
   ```
2. Open `XenUpdate.sln` in Visual Studio 2022.
3. Set `XenUpdate.App` as the startup project.
4. Build and run the application (preferably as Administrator).

---

## ⚠️ Notes & Disclaimer
- XenUpdate interacts directly with Windows system-level tools. Some update operations (especially drivers and core OS updates) require administrator privileges.
- The application does not forcefully restart your computer, but some updates may require a manual reboot to take full effect.
- Always review what you are installing.

---

## 📄 License

XenUpdate is released under the [MIT License](LICENSE) — free to use, modify, and distribute.

---

## 👨💻 Developed By
**Hüseyin Can Çaltı** 
🌐 Website: [huseyincancalti.github.io/karakedidub/](https://huseyincancalti.github.io/karakedidub/)  
🐙 GitHub: [@huseyincancalti](https://github.com/huseyincancalti)
