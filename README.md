<div align="center">

# ⚡ XenUpdate

**A modern Windows desktop application that helps you manage software, Windows, and driver updates from a single interface.**

Built with **.NET 8**, **WPF**, and **MVVM**, featuring a clean architecture focused on maintainability, responsiveness, and a modern user experience.

</div>

---

## ✨ Features

- **App Updates:** Scan and update installed applications via **Winget**.
- **System Updates:** Scan and install **Windows Updates** & **Driver Updates**.
- **Blacklist System:** Exclude specific apps you do not want to update.
- **Modern UI:** Light and Dark theme support with status badges and progress feedback.
- **Log Console:** Built-in console for debugging and sharing logs easily.
- **Configuration:** JSON-based settings persistence.
- **Admin Mode:** Administrator mode support for deep system updates.

## 🏗️ Architecture

XenUpdate uses a clean, layered architecture separating UI logic from update, storage, and system integration operations:

    ZenUpdate.sln
    ├── ZenUpdate.App              # WPF UI, Views, ViewModels, Themes
    ├── ZenUpdate.Core             # Models, Interfaces, Enums
    ├── ZenUpdate.Infrastructure   # Winget, Windows Update, Drivers, Logging, Storage
    └── ZenUpdate.Tests            # Unit tests


### 💻 Technologies
- **.NET 8** & **WPF**
- **CommunityToolkit.Mvvm**
- **MaterialDesignInXamlToolkit**
- **Winget** & **WUApiLib**
- JSON settings storage

---

## 🚀 Getting Started

### 📥 Download
You can download the latest version directly from the Releases page:  
👉 **[Download XenUpdate from Releases](../../releases)**

> **💡 Tip:** After downloading, run the application as **Administrator** for the best experience.

### 🛠️ Build from Source

**Requirements**
- Windows 10 / Windows 11
- Visual Studio 2022
- .NET 8 SDK
- Winget

**Steps**
1. Clone the repository:
       git clone https://github.com/your-username/XenUpdate.git
       cd XenUpdate
2. Open `ZenUpdate.sln` in **Visual Studio 2022**.
3. Set `ZenUpdate.App` as the startup project.
4. Build and run the application.

---

## ⚠️ Notes

- XenUpdate interacts with system-level update tools. Some update operations may require **administrator privileges** or a **system restart**.
- The application **does not** automatically restart your computer.

---

## 👨‍💻 Developer

Developed by **Hüseyin Can Çaltı** 🌐 **Website:** [huseyincancalti.github.io/karakedidub/](https://huseyincancalti.github.io/karakedidub/)

⭐️ *If you like this project or find it useful, please consider giving it a star!*
