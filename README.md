<div align="center">

# 🔳 QuickQr

### Generate, share and scan QR codes — one app, every platform

[![.NET MAUI](https://img.shields.io/badge/.NET%20MAUI-.NET%2010-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/apps/maui)
[![Android](https://img.shields.io/badge/Android-3DDC84?style=for-the-badge&logo=android&logoColor=white)](#-supported-platforms)
[![iOS](https://img.shields.io/badge/iOS-000000?style=for-the-badge&logo=apple&logoColor=white)](#-supported-platforms)
[![Windows](https://img.shields.io/badge/Windows-0078D4?style=for-the-badge)](#-supported-platforms)
[![macOS](https://img.shields.io/badge/macOS-000000?style=for-the-badge&logo=apple&logoColor=white)](#-supported-platforms)
[![Stars](https://img.shields.io/github/stars/smuhammadbilalh/QuickQr?style=for-the-badge&color=f5a623)](https://github.com/smuhammadbilalh/QuickQr/stargazers)

</div>

---

## ✨ Features

- ⚡ **Instant generation** — type any text, URL, Wi-Fi string or contact and the QR code renders live
- 📷 **Camera scanner** — scan QR codes and barcodes in real time with ZXing.Net.MAUI, with camera-permission handling built in
- 💾 **Save & share** — export the code as a crisp PNG or share it straight to any app via the native share sheet
- 🎨 **Polished UX** — Material icons, smooth loading transitions and a friendly first-run welcome
- 🔄 **Update check** — built-in `UpdateHelper` lets users know when a new version is available
- 👨‍💻 **Developer page** — about & contact screen with portfolio and social links

## 📱 Supported Platforms

| Platform | Target |
|---|---|
| Android | `net10.0-android` |
| iOS | `net10.0-ios` |
| macOS (Mac Catalyst) | `net10.0-maccatalyst` |
| Windows | `net10.0-windows10.0.19041.0` |

## 🛠️ Tech Stack

- **Framework:** .NET MAUI on .NET 10 (single project)
- **QR encode/decode:** [ZXing.Net](https://github.com/micjahn/ZXing.Net) & [ZXing.Net.MAUI](https://github.com/Redth/ZXing.Net.Maui)
- **Rendering:** SkiaSharp
- **Pattern:** XAML + code-behind with `INotifyPropertyChanged` bindings

## ⚡ Getting Started

**Prerequisites:** Visual Studio 2022 17.14+ (or VS Code with the .NET MAUI extension) and the **.NET Multi-platform App UI development** workload.

```bash
git clone https://github.com/smuhammadbilalh/QuickQr.git
cd QuickQr
dotnet restore
# Run on Windows
dotnet build -t:Run -f net10.0-windows10.0.19041.0
# Run on Android (emulator or device attached)
dotnet build -t:Run -f net10.0-android
```

Or open `QuickQr.sln` in Visual Studio, pick a target device and press **F5**.

## 🗂️ Project Structure

```
QuickQr/
├── MainPage.xaml(.cs)      # QR generator UI & logic
├── ScanPage.xaml(.cs)      # Live camera scanner
├── DeveloperPage.xaml(.cs) # About the developer
├── UpdateHelper.cs         # Version / update checks
├── AppShell.xaml           # Navigation shell
├── Platforms/              # Per-platform code (Android, iOS, MacCatalyst, Windows)
└── Resources/              # Fonts, images, styles
```

## 🗺️ Roadmap

- [ ] QR templates (Wi-Fi, vCard, SMS, email)
- [ ] Custom colours and logo embedding
- [ ] Scan history
- [ ] Store releases (Google Play / Microsoft Store)

---

## 👨‍💻 Author

<div align="center">

**Muhammad Bilal** — IT Specialist & App Developer · Gujrat, Pakistan

[![GitHub](https://img.shields.io/badge/GitHub-smuhammadbilalh-181717?style=for-the-badge&logo=github)](https://github.com/smuhammadbilalh)
[![LinkedIn](https://img.shields.io/badge/LinkedIn-muhammadbilaltech-0A66C2?style=for-the-badge)](https://www.linkedin.com/in/muhammadbilaltech)
[![Email](https://img.shields.io/badge/Email-smuhammadbilalh%40gmail.com-EA4335?style=for-the-badge&logo=gmail&logoColor=white)](mailto:smuhammadbilalh@gmail.com)

⭐ If this project helped you, please consider giving it a star!

</div>
