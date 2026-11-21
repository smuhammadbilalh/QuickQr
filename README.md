<div align="center">

# 🔳 QuickQr

### Create, design and scan QR codes and barcodes. Offline, private and ad-free, on every platform.

[![.NET MAUI](https://img.shields.io/badge/.NET%20MAUI-.NET%2011-0a0a0a?style=for-the-badge&logo=dotnet&logoColor=fafafa)](https://dotnet.microsoft.com/apps/maui)
[![Android](https://img.shields.io/badge/Android-0a0a0a?style=for-the-badge&logo=android&logoColor=fafafa)](#-supported-platforms)
[![iOS](https://img.shields.io/badge/iOS-0a0a0a?style=for-the-badge&logo=apple&logoColor=fafafa)](#-supported-platforms)
[![Windows](https://img.shields.io/badge/Windows-0a0a0a?style=for-the-badge)](#-supported-platforms)
[![macOS](https://img.shields.io/badge/macOS-0a0a0a?style=for-the-badge&logo=apple&logoColor=fafafa)](#-supported-platforms)
[![CI](https://img.shields.io/github/actions/workflow/status/smuhammadbilalh/QuickQr/ci.yml?style=for-the-badge&color=0a0a0a&label=CI)](https://github.com/smuhammadbilalh/QuickQr/actions)

</div>

---

## ✨ Features

### Free, with no ads and no account
- ⚡ **19 content types**, each with a guided form:
  - links, text, Wi-Fi (WPA/WPA3/WEP/hidden), vCard and MeCard contacts
  - email, SMS, phone, location, calendar events
  - WhatsApp, Telegram, app-store links, social profiles
  - payments: Bitcoin/Ethereum, SEPA/EPC GiroCode, UPI
- 🎨 **Live preview and design basics.** Presets, custom colors with a contrast check, error-correction control, and a capacity meter.
- 📷 **Fast scanner.**
  - ML Kit on Android, Apple Vision on iOS and macOS, zxing-cpp on Windows.
  - Torch, zoom, camera switch, scanning from images, and several codes in one picture.
- 🛡️ **On-device phishing protection.**
  - Warns about look-alike (punycode/homograph) domains, link shorteners, IP-address hosts, `user@host` tricks, risky TLDs, dangerous schemes and file downloads.
  - No data leaves your device.
- ⚙️ **Smart actions.** Join Wi-Fi, add a contact, add a calendar event, call, text, email, navigate, open a payment app, or decrypt a Secure QR.
- 🗂️ **Unlimited history.** Full-text search, favorites, notes and tags. Re-create any code in one tap.
- 🌗 **Comfort and access.** Light and dark themes, app shortcuts, and accessibility semantics.

### Pro ($9.99, one-time)
- 🖌️ **Design studio.** Gradients, dot, rounded and diamond modules, custom eyes, your logo, "SCAN ME" frames and reusable **brand kits**.
- 🖨️ **Pro export.** SVG, print-ready PDF, and PNG up to 4096 px.
- 🔢 **All barcode formats.** Data Matrix, Aztec, PDF417, Code 128/39/93, EAN, UPC, ITF and Codabar.
- 🔐 **Secure QR.** The content is encrypted with a passphrase (AES-256 + HMAC, PBKDF2 600k).
- 🔁 **Continuous scan sessions** with CSV export, plus history export to CSV or JSON.
- 🔒 **App lock** with fingerprint, face or PIN.

### Business ($29.99, one-time)
- 📦 **Bulk generation.** Turn a CSV into a ZIP of hundreds of codes, with a manifest.
- 🏷️ **Print sheets.** A4 or Letter label layouts as vector PDF.
- ☁️ *Coming soon:* dynamic codes, scan analytics, cloud sync and team workspaces.

See [docs/product/PREMIUM-PLAN.md](docs/product/PREMIUM-PLAN.md) for the full matrix and the reasoning behind it.

## 📱 Supported Platforms

| Platform | Target | Minimum |
|---|---|---|
| Android | `net11.0-android` | Android 7.0 (API 24) |
| iOS | `net11.0-ios` | iOS 15 |
| macOS (Mac Catalyst) | `net11.0-maccatalyst` | macOS 14 (Catalyst 17) |
| Windows | `net11.0-windows10.0.19041.0` | Windows 10 1809 |

## 🏛️ Architecture

The app follows clean architecture with three layers:
- **`QuickQr.Core`** is pure .NET: the domain and application services.
- **`QuickQr.Infrastructure`** holds the adapters: ZXing.Net, SkiaSharp, SQLite and HttpClient.
- **`QuickQr.App`** is the MAUI head: MVVM, compiled XAML and platform services.

It is unit-tested with xUnit v3 on Microsoft.Testing.Platform. Details are in [docs/architecture/ARCHITECTURE.md](docs/architecture/ARCHITECTURE.md).

```
QuickQr/
├── src/
│   ├── QuickQr.Core/            # Content model, styling, licensing, history, ports, application services
│   ├── QuickQr.Infrastructure/  # ZXing encode/decode, Skia render (PNG/SVG/PDF), SQLite, batch, updates, billing
│   └── QuickQr.App/             # MAUI: Features/{Generate,Scan,History,Settings,Batch,Premium,About}, Services, Platforms
├── tests/                       # Core, Codecs and Infrastructure test suites
├── docs/                        # Architecture, premium plan, market and standards research
├── Directory.Build.props        # Shared build settings and the product version
├── Directory.Packages.props     # Central package versions
└── global.json                  # .NET SDK 11.0.100 and the test runner
```

## 🛠️ Tech Stack

- **.NET 11 / .NET MAUI 11.0.10.** XAML source generator, compiled bindings, CommunityToolkit.Maui 17 and CommunityToolkit.Mvvm 8.4.
- **Scanning:** BarcodeScanning.Native.Maui 3.1 for the camera, ZXing.Net 0.16.11 for still images.
- **Rendering:** SkiaSharp 4.157, with vector SVG and PDF via `SKSvgCanvas` and `SKDocument`.
- **Data:** Microsoft.Data.Sqlite 11 with FTS5, and System.Text.Json source generators.
- **Networking:** `IHttpClientFactory` with Microsoft.Extensions.Http.Resilience.
- **Billing and security:** Plugin.InAppBilling 11 and Plugin.Maui.Biometric.

## ⚡ Getting Started

**Prerequisites:**
- **.NET SDK 11.0.100+** with the MAUI workloads (`dotnet workload install maui`).
- **Visual Studio 2026 (18.x)** with the *.NET Multi-platform App UI development* workload, or VS Code with the .NET MAUI extension.

```powershell
git clone https://github.com/smuhammadbilalh/QuickQr.git
cd QuickQr
dotnet test --project tests/QuickQr.Core.Tests/QuickQr.Core.Tests.csproj
dotnet build src/QuickQr.App/QuickQr.App.csproj -t:Run -f net11.0-windows10.0.19041.0
dotnet build src/QuickQr.App/QuickQr.App.csproj -t:Run -f net11.0-android
```

You can also open `QuickQr.slnx` in Visual Studio, pick a target and press **F5**.

> Debug builds use a **simulated store**, so you can try every Pro and Business feature without spending money. Release builds use the real platform stores.
> Store builds should add `-p:DefineConstants=STORE_BUILD`, which disables the GitHub self-update check.

## 🔐 Privacy

QuickQr works offline. History, settings and purchases stay on your device. See [PRIVACY.md](PRIVACY.md).

---

## 👨‍💻 Author

<div align="center">

**Muhammad Bilal**: IT Specialist & App Developer · Gujrat, Pakistan

[![GitHub](https://img.shields.io/badge/GitHub-smuhammadbilalh-0a0a0a?style=for-the-badge&logo=github)](https://github.com/smuhammadbilalh)
[![LinkedIn](https://img.shields.io/badge/LinkedIn-muhammadbilaltech-0a0a0a?style=for-the-badge)](https://www.linkedin.com/in/muhammadbilaltech)
[![Email](https://img.shields.io/badge/Email-smuhammadbilalh%40gmail.com-0a0a0a?style=for-the-badge&logo=gmail&logoColor=fafafa)](mailto:smuhammadbilalh@gmail.com)

⭐ If this project helped you, please consider giving it a star!

</div>
