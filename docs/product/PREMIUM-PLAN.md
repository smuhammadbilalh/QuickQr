# QuickQr plans and premium features

This is the product definition behind `src/QuickQr.Core/Licensing/FeatureCatalog.cs`. It's based on the market research in [`docs/research/feature-research.md`](../research/feature-research.md).

## Positioning

> **Offline, private, ad-free, and your codes never expire.**

Competitors charge for things that need their servers: dynamic codes, analytics and seats. Mobile scanners lean on ads and weekly subscriptions, and those subscriptions draw "scam" reviews. QuickQr runs entirely on the device, so it sells **one-time upgrades**. Subscriptions are held back for future features that cost money per user to run (cloud sync, dynamic codes, live threat lookups).

## Tiers

| | **Free** | **Pro**: $9.99 one-time | **Business**: $29.99 one-time ($19.99 upgrade from Pro) |
|---|---|---|---|
| Who | Everyone | Creators, marketers, privacy-minded power users | Shops, events, inventory, print |
| Ads / account | None / none | None / none | None / none |

Store product ids are `quickqr.pro.lifetime`, `quickqr.business.lifetime` and `quickqr.business.upgrade` (non-consumable). The subscription ids `*.monthly` / `*.yearly` are reserved for backend features.

## Feature matrix

| Area | Free | Pro | Business |
|---|---|---|---|
| **Content types** | Text, URL, Wi-Fi (WPA/WPA3/WEP/hidden), vCard and MeCard, email, SMS, phone, location, calendar event, WhatsApp, Telegram, crypto (BIP21/EIP-681), SEPA (EPC v002), UPI, app stores, social profiles | **Secure QR**: the content is encrypted with a passphrase | |
| **Formats** | QR Code | **All formats**: Data Matrix, Aztec, PDF417, Code 128/39/93, EAN-8/13, UPC-A/E, ITF, Codabar | |
| **Design** | Presets, **custom colors with a contrast check**, error correction, quiet zone | **Gradients, module shapes, eye styles, logo, frames with a call to action, brand kits** | |
| **Export** | PNG, JPEG and WebP up to 1024 px, share, save to gallery, decode check | **SVG, PDF, up to 4096 px** | **Bulk CSV → ZIP**, **print sheets** (A4/Letter labels) |
| **Scan** | Live camera (all symbologies), torch, zoom, scan from image, multi-code images, smart actions | **Continuous scan sessions with CSV export** | |
| **Safety** | **On-device phishing analysis**: punycode and homographs, shorteners, IP hosts, `@` tricks, risky TLDs, dangerous schemes, downloads | Live threat lookups *(coming soon; needs a server-side Web Risk proxy)* | |
| **History** | **Unlimited**, full-text search, favorites, notes and tags | **CSV/JSON export** | |
| **Privacy** | Local-only data | **App lock** (biometrics or device PIN) | |
| **Cloud** *(coming soon)* | | | Dynamic codes, scan analytics, cloud sync, team workspace |

## How gating works

- **Try before you buy.** Every premium design renders in the live preview. Gating happens at **export** (save, share or file), where `IQrGenerationService.GetLockedFeatures` lists exactly which features the design uses.
- `IPremiumGate.EnsureAsync(feature)` opens the paywall with that feature highlighted. It returns `true` only if the user upgrades.
- "Coming soon" features show in the comparison table but never unlock and never take payment.
- **Entitlements:**
  - Cached in secure storage, so paid users get their features offline from the first launch.
  - Re-validated against the store on every start, with a 7-day grace period when the store can't be reached.
  - Restored through *Settings → Restore purchases*.

## Store policy notes

- GitHub self-update prompts are compiled out of store builds (`-p:DefineConstants=STORE_BUILD`).
- Google Safe Browsing's Lookup API is for non-commercial use only. Commercial live lookups must go through **Google Web Risk** behind a proxy, and the key must never ship in the app.
- Apple's Small Business Program and Google Play both take 15% on the first $1M. The Microsoft Store lets non-game apps use their own payment system.

## Roadmap (beyond this release)

| Priority | Item | Tier |
|---|---|---|
| P1 | Android Quick Settings tile and home-screen widget; iOS widgets / App Intents | Free / Pro |
| P1 | Localization (resx): English, Urdu, Arabic, Spanish, German, French | Free |
| P1 | Folders, backup/restore (encrypted file), desktop screen-region scanning | Pro |
| P2 | GS1 Digital Link, GS1-128 parsing, inventory sessions, webhook per scan | Business |
| P2 | Signed offline license keys for sideloaded desktop builds | Pro / Business |
| Backend | Dynamic codes, analytics, sync, teams, Web Risk proxy (subscription) | Business |
