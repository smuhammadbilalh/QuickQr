# QuickQr: Feature, Pricing and Standards Research

- **Date:** 2026-11-21
- **Scope:** QR generator and scanner products and services in 2025-2026, a Free / Pro / Business split, payload format specs, scanner security ("quishing"), and the ZXing.Net symbology matrix.
- **Target app:** QuickQr, a .NET MAUI app for Android, iOS, macOS (Mac Catalyst) and Windows, built by a solo developer.
- **Current baseline:** free-text QR generation with presets (Email, Phone, URL, SMS, Wi-Fi, vCard, Geo), PNG save and share, live ZXing camera scanning, scanning from an image file, basic smart actions, and a GitHub-release update check. QuickQr has no history, settings, styling or paid tier yet.

**Legend used throughout**

| Field | Values |
|---|---|
| Priority | **P0** = table stakes, ship next. **P1** = strong differentiator, ship soon. **P2** = later or nice to have. |
| Tier | **Free**, **Pro** (power user, one person), **Business** (batch, inventory, integration, print). |
| Feasibility | **on-device**: works offline in MAUI with ZXing.Net, SkiaSharp, SQLite and platform APIs. **on-device (net)**: needs no backend of yours, but calls a public or user-configured endpoint. **3rd-party backend**: someone else's service, such as a bring-your-own-key (BYO-key) API or RevenueCat. **needs backend**: you would have to run a server. |

> **Pricing caveat.** Vendors change prices often, and several pricing pages render prices with JavaScript or only in EUR or local currency. This report uses the vendor page where it was readable and Capterra, G2 or other third parties otherwise, and flags conflicts. Re-check the prices before using them in marketing copy.

---

## 0. Executive summary

1. **Static generation is a commodity, and so is free design on the web.** QRCode Monkey offers logos, gradients, shapes and SVG/PDF/EPS export free, with no sign-up. Every SaaS platform gives away unlimited static codes. What they charge for is **dynamic (editable) codes, scan analytics, bulk creation, API access, users, custom domains and white-labelling**, all of which need a server.
2. **Mobile scanner apps make money from ads, ad-unlocks and aggressive weekly subscriptions**, which draw a lot of complaints. Examples are QRbot's ad-unlocks with Pro IAPs of $3.99-$14.99, and SafeQR at $1.99/week or $79.99/year. An **offline, private, ad-free app with honest one-time pricing** is a real positioning gap. A second strong message is *"your codes never expire"*: SaaS dynamic codes stop working when the plan lapses.
3. **Security is the strongest free differentiator.** The FTC and FBI both warn about QR phishing. Kaspersky's scanner relies on a cloud lookup and Norton Snap has been retired. An **on-device URL risk engine** can cover punycode/IDN homographs, shorteners, IP literals, `@` userinfo tricks, http, risky TLDs, dangerous schemes and file downloads, and it needs no backend and no Google terms.
4. **Google Safe Browsing API is non-commercial only.** A paid app must use **Web Risk**, which needs a Google Cloud billing account, has a per-call price above the free quota, and should sit behind a proxy so the key isn't shipped in the app. Defer it. Meanwhile, open links through the system browser or a WebView: Chrome/Android WebView Safe Browsing, Safari/WKWebView fraudulent-site warnings and WebView2 SmartScreen all add free protection.
5. **Recommended tiers.** Free covers everything a normal user needs: every payload builder, scanning, smart actions, security checks, unlimited local history, PNG export, basic colors, shortcuts and share target. **Pro (about $9.99 one-time)** covers advanced styling, vector and print export, other barcode formats, batch scanning, folders and tags, export and backup, and desktop screen scanning. **Business (about $29.99 one-time)** covers CSV bulk generation, label sheets, inventory sessions, webhooks, GS1 tools, check-in or kiosk mode, and a desktop CLI. Keep subscriptions for later backend features such as dynamic QR, sync and reputation lookups, which have a running cost per user.
6. **Correct payloads are a quality moat.** Generate with strict builders and validation: IBAN mod-97, EPC 331-byte limit at error-correction level M, BIP21/321 and EIP-681 checksums, Wi-Fi escaping. Accept the many de-facto variants when parsing (`SMSTO:` and `sms:`, `MATMSG` and `mailto:`, `T:SAE` and `T:WPA;R:1`).
7. **ZXing.Net 0.16.11 (Oct 2025)** writes QR, Data Matrix, Aztec, PDF417, Code 128/39/93, EAN-8/13, UPC-A/E, ITF, Codabar, MSI and Plessey. It *reads* MaxiCode (pure images only), GS1 DataBar (RSS-14/Expanded), IMb and Pharmacode, but cannot write them. **No Micro QR or rMQR in either direction.** zxing-cpp has both, but its NuGet package ships desktop native libraries only.
8. **Store hygiene.** Disable the GitHub self-update prompt in Google Play and App Store builds, because Google Play forbids updating outside Play. Use `Plugin.InAppBilling` 11.x for IAP on all four platforms. For builds distributed outside the stores, use offline-verifiable signed license keys.

---

## 1. Competitive landscape (2025-2026)

### 1.1 Generator platforms (SaaS)

| Product | Model | Free offer | Paid plans (as reported) | What paid unlocks |
|---|---|---|---|---|
| **QR Code Generator PRO** (qr-code-generator.com; grouped with **Bitly** in the brief) | SaaS, dynamic QR | 14-day trial, no card needed; static codes free on the website | **Starter** (2 dynamic, 10,000 scans, monthly or annual), **Advanced** (50 dynamic, unlimited scans, bulk 100, API 3,000/mo, 2 users, annual only), **Professional** (250 dynamic, bulk 500, API 10,000/mo, 5 users, annual only), **Enterprise** (custom, white label). The upgrade page shows about €8/mo for Starter and €37.50/mo for Professional, billed annually; third parties report about $12.50 for Advanced. | Dynamic codes, scan statistics and CSV export, bulk CSV creation, API, users, 22 QR types, logos and frames |
| **Bitly** | Links plus QR | Free: 2 QR codes/mo, 5 links/mo | Core $10/mo (annual only, 5 QR/mo); Growth $29/mo annual or $35 monthly (10 QR/mo); Premium $199/mo annual or $300 monthly (200 QR/mo); Enterprise custom | QR volume, click and scan data retention (30 days to 1 year), landing pages, custom domains |
| **QR TIGER** | SaaS | 3 dynamic codes with 500 scans each, a QR TIGER logo pop-up, and unlimited static codes | Regular $7/mo (12 dynamic, API 500/mo); **Advanced $16/mo** (200 dynamic, bulk up to 3,000 per batch, white-label domain, geofencing, Zapier and HubSpot); Premium $37/mo (600 dynamic, API 10,000/mo); Enterprise custom | Dynamic codes, analytics, bulk, API, white-label domain, editing after creation, integrations. Paid dynamic codes stop working when the plan expires. |
| **Uniqode** (formerly Beaconstac) | Enterprise-leaning SaaS | 14-day trial only. After the trial, dynamic codes go inactive and static codes keep working. | Essential $9/mo (15 dynamic, 1 user); Core $49/mo (250 dynamic, API at 10 req/s); Plus $99/mo (500 dynamic, 3 users; custom domain and SSO are $500/yr add-ons); Business+ custom (SSO, custom domain, digital business cards). Yearly prices only. | Analytics retention (30-180 days and up), bulk, API, GPS scan tracking, SSO, custom domains, digital business cards |
| **Scanova** | SaaS | Trial | Basic 3, Lite 10, Standard 50, Pro 500 dynamic codes; Standard is USD 50/mo or 420/yr per the help centre; Capterra lists $5/$9/$35/$75; one-time "Ultra-Lite" USD 150; Enterprise | Dynamic codes, design, analytics |
| **ME-QR** | Freemium with ads | **Free with ads on every code**: unlimited scans, 10,000 codes, 1-year analytics, 100 MB, API, unlimited users and folders | Lite (one ad-free code); Premium (all codes ad-free, 1M codes, 3-year analytics, 500 MB), about $69/yr per third parties. Prices are shown in local currency. | Removing ads, storage, analytics history |
| **QRCode Monkey** | Free web generator | Static codes only; free logos, colors, gradients and shapes; PNG, SVG, PDF and EPS export; no sign-up; codes never expire | n/a | n/a. Sets the expectation that design is free. No dynamic codes or statistics. |

**Takeaway:** every SaaS meters **dynamic codes, scans, analytics retention, users, bulk, API and custom domains**. All of these need a server. Static codes are free and unlimited everywhere.

### 1.2 Scanner apps and built-in scanners

| Product | Business model | Notable features | Security presentation |
|---|---|---|---|
| **QRbot** (TeaCapps) | Free with ads; watching ads unlocks features; Pro IAPs listed at US$3.99 / $10.99 / $14.99 | Wide symbology support (QR, EAN/UPC, Code 128/39/93, Data Matrix, PDF417, Aztec, ITF, Codabar, DataBar); searchable history with **CSV export**; **business scanner mode** that sends scans to a configurable web server; inventory; product and price lookup; generator for URL, Wi-Fi and vCard/MeCard | Shows content before acting; no reputation service advertised |
| **Kaspersky QR Scanner** | Free | Scan, check, then open; reads text, contacts and Wi-Fi; optional history; built-in preview browser | Checks links against **Kaspersky Security Network** (cloud). Known-dangerous links are **highlighted red and cannot be opened or saved**. **Unknown sites are not highlighted**, so a clean result means only "no known threat". |
| **Norton Snap** | Retired | Checked the Norton Safe Web rating and **prompted before visiting** | Norton now points users to Norton Mobile Security's Web Protection |
| **Orca Scan** | Free tier (1 user, 1 sheet, 50 rows); Starter $20/user/mo; Business $40/user/mo; Enterprise | Scan to a spreadsheet, custom fields, GPS, integrations | n/a |
| **Scan-IT to Office** (TEC-IT) | Subscription: $4.99/mo, $10.99/3 mo, $30.99/yr. Without a subscription the app **injects demo text** at irregular intervals. Volume licensing available. | Real-time capture into Excel, Word and Google Sheets (free add-ins); offline caching; GPS and timestamps | n/a |
| **SafeQR Reader** (example of a security-pitched scanner) | $1.99/wk, $7.99/mo, $79.99/yr | AI-labelled scam detection and content summaries | "Real-time alerts" with the method undisclosed. Its App Privacy label lists ad tracking, which undercuts the privacy pitch. |
| **Android system scanner and Google Lens** | Built in | Android 13+ **Quick Settings tile** scanner. A redesign shows the decoded URL, the default browser's icon and a share button. Lens recognizes QR codes. The Play services **code scanner API** needs no camera permission. | Shows the URL before opening |
| **Apple Camera and Code Scanner** | Built in | Since iOS 17 the link button appears at the bottom of the camera view. Supports **Wi-Fi** (not hidden networks), **vCard**, **iCalendar** and URLs. Code Scanner is also available in Control Center. | Shows the destination before opening |

There is also a widespread pattern of **weekly-subscription QR apps** at $4.99-$9.99/week that collect "scam" reviews. QuickQr can stand apart from them with a clear one-time price.

### 1.3 Commoditized vs monetized

| Commoditized (users expect it free) | Monetized by competitors |
|---|---|
| Static QR for URL, text, Wi-Fi, vCard, email, SMS, phone, geo | Dynamic or editable QR and scan analytics (server) |
| Scanning QR and common 1D codes, scanning from images | Bulk generation (CSV), API, users and SSO, custom domains (server or ops) |
| Showing the URL before opening (both OS cameras do this) | Ad removal, batch scan, CSV export (mobile apps) |
| Basic colors, even logos on the web (QRCode Monkey) | Brand templates, frames, vector and print exports (in apps) |
| Local history | Scan-to-spreadsheet and inventory workflows (Orca, Scan-IT) |

### 1.4 Positioning opportunities for QuickQr

1. **Privacy-first and offline:** no account, no ads, no tracking, history stays on the device. Most free scanners either show ads or send URLs to a cloud.
2. **Security without a cloud:** an explainable on-device verdict ("This link uses Cyrillic letters that imitate *apple.com*").
3. **"Codes never expire":** static codes only, plus honest messaging about why dynamic codes need a server.
4. **True desktop parity:** Windows and macOS with clipboard and screen scanning, drag and drop, and print. Mobile-first competitors are weak on desktop.
5. **Standards-correct payment codes** (EPC/GiroCode, UPI, BIP21/321, EIP-681, optionally Swiss QR-bill and EMVCo/Pix) with validation, a niche that SaaS products handle shallowly.

---

## 2. Monetization

### 2.1 Typical structures and price points

- **SaaS generators:** Free or trial, then **Starter at $5-$10/mo, Pro or Business at $16-$49/mo, Plus at about $99/mo, and Enterprise custom**. Most bill annually for the mid and upper tiers. Usage is metered by dynamic codes, scans, users and API calls.
- **Mobile scanner and generator apps:** ads plus one-time Pro IAPs at **$3.99-$14.99** (QRbot), or subscriptions at **$1.99-$9.99/week, $7.99/month, about $30-$80/year** (Scan-IT, SafeQR).
- **Data-capture tools:** **$20-$40 per user per month** (Orca Scan).

### 2.2 Store fees and billing mechanics

| Store | Commission (small developer) | Notes |
|---|---|---|
| Apple App Store and Mac App Store | **15%** under the Small Business Program (below $1M a year); auto-renewing subscriptions drop to 15% after the first year | Digital unlocks must use IAP. A Mac Catalyst app can share purchases with the iOS app through Universal Purchase. |
| Google Play | **15%** on the first $1M a year (enrolment required); **15% on subscriptions from day one** | Play policy forbids apps updating themselves outside Play, so **disable the GitHub update check in Play builds**. |
| Microsoft Store | Non-game apps may **use their own commerce and keep 100%**; the Microsoft commerce fee is low (5% default under the 2018 terms; check the current App Developer Agreement) | Individual developer registration has been free since Sept 2025 |
| Outside the stores (GitHub releases) | Merchant-of-record fees only | Use **offline-verifiable license keys**: an ECDSA P-256 signature checked against a public key embedded in the app, with `System.Security.Cryptography.ECDsa` built in. No backend needed. |

**Billing library:** `Plugin.InAppBilling` 11.x supports Android, iOS, Mac Catalyst and Windows. It uses StoreKit 2 on iOS and Mac Catalyst 15+, and `Windows.Services.Store` on Windows.

**Cross-store entitlement sync** (buy on Android, unlock on Windows) needs an account system. **RevenueCat** is free up to $2,500 monthly tracked revenue and then takes 1%. That makes it a 3rd-party backend and P2. Until then, sell per store and say so plainly.

### 2.3 Recommended QuickQr tiers

| Tier | Suggested price | Contents (detail in §3-§4) | Rationale |
|---|---|---|---|
| **Free** | $0, no ads, no account | All payload builders; all scanning; smart actions; on-device security checks; unlimited local history with search and favorites; PNG export; foreground and background colors; caption; app shortcuts and share target | Wins reviews and trust; matches the OS cameras and QRCode Monkey |
| **Pro** | **$9.99 one-time** (non-consumable), per store family | Logos, shapes, gradients and frames; templates; SVG/PDF and print export; generating non-QR symbologies; batch or continuous scanning; folders, tags and notes; CSV/JSON export, backup and restore; app lock; screen and PDF scanning; custom actions; widgets showing a code | Sits in QRbot's IAP range and far below weekly subscriptions. QuickQr has no running costs, so one-time pricing is credible. |
| **Business** | **$29.99 one-time**, or about $19.99 as an upgrade product for Pro owners | Everything in Pro, plus CSV/XLSX bulk generation, label sheets, inventory sessions with custom fields, webhook per scan, check-in list matching, GS1 tools, kiosk mode, keyboard-wedge or LAN scanner, desktop CLI, and BYO-key dynamic links (Bitly/Short.io) | About one year of Scan-IT, and a fraction of Orca Scan |
| *(Later)* **Cloud add-on** | Subscription | Dynamic QR, analytics, sync, Web Risk lookups | Only here does a server, and so a running cost, justify a subscription |

The stores offer no "upgrade pricing" for non-consumables. Model Business as its own product plus a discounted "Business upgrade" product that only Pro owners can see.

---

## 3. Feature catalog (sections A, B and C)

Each table lists the feature, a one-line description, the user value, priority, tier, feasibility and a 1-2 line implementation sketch.

### 3.1 Generation

| ID | Feature | Description | User value | Pri | Tier | Feasibility | Implementation sketch |
|---|---|---|---|---|---|---|---|
| G1 | Structured builders for every preset | Form fields per type (Wi-Fi, vCard, Email, SMS, Phone, URL, Geo, Event...) instead of free text | No syntax errors; codes work on every scanner | P0 | Free | on-device | `IPayloadBuilder` per type that emits strings per §5; unit-test every escape rule; add a "show raw payload" toggle |
| G2 | Live preview with a capacity meter | Shows bytes used, the QR version (for example "v7, 45×45") and the limit for the chosen error-correction level | Avoids oversized, hard-to-scan codes | P0 | Free | on-device | `Encoding.UTF8.GetByteCount`; `ZXing.QrCode.Internal.Encoder.encode(...)` returns a `QRCode` with `.Version` and `.Matrix`; warn above version 10 for print |
| G3 | Error correction, quiet zone, size | L/M/Q/H picker, a margin of 4 modules or more, output pixels | Reliability and print fit | P0 | Free | on-device | `QrCodeEncodingOptions { ErrorCorrection, Margin, Width, Height }`; force H when a logo is present |
| G4 | Correct Unicode handling | UTF-8 encoding with an optional ECI segment | Non-Latin text (Hindi, Arabic, emoji) decodes correctly | P0 | Free | on-device | `CharacterSet = "UTF-8"`; offer a `DisableECI` compatibility toggle; self-test decode (S2) |
| G5 | Calendar event (VEVENT) | Title, start and end (all-day), location, description | Event invitations on posters | P0 | Free | on-device | §5.4 builder; convert times to UTC; fold nothing |
| G6 | Generate from clipboard or shared text | One tap turns copied text or a URL into a QR | Speed (top use on desktop) | P0 | Free | on-device | `Clipboard.GetTextAsync()`; the share-target entry point (§3.8, item P2) routes into the generator |
| G7 | Messaging links | WhatsApp (wa.me), Telegram (t.me), plus Signal and Viber as URL templates | Very common small-business use | P1 | Free | on-device | URL templates per §5.13-5.14 with phone normalization to E.164 digits |
| G8 | Payment codes | EPC/GiroCode (SEPA), UPI, PayPal.me; Swiss QR-bill and EMVCo/Pix in P2 | Invoices, donations, shops | P1 | Free | on-device | Validators for IBAN mod-97, BIC length, 331 bytes at EC M (EPC); UPI `pa`/`pn` required |
| G9 | "My codes" quick cards | Pinned personal vCard, home Wi-Fi, payment code | Show a code instantly to a guest | P1 | Free | on-device | Favorite generated items in history (H3) with a full-screen "present" mode that raises brightness |
| G10 | Other symbologies | Data Matrix, Aztec, PDF417, Code 128/39/93, EAN/UPC, ITF, Codabar | Labels, tickets, retail | P1 | Pro | on-device | `MultiFormatWriter` with per-format validators from §7; aspect-ratio defaults for 1D |
| G11 | Crypto requests | BIP21/321 bitcoin:, EIP-681 ethereum: | Donations and payments | P2 | Free | on-device | Base58Check, bech32/bech32m and EIP-55 checksum validation; amount in BTC or wei |
| G12 | App-store and social presets | App Store, Play, Microsoft Store, Instagram, YouTube, LinkedIn | Marketing convenience | P2 | Free | on-device | URL templates (§5.15); validate the package name or numeric id |
| G13 | GS1 builders | GS1 Digital Link QR, GS1-128 and GS1 DataMatrix with AI parsing and check digits | Retail and pharma labels (the GS1 2D "Sunrise 2027" migration) | P2 | Business | on-device | Build the AI string with FNC1 (`GS1Format = true`); Digital Link URI `https://id.gs1.org/01/{GTIN14}/10/{lot}` |
| G14 | Compact encoding | Uppercasing a URL's scheme and host to use alphanumeric mode, or a minimal mixed-mode hint | Smaller, more scannable codes | P2 | Free | on-device | Only when the target server is case-insensitive; compare the version before and after; ZXing's "QR compact" hint where available |

### 3.2 Styling and export

| ID | Feature | Description | User value | Pri | Tier | Feasibility | Implementation sketch |
|---|---|---|---|---|---|---|---|
| S1 | Foreground and background colors with a contrast check | Color pickers; warns on low contrast or inverted colors | Personalisation without broken codes | P0 | Free | on-device | Compute the WCAG contrast ratio and warn below 4.5:1 or when the foreground is lighter than the background |
| S2 | **Scannability self-test** | Decodes the rendered image with ZXing after every style change | Guarantees the styled code still scans | P0 | Free | on-device | Render to `SKBitmap`, then `BarcodeReader.Decode`; show a "Scan check passed" or failed badge |
| S3 | Caption or label text | Text under the code (for example the SSID or "Scan to pay") | Printable Wi-Fi cards and signage | P1 | Free | on-device | SkiaSharp text below the matrix; include it in the export bounds |
| S4 | Logo in the center | Image overlay limited to about 20% of the area, with EC forced to H | Branding | P1 | Pro | on-device | Draw the matrix, then clear modules under the logo plus padding; S2 confirms it decodes |
| S5 | Module and finder-eye shapes and colors | Dots, rounded or classy modules; custom eye frame and ball | Brand look | P1 | Pro | on-device | Iterate `ByteMatrix`; skip the three 7×7 finder regions and draw custom paths for them |
| S6 | Frames and call-to-action templates | Frames with "Scan me" text, a gallery of presets | Higher scan rates on posters | P1 | Pro | on-device | Vector frame templates (SkiaSharp paths) parameterized by text and color |
| S7 | Gradients and transparent background | Linear or radial gradient fills; PNG alpha | Design flexibility | P2 | Pro | on-device | `SKShader.CreateLinearGradient` as the paint shader; warn on transparent backgrounds that will be shown on dark surfaces |
| S8 | Saved styles and brand kit | Save and apply style presets | Consistency across many codes | P2 | Pro | on-device | Serialize the style to JSON in SQLite; apply it in bulk (B1) |
| S9 | **Vector export** (SVG, PDF; EPS in P2) | Resolution-independent output | Print shops, designers | P1 | Pro | on-device | Same draw routine on `SKSvgCanvas.Create(...)` or `SKDocument.CreatePdf(stream)`; SVG can also be emitted directly from the matrix |
| S10 | Print-resolution PNG, JPEG or WebP, with DPI | Up to 1024 px free; up to 4096 px at 300/600 DPI in Pro | Print quality | P1 | Free / Pro | on-device | `SKImage.Encode(format, quality)`; write the DPI to PNG pHYs metadata |
| S11 | Copy image and drag out | Copy the QR to the clipboard; drag into documents on desktop | Fast workflow | P1 | Free | on-device | Windows `DataPackage.SetBitmap`; iOS/macOS `UIPasteboard`; Android uses the share sheet (a clipboard image needs a content URI) |
| S12 | Print | System print dialog; printable Wi-Fi or business cards | Offline sharing | P1 | Free (basic) / Pro (templates) | on-device | Android `PrintManager`; iOS `UIPrintInteractionController`; Windows `PrintManager` or the PDF path |

### 3.3 Scanning

| ID | Feature | Description | User value | Pri | Tier | Feasibility | Implementation sketch |
|---|---|---|---|---|---|---|---|
| R1 | Multi-format live scanning with a format filter | QR plus all ZXing-readable formats, configurable | Faster and fewer false reads | P0 | Free | on-device | `PossibleFormats` from settings; only enable MSI, Pharmacode and IMb explicitly (§7) |
| R2 | Camera controls | Torch, pinch or slider zoom, tap to focus, camera switch | Low light and small codes | P0 | Free | on-device | Platform camera controls (ZXing.Net.Maui `IsTorchOn`, camera location); a custom view on Windows |
| R3 | Feedback settings | Beep, vibrate, auto-copy, continuous vs single scan | Comfort and accessibility | P0 | Free | on-device | `HapticFeedback.Perform`; short audio clip; `Preferences` |
| R4 | Multiple codes in one image | Detects every code in a still image and lets the user pick | Documents with several codes | P1 | Free | on-device | `reader.DecodeMultiple(bitmap)` (GenericMultipleBarcodeReader); overlay boxes from `ResultPoints` |
| R5 | Paste, drop or clipboard image scanning | Ctrl+V or drag an image or screenshot onto the window | Desktop: scanning a QR from an email | P1 | Free | on-device | Windows `Clipboard.GetContent().GetBitmapAsync()`; MAUI `DropGestureRecognizer`; decode with SkiaSharp |
| R6 | Inverted and damaged codes | Light-on-dark, mirrored and low-contrast codes | Fewer failures | P1 | Free | on-device | `TryInverted = true`, `TryHarder` for still images, `HybridBinarizer` with a global fallback, upscaling for tiny codes |
| R7 | **Batch or continuous scanning** | Keeps scanning with dedupe, a counter and a list view | Inventory, events, collecting codes | P1 | Pro | on-device | Debounce the same text for N seconds; session table in SQLite; sound per new item |
| R8 | **Screen scanning on desktop** | Scans a QR shown on screen (2FA setup, meeting links) | A killer desktop use case | P1 | Pro | on-device | Windows: launch `ms-screenclip:`, then read the clipboard bitmap; macOS: guide the user to Ctrl+Shift+Cmd+4, then auto-scan the clipboard |
| R9 | PDF scanning | Decodes codes on PDF pages (tickets, invoices with EPC QR) | Paperless workflows | P2 | Pro | on-device | Android `PdfRenderer`, iOS/macOS `PDFKit`, Windows `Windows.Data.Pdf`; rasterize at 200-300 DPI, then decode |
| R10 | Aim box and region of interest | Restricts decoding to the center box; a hint when the code is too small | Precision in crowded labels | P2 | Free | on-device | Crop frames before decoding; this also improves speed |
| R11 | Native engines (optional) | ML Kit (Android) and Apple Vision (iOS/macOS) for speed and more symbologies | Better low-light performance; Micro QR on Apple | P2 | Free | on-device | `BarcodeScanning.Native.Maui`, or a platform-specific scanner service behind an interface |
| R12 | GS1 parsing | Parses Application Identifiers (01 GTIN, 17 expiry, 10 lot, 21 serial) | Retail and pharma | P2 | Business | on-device | FNC1/GS separator parsing table; Digital Link URI parser |

### 3.4 Smart actions

| ID | Feature | Description | User value | Pri | Tier | Feasibility | Implementation sketch |
|---|---|---|---|---|---|---|---|
| A1 | Typed result cards | Parses every format in §5 into a card with fields and actions | Understand before acting | P0 | Free | on-device | Ordered parser chain (§5.18), with a `ParsedResult` subclass per type |
| A2 | **URL preview sheet** | Never auto-opens by default; shows the emphasized domain and verdict (§6) | Phishing defence | P0 | Free | on-device | Bottom sheet with eTLD+1 in bold, the full URL collapsible, and Open, Copy and Share |
| A3 | Open in a protected browser | System browser or in-app tab | Gets the browser's own phishing protection | P1 | Free | on-device | `Browser.OpenAsync(uri, BrowserLaunchMode.SystemPreferred)` (Custom Tabs or SFSafariViewController) |
| A4 | Add a contact | vCard/MeCard to the OS contacts UI | One-tap save | P0 | Free | on-device | Android `ContactsContract.Intents.Insert`; iOS `CNContactVCardSerialization` and `CNContactViewController`; desktop: write a `.vcf` and open it |
| A5 | Add a calendar event | VEVENT to the calendar UI | One-tap save | P1 | Free | on-device | Android `Intent.ActionInsert` with `CalendarContract`; iOS `EKEventEditViewController`; desktop: open a `.ics` |
| A6 | Join Wi-Fi | Connects to the network, with copying the password as a fallback | The top consumer QR use | P1 | Free | on-device | Android 11+ `Settings.ACTION_WIFI_ADD_NETWORKS` with a suggestion list; iOS `NEHotspotConfiguration` (needs the Hotspot Configuration entitlement); Windows `WiFiAdapter.ConnectAsync` (`wiFiControl` capability) |
| A7 | Payment code display | Shows EPC beneficiary, IBAN and amount, UPI payee and amount, crypto address and amount, each with copy buttons | Checks before paying | P1 | Free | on-device | Large recipient and amount display; hand UPI and crypto off to wallets via `Launcher.OpenAsync(upi://...)` |
| A8 | Sensitive-content guard | Detects otpauth://, Wi-Fi passwords, BIP39 seed phrases, private keys (WIF/hex) | Prevents leaking secrets | P1 | Free | on-device | Pattern detectors; mask by default; exclude from history unless opted in; never auto-copy |
| A9 | Product lookup | EAN/UPC/ISBN to Open Food Facts, Open Library or a web search | Shopping and books | P2 | Free | on-device (net) | Optional HTTP call to the public API; otherwise just open a search URL |
| A10 | Custom actions | User-defined URL templates such as `https://inventory.local/item/{code}` | Power users and workflows | P2 | Pro | on-device | Templates are URL-encoded and run through the security check before opening |

### 3.5 History and data

| ID | Feature | Description | User value | Pri | Tier | Feasibility | Implementation sketch |
|---|---|---|---|---|---|---|---|
| H1 | **Local history** of scans and generated codes | Chronological list; reopen or regenerate | Find that code again | P0 | Free | on-device | SQLite (`sqlite-net-pcl`) `HistoryItem` table (schema in §8) |
| H2 | Search and filters | Full-text search; filter by type, format or date | Large histories stay usable | P0 | Free | on-device | SQLite FTS5 virtual table over title, raw text and notes |
| H3 | Favorites and pins | Star items; pinned items appear on the home screen | Quick reuse | P0 | Free | on-device | `IsFavorite` column with an index |
| H4 | History privacy controls | History on or off, incognito scan, auto-delete after N days, exclude sensitive types | Trust | P1 | Free | on-device | Settings plus a cleanup job at app start |
| H5 | Folders, tags and notes | Organise items; add notes | Projects and inventory | P1 | Pro | on-device | `Tag` and `ItemTag` tables; a notes column |
| H6 | Bulk export | CSV, JSON or XLSX of all or selected items (sharing a single item stays free) | Spreadsheets and records | P1 | Pro | on-device | `CsvHelper`, or hand-rolled RFC 4180 output; XLSX via MiniExcel or ClosedXML; export through the share sheet |
| H7 | Backup and restore | Zip of the database and images; import it on another device | Device migration without a cloud | P1 | Pro | on-device | `System.IO.Compression`; versioned manifest; merge by GUID on import |
| H8 | App lock | Biometric or PIN gate for history | Protects Wi-Fi passwords and private codes | P2 | Pro | on-device | Android BiometricPrompt, iOS `LAContext`, Windows Hello `UserConsentVerifier` |
| H9 | Scan count and duplicate merge | Repeat scans increment a counter | Clean history | P2 | Free | on-device | Upsert on a hash of format plus raw text |
| H10 | Location and time stamps | Opt-in GPS per scan | Field audits | P2 | Business | on-device | MAUI `Geolocation.GetLastKnownLocationAsync`; explicit opt-in |

### 3.6 Security (summary; detail in §6)

| ID | Feature | Description | User value | Pri | Tier | Feasibility | Implementation sketch |
|---|---|---|---|---|---|---|---|
| X1 | **On-device URL risk analyzer** | Rule-based checks with weighted scores and explanations | Catches most quishing tricks offline | P0 | Free | on-device | `UrlRiskAnalyzer` in §6.2 plus a bundled data JSON (TLDs, shorteners, brands) |
| X2 | IDN and punycode homograph detection | Mixed or whole-script confusables and brand skeleton matching | Stops look-alike domains | P0 | Free | on-device | `IdnMapping`; script classification; Unicode TR39 `confusables.txt` subset |
| X3 | Dangerous scheme blocking | `javascript:`, `data:`, `file:`, `intent:`, `content:`, `vbscript:` | Blocks code execution and file access | P0 | Free | on-device | Allow-list of schemes; anything else is "copy only" |
| X4 | Shortener detection and opt-in expansion | Flags bit.ly-style links; "Reveal destination" | Shows where a link really goes | P1 | Free | on-device (net) | HEAD (falling back to GET), no automatic redirects, at most 5 hops, no cookies, 5 s timeout; each hop goes through X1 |
| X5 | Payment and crypto sanity checks | Checksum validation; irreversible-payment warnings | Reduces fraud loss | P1 | Free | on-device | Validators from §5; a red banner for payment intents found in unexpected places |
| X6 | Protected preview | Opens the page in a WebView with platform reputation checks | Kaspersky-style preview | P2 | Free | on-device | Android WebView Safe Browsing (on by default), WKWebView fraudulent-site warning, WebView2 SmartScreen |
| X7 | Reputation lookup (Web Risk) | Google threat-list verdicts | Catches known-bad sites | P2 | Pro (later, cloud add-on) | needs backend | Proxy service with app attestation; see §6.5 for terms and pricing |

### 3.7 UX, accessibility and localization

| ID | Feature | Description | User value | Pri | Tier | Feasibility | Implementation sketch |
|---|---|---|---|---|---|---|---|
| U1 | Settings page | Theme, language, feedback, open behavior, history, security options, defaults (EC level, size) | Basic expectation | P0 | Free | on-device | `Preferences` behind a typed settings service |
| U2 | Light, dark and system theme | AppThemeBinding; codes are always rendered dark on light | Comfort | P0 | Free | on-device | `Application.Current.UserAppTheme`; keep the code canvas white by default |
| U3 | Screen-reader support | Labels and hints; announces scan results | Blind and low-vision users | P0 | Free | on-device | `SemanticProperties.Description` and `Hint`; `SemanticScreenReader.Announce("Wi-Fi code: HomeNet")` |
| U4 | Dynamic type and large targets | Respects OS font scaling; 44-48 pt targets; reduced motion | Accessibility | P0 | Free | on-device | Avoid fixed heights; use `FontAutoScalingEnabled`; test at 200% scale |
| U5 | Onboarding and permission rationale | Explains camera use; fallback when permission is denied (scan an image instead) | Fewer dead ends | P0 | Free | on-device | `Permissions.CheckStatusAsync<Camera>`; deep link to app settings |
| U6 | Privacy promise | No ads; no analytics, or opt-in only; offline statement | Trust is the differentiator | P0 | Free | on-device | In-app privacy page; store privacy labels showing no data collected |
| U7 | Desktop keyboard shortcuts | Ctrl/Cmd+V to scan an image, Ctrl+S save, Ctrl+C copy, Ctrl+N new | Desktop productivity | P1 | Free | on-device | `KeyboardAccelerator` on MenuFlyoutItems (MAUI menus) |
| U8 | Localization and RTL | resx strings; right-to-left layout | Wider markets | P1 | Free | on-device | `AppResources.resx` per culture; `FlowDirection`; start with EN, ES, PT-BR, DE, FR, HI, ID, JA, AR, picked for QR- and payment-heavy markets |
| U9 | Haptics and sound cues | Vibrate on success, distinct error tone | Accessibility | P1 | Free | on-device | `HapticFeedback`; a short WAV |
| U10 | Audio aiming guidance | Pitch or tempo changes as the code nears the center (as VIP Code Reader does) | Blind users | P2 | Free | on-device | Track the result-point centroid against the frame center and map distance to beep rate |
| U11 | Update check per channel | Keep the GitHub check only for sideloaded and desktop builds | Store compliance | P0 | Free | on-device | `#if` or a build property such as `STORE_BUILD` that disables the update check |

### 3.8 Platform integrations

| ID | Feature | Description | User value | Pri | Tier | Feasibility | Implementation sketch |
|---|---|---|---|---|---|---|---|
| P1 | **App shortcuts** (Scan, New QR, My Wi-Fi, History) | Long-press the icon on Android or iOS; jump list on Windows | One-tap entry | P0 | Free | on-device | MAUI `AppActions` (`AddAppAction` / `OnAppAction`); Android needs the intent filter plus `OnResume`/`OnNewIntent` |
| P2 | **Android share target** | Share text or a URL to generate a QR; share an image to scan it | Works from any app | P0 | Free | on-device | `[IntentFilter(new[]{Intent.ActionSend}, DataMimeTypes = new[]{"text/plain","image/*"})]` on an Activity |
| P3 | Android Quick Settings tile | A "Scan QR" tile | Faster than opening the app | P1 | Free | on-device | Subclass `TileService` in Platforms/Android; launch the scan page with a flag |
| P4 | URL scheme for automation | `quickqr://generate?text=...`, `quickqr://scan` | Shortcuts and Tasker automation | P1 | Free | on-device | Register the scheme per platform; route through the AppShell URI parser; never auto-execute payloads |
| P5 | Android home widget | "Scan" button (Free) or a widget showing a saved QR (Pro) | Guest Wi-Fi or personal card on the home screen | P1 | Free / Pro | on-device | `AppWidgetProvider` with RemoteViews; render the QR bitmap to the app's files (Microsoft's 2026 MAUI Android widget guide) |
| P6 | iOS and macOS share extension | Share text or an image from Safari or Photos | Parity with Android | P2 | Free | on-device | Separate extension target (native or .NET); share an App Group container |
| P7 | iOS widgets and Control Center control | Home or Lock Screen widget showing a saved QR; iOS 18 control | Speed | P2 | Pro | on-device | WidgetKit extension in Swift (needs a Mac; see Microsoft's Dec 2025 iOS widget post); data through an App Group |
| P8 | Windows share target | Receive shared text and images | Desktop integration | P2 | Free | on-device | MSIX `ShareTarget` declaration; `ShareOperation` activation |
| P9 | Siri Shortcuts and App Intents; Android App Actions | "Make a QR of my clipboard" | Voice and automation | P2 | Pro | on-device | App Intents need a Swift extension; Android `shortcuts.xml` capabilities |
| P10 | Tray or menu bar quick generate | A global hotkey generates a QR from the clipboard | Desktop power users | P2 | Pro | on-device | Windows: a tray icon via WinUI interop and `RegisterHotKey`; macOS: `NSStatusItem` (needs AppKit bridging) |
| P11 | Apple Wallet and Google Wallet passes | Store a QR as a pass | Tickets and loyalty | P2 | Business | **needs backend** | `.pkpass` signing needs your Pass Type certificate's private key; a Google Wallet JWT needs a service-account key. Neither key belongs in a client. |

### 3.9 Business and productivity (all on-device)

| ID | Feature | Description | User value | Pri | Tier | Feasibility | Implementation sketch |
|---|---|---|---|---|---|---|---|
| B1 | **Bulk generation from CSV or XLSX** | Template plus rows, output as a ZIP of PNG/SVG or one PDF | Hundreds of codes in seconds | P1 | Business | on-device | Parse the CSV; column mapping UI; `{column}` placeholders; render in parallel; stream into a `ZipArchive` |
| B2 | Label and sheet printing | A4 and Letter sheets, Avery-style grids, captions | Asset tags, shelf labels | P1 | Business | on-device | `SKDocument.CreatePdf`; grid layout engine; mm-accurate margins |
| B3 | **Inventory sessions** | Named sessions with custom fields, quantity and notes; export to XLSX | Replaces Orca or Scan-IT for small shops | P1 | Business | on-device | `Session` and `SessionRow` tables; field schema stored as JSON; continuous scanning (R7) |
| B4 | **Webhook per scan** | POST JSON to a user-configured URL (as in QRbot's business mode) | Integrates with Sheets, Zapier, internal APIs, without a QuickQr backend | P1 | Business | on-device (net) | `HttpClient` POST with retries and a queued outbox in SQLite; optional HMAC header with a user secret |
| B5 | Check-in or verification mode | Import a list (CSV) and show valid, duplicate or unknown for each scan | Events, tickets, attendance | P2 | Business | on-device | Hash set lookup; big green or red full-screen result; audit log |
| B6 | Keyboard wedge or LAN scanner | The phone scans and the text is typed into the active desktop app over the local network | Turns a phone into a USB-style scanner | P2 | Business | on-device (LAN) | Desktop listens on a local WebSocket; pair by QR; the desktop types with `SendInput` (Windows) or CGEvent (macOS; needs Accessibility permission) |
| B7 | GS1 tools | GTIN and SSCC check digits, GS1-128 and DataMatrix with AIs, Digital Link | Retail compliance | P2 | Business | on-device | See G13 and R12 |
| B8 | Kiosk mode | A locked continuous-scan screen with an optional exit PIN | Reception desks | P2 | Business | on-device | Full-screen page; Android `startLockTask` (screen pinning); iOS Guided Access instructions |
| B9 | Desktop CLI | `quickqr gen --type wifi --ssid X --out a.svg` | Scripting and CI | P2 | Business | on-device | Separate console project sharing the payload and render libraries; check the license file |
| B10 | BYO-key dynamic links | Creates or edits Bitly or Short.io links in the user's own account, then makes the QR | "Dynamic QR" without running a server | P2 | Business | 3rd-party backend | Store the user's API token in `SecureStorage`; call the vendor REST API; show their scan statistics if the API exposes them |

### 3.10 Backend-dependent features (defer; listed for completeness)

| ID | Feature | Description | User value | Pri | Tier | Feasibility | Implementation sketch |
|---|---|---|---|---|---|---|---|
| N1 | Dynamic QR (own redirect) | Short URL whose destination can be edited | Fix mistakes after printing | P2 | Business (subscription) | needs backend | Redirect service, for example on Cloudflare Workers with KV; abuse handling; domain reputation management |
| N2 | Scan analytics | Counts, time, rough geography and device per dynamic code | Marketing ROI | P2 | Business (subscription) | needs backend | Log at the redirect; privacy-preserving aggregation |
| N3 | Cloud sync | History and styles across devices | Multi-device users | P2 | Pro (subscription) | needs backend (or iCloud/Drive per platform) | CloudKit on Apple only; Google Drive appData folder; otherwise your own sync API |
| N4 | Teams and workspaces | Shared folders, roles, SSO | Companies | P2 | Business | needs backend | Out of scope for a solo developer |
| N5 | Public REST API | Programmatic generation | Developers | P2 | Business | needs backend | Use the desktop CLI (B9) instead |
| N6 | Hosted landing pages | vCard page, PDF, menu, link-in-bio | Rich mobile pages | P2 | Business | needs backend | Hosting, content moderation, abuse risk |
| N7 | Cross-store entitlements | Buy once, unlock on every platform | Fairness | P2 | n/a | 3rd-party backend | RevenueCat (free to $2.5k MTR) plus a sign-in |
| N8 | Web Risk verdicts | See X7 and §6.5 | Known-threat blocking | P2 | Pro | needs backend | Proxy to protect the key; billing |

---

## 4. Prioritized Free / Pro / Business split

| Area | **Free** | **Pro** (about $9.99 one-time) | **Business** (about $29.99 one-time) |
|---|---|---|---|
| Generate | **P0:** builders for all presets (Text, URL, Email, Phone, SMS, Wi-Fi, vCard/MeCard, Geo, Event), capacity meter, EC/margin/size, UTF-8, clipboard and share input. **P1:** WhatsApp/Telegram, EPC/UPI/PayPal.me, "My codes". **P2:** crypto, app-store and social links, compact mode | **P1:** Data Matrix, Aztec, PDF417, Code 128/39/93, EAN/UPC, ITF, Codabar | **P1:** CSV/XLSX bulk generation. **P2:** GS1 Digital Link, GS1-128 and GS1 DataMatrix |
| Style and export | **P0:** colors with contrast check, scannability self-test, PNG up to 1024 px. **P1:** caption, copy image, basic print | **P1:** logo, module and eye shapes, frames and CTA, SVG/PDF, print-DPI PNG, print templates. **P2:** gradients, transparency, brand kit | **P1:** label sheets and Avery grids (PDF) |
| Scan | **P0:** multi-format live scan, torch/zoom/focus, feedback settings, image file scan. **P1:** multiple codes per image, paste/drop scanning, inverted and damaged codes. **P2:** aim box, native engines | **P1:** batch or continuous scanning, screen scanning (desktop). **P2:** PDF scanning | **P1:** inventory sessions. **P2:** GS1 AI parsing, check-in mode, kiosk mode, LAN keyboard wedge |
| Act | **P0:** typed result cards, URL preview sheet, add contact. **P1:** protected browser, calendar, join Wi-Fi, payment display, sensitive-content guard. **P2:** product lookup | **P2:** custom URL actions | **P1:** webhook per scan |
| History | **P0:** unlimited local history, search, favorites. **P1:** privacy controls (off, incognito, auto-delete). **P2:** duplicate merge | **P1:** folders, tags and notes; CSV/JSON/XLSX export; backup and restore. **P2:** app lock | **P2:** GPS and time stamps |
| Security | **P0:** URL risk analyzer, IDN homograph detection, scheme blocking. **P1:** shortener reveal (opt-in), payment checks. **P2:** protected preview | **P2, later:** Web Risk reputation (needs backend) | n/a |
| UX and platform | **P0:** settings, themes, screen reader, dynamic type, onboarding, privacy promise, app shortcuts, Android share target, per-channel update check. **P1:** shortcuts on desktop, localization and RTL, haptics, QS tile, URL scheme, Android "Scan" widget. **P2:** audio aiming, iOS and Windows share targets | **P1:** Android widget showing a QR. **P2:** iOS widgets and controls, Siri and App Actions, tray or menu bar hotkey | **P2:** desktop CLI, BYO-key dynamic links (Bitly/Short.io) |
| Backend (defer) | n/a | Cloud sync (subscription) | Dynamic QR, analytics, teams, API, landing pages, Wallet passes (subscription) |

**Suggested build order:** settings, history, then the URL preview and analyzer (P0 block). Next: payload builders and validators, the self-test, and AppActions plus the share target. Then the Pro styling and vector export, batch scanning, and export and backup. Business features come last.

---

## 5. Payload format specifications (section D)

### 5.0 Encoding, capacity and error correction

- **Character set:** QR byte mode is interpreted as ISO-8859-1 by default. UTF-8 can be signalled with an ECI segment (ECI 26), and most phone readers also detect UTF-8 heuristically. In ZXing.Net, set `CharacterSet = "UTF-8"`; it adds an ECI header unless `DisableECI = true`. **Recommendation:** use ASCII-only payloads where possible. For non-ASCII text use UTF-8, default to omitting the ECI (the most common generator output), offer "Add ECI" as an advanced option, and always run the self-test decode.
- **Uppercase trick:** alphanumeric mode (0-9, A-Z, space, `$%*+-./:`) is about 1.4× denser than byte mode. ZXing's wiki notes that uppercasing a URL helps only if the server accepts it. BIP321 explicitly expects all-uppercase bitcoin URIs.
- **Maximum capacity at version 40 (177×177 modules, ISO/IEC 18004):**

| EC level (recoverable) | Numeric | Alphanumeric | **Byte (8-bit)** | Kanji |
|---|---|---|---|---|
| **L** (about 7%) | 7,089 | 4,296 | **2,953** | 1,817 |
| **M** (about 15%) | 5,596 | 3,391 | **2,331** | 1,435 |
| **Q** (about 25%) | 3,993 | 2,420 | **1,663** | 1,024 |
| **H** (about 30%) | 3,057 | 1,852 | **1,273** | 784 |

- **Practical limits:** for phone scanning of printed codes keep payloads at about 300 bytes or less (around version 10, 57×57, or below). Use EC **M** by default, **H** with a logo, and **L** for very long payloads on screens. **EPC requires M.** The quiet zone is 4 modules for QR and 2 for Micro QR. A rule of thumb for print size is code width ≥ scanning distance / 10.

### 5.1 Wi-Fi: `WIFI:`

```
WIFI:T:<auth>;S:<ssid>;P:<password>;H:true;;
```

| Field | Values and rules |
|---|---|
| `T` | `WPA` (WPA, WPA2 and WPA3-Personal transition networks), `WEP`, `nopass` (or omit T and P). `WPA2-EAP` for enterprise, with `E:` (EAP method), `A:` (anonymous identity), `I:` (identity) and `PH2:` (phase-2). De facto **`SAE`** means WPA3-Personal (Android's own share sheet emits `T:SAE`). Emit `WPA`, not `WPA2`, because some parsers reject unknown values. |
| `S` | SSID, required |
| `P` | Password; omit for `nopass` |
| `H` | `true` only for hidden SSIDs; omit otherwise. iOS does not support hidden networks from QR. |
| WPA3 spec (Wi-Fi Alliance, section 7) | `R:<hex>` Transition Disable bitmap: 1 = WPA3-Personal only (SAE), 2 = SAE-PK only, 4 = WPA3-Enterprise, 8 = Enhanced Open. `K:` SAE-PK public key (base64 DER). `I:` SAE password identifier. Parsers must ignore unknown fields. The spec form for WPA3-only is `T:WPA;R:1`. |
| Escaping | ZXing convention: **backslash-escape `\` `;` `,` `"` `:`**. The WPA3 spec instead says to percent-encode semicolons and non-printables, but Android uses backslash escapes and does not decode percent-encoding. **Generate with backslash escapes.** Don't wrap values in quotes; ZXing only uses quotes to stop an SSID being read as hex. |
| Order and terminator | Any field order; end with `;;` |

**Generator policy:** "WPA/WPA2/WPA3" emits `T:WPA`. An advanced "WPA3-only" option offers either `T:SAE` (Android-style) or `T:WPA;R:1` (spec). **Parser policy:** accept WPA, WPA2, WPA3, SAE, WEP, nopass, WPA2-EAP or an empty T; unescape backslashes; strip quotes around a value; ignore R, K, I and unknown fields.

Sample (SSID `Cafe;Guest`, password `pa"ss:wo\rd,1`):
```
WIFI:T:WPA;S:Cafe\;Guest;P:pa\"ss\:wo\\rd\,1;;
```
WPA3-only (spec form): `WIFI:T:WPA;R:1;S:HomeNet;P:correct horse battery;;`

### 5.2 MECARD (NTT DoCoMo; no formal standard)

```
MECARD:N:<last>,<first>;SOUND:<reading>;TEL:<num>;TEL-AV:<num>;EMAIL:<addr>;NOTE:<memo>;BDAY:<YYYYMMDD>;ADR:<pobox>,<room>,<street>,<city>,<region>,<postal>,<country>;URL:<url>;NICKNAME:<nick>;;
```

- Fields are separated by `;`, each tag and value by `:`, and the record ends with `;;`. Use UTF-8 in practice. `TEL`, `EMAIL` and `URL` may repeat. `ORG` is not in DoCoMo's list but is widely parsed, and ZXing emits it. `N` and `ADR` components are comma-separated.
- **Escaping:** ZXing's encoder backslash-escapes `\` `:` `;` and **strips newlines**. segno also escapes `"`. The archived DoCoMo spec escapes `\ ; : ,`. **Recommendation:** escape `\ ; :` always, and `,` inside N, SOUND and ADR components. Replace newlines with spaces.
- Prefer vCard 3.0 for maximum compatibility, since Apple documents vCard support. MECARD is the compact option, often 40-50% smaller.

Sample:
```
MECARD:N:Doe,John;TEL:+12125551212;EMAIL:john.doe@example.com;ORG:ACME\; Inc.;ADR:,,123 Main St.,Springfield,IL,62701,USA;URL:https\://example.com;NOTE:Desk 4;;
```

### 5.3 vCard 3.0 (RFC 2426)

- **Required:** `BEGIN:VCARD`, `VERSION:3.0`, `N`, `FN`, `END:VCARD`.
- **Line ending:** CRLF per the RFC. ZXing emits LF and most readers accept both; prefer CRLF for strictness, or LF to save bytes.
- **Structured values:** `N` is Family;Given;Additional;Prefix;Suffix. `ADR` is POBox;Ext;Street;Locality;Region;Postal;Country. `ORG` is Org;Unit.
- **Text escaping:** backslash `\\`, comma `\,`, semicolon `\;` (inside component text), newline `\n` (or `\N`). Colons are **not** escaped. Parameters look like `TEL;TYPE=CELL,VOICE:` and `EMAIL;TYPE=INTERNET:`.
- **Line folding:** lines longer than 75 octets SHOULD be folded with CRLF followed by one space or tab, and unfolded by removing CRLF plus that whitespace. In QR codes, **don't fold**: it wastes bytes and every reader accepts long lines. If you fold, never split a UTF-8 multibyte sequence. Parsers should still unfold.
- Avoid `PHOTO` (size). vCard 4.0 (RFC 6350) works on iOS, but 3.0 is the safest choice.

Sample:
```
BEGIN:VCARD
VERSION:3.0
N:Doe;John;;;
FN:John Doe
ORG:ACME\, Inc.
TITLE:Engineer
TEL;TYPE=CELL:+1-212-555-1212
EMAIL;TYPE=INTERNET:john.doe@example.com
ADR;TYPE=WORK:;;123 Main St.;Springfield;IL;62701;USA
URL:https://example.com
NOTE:Line one\nLine two
END:VCARD
```

### 5.4 iCalendar VEVENT (RFC 5545)

- **Strictly valid:** a `VCALENDAR` with `VERSION:2.0` and `PRODID`, containing a `VEVENT` with `UID` and `DTSTAMP`. `DTSTART` is required when there is no `METHOD`.
- **QR practice:** ZXing and the iOS camera accept a bare `BEGIN:VEVENT ... END:VEVENT` (this is the ZXing wiki example). Offer **Strict** (the default) and **Compact** variants.
- **Dates:** UTC `YYYYMMDDTHHMMSSZ` (recommended); floating local time `YYYYMMDDTHHMMSS`; `TZID=` needs a `VTIMEZONE` component, so avoid it in QR. All-day events use `DTSTART;VALUE=DATE:20261205` with an *exclusive* `DTEND;VALUE=DATE:20261206`.
- **TEXT escaping:** `\\`, `\;`, `\,`, `\n`. Fold at 75 octets as for vCard (don't fold in QR). Use CRLF line endings.

Strict sample:
```
BEGIN:VCALENDAR
VERSION:2.0
PRODID:-//QuickQr//EN
BEGIN:VEVENT
UID:20261121T090000Z-7f3a@quickqr
DTSTAMP:20261121T090000Z
DTSTART:20261205T170000Z
DTEND:20261205T183000Z
SUMMARY:Team dinner
LOCATION:Main St. 1\, Springfield
DESCRIPTION:Bring a gift\nDress code: casual
END:VEVENT
END:VCALENDAR
```
Compact sample: `BEGIN:VEVENT\nSUMMARY:Team dinner\nDTSTART:20261205T170000Z\nDTEND:20261205T183000Z\nEND:VEVENT`

### 5.5 Email: `mailto:` (RFC 6068) and `MATMSG`

- **mailto:** `mailto:<addr>[,<addr>...][?subject=<v>&body=<v>&cc=<addr>&bcc=<addr>]`. Percent-encode the UTF-8 bytes of non-ASCII and reserved characters (`% & = ? # +` and space). **Encode space as `%20`, never `+`**. Encode body line breaks as `%0D%0A`.
  Sample: `mailto:info@example.com?subject=Order%20%23123&body=Hi%2C%0D%0APlease%20call%20me.`
- **MATMSG (DoCoMo):** `MATMSG:TO:<addr>;SUB:<subject>;BODY:<body>;;`. Escape `\ ; :` (and `,`) with a backslash, as for MECARD.
  Sample: `MATMSG:TO:info@example.com;SUB:Order #123;BODY:Hi\, please call me.;;`
- **Generate `mailto:`** (universal). **Parse both**, plus the legacy `SMTP:addr:subject:body`.

### 5.6 SMS: `SMSTO:` and `sms:` (RFC 5724)

- **RFC 5724:** `sms:<number>[,<number>...][?body=<pct-encoded UTF-8>]`. Numbers SHOULD be global E.164 with `+`. Local numbers need `;phone-context=`. `-`, `.` and parentheses are visual separators only.
  Sample: `sms:+15105550101?body=hello%20there`
- **De facto:** `SMSTO:<number>:<message>`. The text after the second colon is raw (not percent-encoded, no escaping). `MMSTO:` and `mms:` are analogous. The ZXing wiki also lists `sms:<number>:<text>`.
  Sample: `SMSTO:+15105550101:Hello there`
- **Policy:** generate `SMSTO:` by default (the most common output), with an option for `sms:?body=`. Parse all variants. Treat premium short codes (fewer than 7 digits) as a caution (§6).

### 5.7 Telephone: `tel:` (RFC 3966)

`tel:+<country><number>[;ext=<n>]`, with visual separators allowed. Local numbers need `;phone-context=<domain or +prefix>`.
Sample: `tel:+1-212-555-1212;ext=42`
**Scanner:** pre-fill the dialer with `PhoneDialer.Open` and never call automatically. Warn when the number contains `*` or `#` (USSD or MMI codes).

### 5.8 Geo: `geo:` (RFC 5870)

`geo:<lat>,<lon>[,<alt>][;crs=wgs84][;u=<meters>]`. Uses WGS-84 decimal degrees with `.` as the decimal point: latitude -90..90, longitude -180..180, altitude in meters (omit it if unknown, rather than writing 0). `crs` and `u` appear at most once, `crs` first. Parameter names are case-insensitive. **Always format with `CultureInfo.InvariantCulture`**, or German locales will write a comma.
Samples: `geo:48.198634,16.371648;u=40`, `geo:40.71872,-73.98905,100`
The Android extension `geo:0,0?q=<address>` and `?q=<lat>,<lon>(Label)` is not in the RFC. For iOS reach, offer a "map link" alternative: `https://maps.apple.com/?ll=48.1986,16.3716&q=Label` or `https://www.google.com/maps/search/?api=1&query=48.1986,16.3716`.

### 5.9 Bitcoin: `bitcoin:` (BIP21, superseded by BIP321)

`bitcoin:<address>[?amount=<BTC decimal>][&label=<pct>][&message=<pct>][&<other>]`

- Amounts are decimal **BTC**, with `.` as the separator and no thousands separators (`50,000.00` is invalid). Values are percent-encoded UTF-8.
- If a client doesn't understand a `req-` prefixed parameter, the **whole URI is invalid**. Unknown non-`req-` parameters are ignored.
- **BIP321 additions:** query keys are case-insensitive; the address may be empty when a payment instruction is present (`lightning=` BOLT 11, `lno=` BOLT 12, `sp=` silent payments, `bc=` or `tb=` segwit); all-uppercase URIs must be handled, which suits QR alphanumeric mode; `pop=`/`req-pop=` proof-of-payment callbacks (must not be http, https or file); `label`, `message`, `amount` and `pop` must not repeat.
- Sample (BIP's intentionally invalid example address): `bitcoin:175tWpb8K1S7NmH4Zx6rewF9WQrcZv245W?amount=20.3&label=Luke-Jr&message=Donation%20for%20project%20xyz`
- Uppercase bech32 example (BIP321): `BITCOIN:BC1QUFGY354J3KMVUCH987XE4S40836X3H0LG8F5NQ`
- **Validate** Base58Check (legacy and P2SH) or bech32/bech32m (BIP173/350) checksums before display.

### 5.10 Ethereum: `ethereum:` (EIP-681, Final)

```
ethereum:[pay-]<0xaddress | ENS>[@<chain_id>][/<function>][?value=<wei>&gas=..&gasLimit=..&gasPrice=..&<type>=<v>]
```
- `value` is in **wei** (atomic units). Scientific notation is encouraged; its exponent must be at least the number of decimals.
- Sample (2.014 ETH): `ethereum:0xfb6916095ca1df60bb79Ce92ce3ea74c37c5d359?value=2.014e18`
- ERC-20 transfer: `ethereum:0x89205a3a3b2a69de6dbf7f01ed13b2108b2c43e7/transfer?address=0x8e23ee67d1332ad560396262c48ffbb01f93d052&uint256=1`
- Validate the EIP-55 mixed-case checksum. Show the chain (1 = Ethereum mainnet) and warn if it is missing, since the transaction could land on the wrong network. Wallet support for the function-call form varies.

### 5.11 EPC QR / SEPA GiroCode (EPC069-12, v3.0 of Sept 2022; payload version 002)

One field per line, separated by **LF or CRLF**. Trailing empty optional lines may be omitted, with no separator needed after the last populated line.

| Line | Field | Status (002) | Max | Format and notes |
|---|---|---|---|---|
| 1 | Service tag | M | 3 | `BCD` |
| 2 | Version | M | 3 | `001` or `002` |
| 3 | Character set | M | 1 | 1 = UTF-8, 2 = ISO 8859-1, 3 = 8859-2, 4 = 8859-4, 5 = 8859-5, 6 = 8859-7, 7 = 8859-10, 8 = 8859-15 |
| 4 | Identification | M | 3 | `SCT` |
| 5 | BIC | M in 001, **optional in 002** (EEA) | 11 | 8 or 11 characters |
| 6 | Beneficiary name | M | 70 | |
| 7 | IBAN | M | 34 | No spaces; validate mod-97 |
| 8 | Amount | O | 12 | `EUR` plus amount with `.` decimal and at most 2 decimals, `EUR0.01`-`EUR999999999.99`. Empty lets the payer enter the amount. |
| 9 | Purpose | O | 4 | ISO 20022 purpose code, for example `CHAR`, `GDDS` |
| 10 | Remittance (structured) | O | 35 | ISO 11649 RF creditor reference. **Use either line 10 or line 11, not both.** |
| 11 | Remittance (unstructured) | O | 140 | Free text |
| 12 | Beneficiary-to-originator info | O | 70 | Shown to the payer, not transmitted |

Constraints: **payload at most 331 bytes** (bytes, not characters, under UTF-8), **error correction M**, **QR version 13 or below**. Payment apps in Germany, Austria, Belgium, the Netherlands, Finland and elsewhere read it ("GiroCode" is the German brand).

Sample:
```
BCD
002
1
SCT
BPOTBEB1
Red Cross
BE72000000001616
EUR12.50
CHAR

Urgency fund
Sample EPC QR code
```

### 5.12 UPI: `upi://pay` (NPCI UPI Linking Specification)

```
upi://pay?pa=<vpa>&pn=<payee name>&am=<amount>&cu=INR&tn=<note>&tr=<txn ref>&mc=<mcc>&mode=<nn>&purpose=<nn>&url=<ref url>&sign=<base64>
```
| Param | Rule |
|---|---|
| `pa` | **Mandatory.** Payee VPA, for example `shop.owner@okaxis` |
| `pn` | **Mandatory.** Payee name, percent-encoded |
| `am` | Optional. Decimal with 2 places (`249.00`); if absent the payer enters it |
| `cu` | `INR`, the only supported value |
| `tn` | Short transaction note; keep it short because apps truncate it |
| `tr` | Transaction reference, up to 35 characters; mandatory for merchant dynamic QR |
| `mc` | 4-digit merchant category code; critical for merchant QRs |
| `mode` | 00 default, 01 QR, 02 secure QR, 04 intent (and others) |
| `sign` | Base64 digital signature on signed (secure) merchant QRs |

Sample: `upi://pay?pa=shop.owner@okaxis&pn=Sharma%20Stores&am=249.00&cu=INR&tn=Order%201234`
**Notes:** NPCI stopped P2P "collect" requests from 1 Oct 2025. That doesn't affect pay-intent QRs, but the scanner should show the payee VPA and name prominently. On Android, hand off with `ACTION_VIEW` to the UPI app chooser. Test handling of `upi://` on iOS with each target app.

### 5.13 WhatsApp click-to-chat

`https://wa.me/<number>[?text=<pct-encoded>]`. `<number>` is the full international number as **digits only**: no `+`, no leading `00`, no spaces, brackets or dashes. `https://wa.me/?text=...` lets the user pick a contact.
Sample: `https://wa.me/15551234567?text=I%27m%20interested%20in%20your%20car`
The `whatsapp://send?phone=...&text=...` scheme is less portable; prefer wa.me.

### 5.14 Telegram

| Purpose | Format |
|---|---|
| Profile, channel or bot | `https://t.me/<username>`. Usernames are 5-32 characters of `a-z`, `0-9` and `_`, case-insensitive. Native form: `tg://resolve?domain=<username>` |
| Phone number | `https://t.me/+<digits>` (international) |
| Share text or URL | `https://t.me/share/url?url=<pct>&text=<pct>` (native `tg://msg_url?url=...&text=...`) |
| Invite link | `https://t.me/+<hash>` (legacy `https://t.me/joinchat/<hash>`) |
| Bot deep link | `https://t.me/<bot>?start=<payload>` (payload up to 64 characters of `A-Z a-z 0-9 _ -`) |

Sample: `https://t.me/share/url?url=https%3A%2F%2Fexample.com&text=Check%20this`
(core.telegram.org refused the connection during this research, so these formats were not re-verified against the page.)

### 5.15 App-store links

| Store | Universal web link | Native scheme |
|---|---|---|
| Apple App Store and Mac App Store | `https://apps.apple.com/app/id<numeric id>` (optionally `/us/app/<slug>/id<id>`) | `itms-apps://apps.apple.com/app/id<id>`, `macappstore://` |
| Google Play | `https://play.google.com/store/apps/details?id=<package>` (optional `&referrer=<pct>` for the install referrer) | `market://details?id=<package>` (Android only) |
| Microsoft Store | `https://apps.microsoft.com/detail/<ProductId>` (12-character id such as `9NBLGGH4NNS1`) | `ms-windows-store://pdp/?productid=<id>` |

Always generate the **https** form. One QR that routes to the right store per OS needs a redirect page, which means a backend (or a static page with JavaScript user-agent sniffing on any static host).

### 5.16 PayPal.me

`https://paypal.me/<username>[/<amount>[<ISO 4217 code>]]`. Usernames are alphanumeric with no symbols, at most 20 characters. Without a currency code the payee's default currency applies.
Samples: `https://paypal.me/janedoe/12.50EUR`, PayPal's own example `PayPal.Me/DiaRusso/25AUD`

### 5.17 Not requested but relevant

- **otpauth (2FA):** `otpauth://totp/<Issuer>:<account>?secret=<BASE32>&issuer=<Issuer>&algorithm=SHA1&digits=6&period=30`. This is a **secret**: mask it, keep it out of history, and offer "Open in authenticator app".
- **Swiss QR-bill (`SPC`):** header lines `SPC`, `0200`, `1`; strict line layout; at most 997 characters; EC level M; the Swiss cross must be printed in the center. Treat it as a separate P2 builder and parser.
- **EMVCo merchant-presented QR** (Pix, QRIS, PromptPay, PayNow, DuitNow and others): TLV fields `ID(2) LEN(2) VALUE`, starting `000201`. Tag 26-51 holds merchant account info (Pix uses GUI `br.gov.bcb.pix`), 52 MCC, 53 numeric currency, 54 amount, 58 country, 59 name (up to 25), 60 city (up to 15), and **63 is a CRC-16/CCITT-FALSE** (polynomial 0x1021, initial 0xFFFF) over everything up to and including `6304`. Parse and display it (P1); Pix static generation is P2.

### 5.18 Parser detection order (scanner)

Trim, then match case-insensitively in this order:
1. `WIFI:`
2. `BEGIN:VCARD`
3. `MECARD:` or `BIZCARD:`
4. `BEGIN:VCALENDAR` or `BEGIN:VEVENT`
5. `MATMSG:`, `mailto:`, `SMTP:`
6. `SMSTO:`, `sms:`, `MMSTO:`, `mms:`
7. `tel:`
8. `geo:`
9. `bitcoin:`, `ethereum:`
10. `upi://`
11. `BCD` followed by a line break (EPC), `SPC` followed by a line break (Swiss)
12. `000201` with a valid CRC (EMVCo)
13. `otpauth://`
14. Other `scheme://` URIs, sub-classified by host (wa.me, t.me, apps.apple.com, play.google.com, paypal.me)
15. A bare domain (`www.` or a registrable domain via the Public Suffix List, offered as "Open as https?")
16. Product codes by symbology (EAN/UPC/ISBN 978/979)
17. Plain text, with embedded-URL detection

---

## 6. Scanner security ("quishing" defences, section E)

### 6.1 Threat model

The FTC warns about malicious codes **stuck over parking-meter codes**, and about unsolicited codes in texts, emails and **packages**. The FBI flags "brushing" packages that carry QR codes. Interisle's *Phishing Landscape 2025* counted more than 1.5 million phishing domains (+38%), concentrated in cheap TLDs. Common attacks:
- look-alike domains (IDN homographs, typosquats, brand in a subdomain);
- shorteners or dynamic-QR redirectors that hide the destination;
- `http` credential capture;
- direct APK or EXE downloads;
- `tel:` USSD codes and premium SMS;
- swapped payment recipients (EPC, UPI, crypto);
- malicious Wi-Fi (evil twin, open networks).

### 6.2 On-device URL risk engine (free, P0)

Parse with `Uri.TryCreate`, then analyze the host: punycode via `IdnMapping`, eTLD+1 via the Public Suffix List (for example the Nager.PublicSuffix package). Each rule adds a score and a plain-language reason.

| # | Check | Detection | Severity |
|---|---|---|---|
| 1 | Dangerous scheme | Scheme not in the allow-list {http, https, mailto, tel, sms, geo, market, itms-apps, upi, bitcoin, ethereum, otpauth, wifi, tg, whatsapp}. `javascript:`, `data:`, `file:`, `content:`, `intent:`, `vbscript:` and `blob:` are **blocked** (copy only). | Block |
| 2 | Plain http | `http://` | Medium ("Not encrypted; don't enter passwords or payment details") |
| 3 | IP-literal host | Dotted IPv4, **integer, hex or octal IPv4** (`http://3232235777`, `0xC0A80001`), IPv6 `[...]`. Private ranges (10/8, 172.16/12, 192.168/16, 127/8, 169.254/16) get a separate "local network device" notice, because routers are legitimate. | High (public) or Info (private) |
| 4 | Userinfo `@` trick | `Uri.UserInfo` not empty (`https://paypal.com@evil.example/`); also a backslash or `%40` in the authority | High; show "Real site: evil.example" |
| 5 | IDN or punycode | Any label starting `xn--` or containing non-ASCII. Flag mixed scripts within a label (Latin with Cyrillic or Greek), **whole-script confusables** (an all-Cyrillic label that looks Latin), and a **skeleton match to a protected-brand list** (UTS #39 skeletons; Chromium compares against its top domains). Always show both the Unicode and punycode forms. | High |
| 6 | Look-alike or typosquat | eTLD+1 within edit distance 1-2 of a brand list (banks, PayPal, Apple, Google, Microsoft, Amazon, delivery firms, parking and payment apps); digit-for-letter swaps (`paypa1`); brand token in a subdomain or path (`paypal.com.secure-login.top`) | High |
| 7 | URL shortener or redirector | Host in a bundled shortener list (seeded from the PeterDaveHello/url-shorteners list, about 1,000+ domains, CC-BY-SA), including dynamic-QR redirectors such as `bit.ly`, `qrco.de`, `qr1.be` | Medium ("Destination hidden"), with an opt-in **Reveal destination** button |
| 8 | Embedded redirect | Query values that parse as URLs (`url=`, `redirect=`, `next=`, `u=`, `dest=`) pointing to another domain | Medium; show the embedded target |
| 9 | Risky TLD | Bundled list, updated with app releases. 2025 heavy hitters from Interisle: `.xin .bond .help .win .cfd`. Repeat offenders: `.top .pro .vip .world .cc .sbs .icu .click .lol .shop .support .mom`. Spamhaus also highlights `.top .xyz .life .club`. | Low to medium (a signal, not a verdict) |
| 10 | Unusual port | Port other than 80 or 443 | Low to medium |
| 11 | Obfuscation | Percent-encoded host characters, more than 4 subdomain labels, length over 200, double extensions, `%00` | Low |
| 12 | Direct download | Path ends in `.apk .xapk .exe .msi .dmg .pkg .ipa .bat .cmd .ps1 .scr .js .vbs` or `.zip` | High ("This downloads an app or file") |
| 13 | Non-URL abuse | `tel:` containing `*` or `#` (USSD); SMS to numbers shorter than 7 digits (premium); open or WEP Wi-Fi networks | Medium |
| 14 | Payment intent | EPC, UPI, PayPal or crypto payload, or a URL containing pay, parking, toll or fine | Info: "Check the recipient; stickers on meters can be fake" |

**Verdict mapping:** a score of 0 gives "No problems found. Check the domain before you continue." (neutral, never "Safe"). A low score gives **Caution** (amber, lists reasons, Open is enabled). High or Block gives **Warning** (red, Open is moved behind "Open anyway", and Copy is still allowed). This follows Kaspersky's practice of only highlighting known problems, and Google's guidance to use qualified wording such as "suspected" or "may be".

**Display rules** (from Chromium's URL display guidelines): show the **host prominently with the eTLD+1 in bold**, hide or collapse the path, never truncate the right-hand side of the host, and never show only the page title.

```csharp
public sealed record RiskFinding(string Code, int Score, string Message);

public static IReadOnlyList<RiskFinding> Analyze(Uri u, RiskData d)
{
    var f = new List<RiskFinding>();
    if (!d.AllowedSchemes.Contains(u.Scheme)) f.Add(new("scheme", 100, $"'{u.Scheme}:' links can run code or open files"));
    if (u.Scheme == "http") f.Add(new("http", 20, "Not encrypted"));
    if (!string.IsNullOrEmpty(u.UserInfo)) f.Add(new("userinfo", 60, $"Real site is {u.IdnHost}"));
    if (IsIpLiteral(u.Host, out var isPrivate)) f.Add(new("ip", isPrivate ? 5 : 50, "Uses a numeric address instead of a name"));
    var uni = new IdnMapping().GetUnicode(u.IdnHost);
    if (HasMixedScripts(uni) || MatchesBrandSkeleton(uni, d.Brands)) f.Add(new("idn", 70, $"Look-alike characters: {uni} ({u.IdnHost})"));
    var reg = d.Psl.GetRegistrableDomain(u.IdnHost);
    if (d.Shorteners.Contains(reg)) f.Add(new("short", 25, "Shortened link hides the destination"));
    if (d.RiskyTlds.Contains(TldOf(reg))) f.Add(new("tld", 15, "Domain ending often used for scams"));
    // ...ports, downloads, embedded redirects, typosquat distance, obfuscation
    return f;
}
```

### 6.3 Shortener "Reveal destination" (opt-in, on-device with network)

Use `HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }`. Send `HEAD`, falling back to `GET` with `Range: bytes=0-0`. Read `Location` for at most 5 hops with a 5 s timeout, and run the analyzer on every hop. Tell the user that this contacts the link's server, which may log the visit. Never execute page JavaScript.

### 6.4 Payment and Wi-Fi specifics

- **EPC:** show the beneficiary name and IBAN (grouped in 4s) and the amount in large type. Validate IBAN mod-97 and the RF reference.
- **UPI:** show the VPA and name; flag a VPA domain that doesn't match `pn` only as Info.
- **Crypto:** checksum validation; "Payments are irreversible"; compare the first and last 6 characters to guard against clipboard-hijacking malware.
- **Wi-Fi:** flag open networks and WEP. Remind users that a captive portal asking for card details is a red flag.

### 6.5 Google Safe Browsing vs Web Risk: can a client app use them?

| Option | Terms and cost | Usable by QuickQr? |
|---|---|---|
| **Safe Browsing API v4/v5** (Lookup and Update) | Free, **non-commercial use only** ("you may not use the Safe Browsing API for commercial purposes" without a separate agreement). Warnings must use qualified language, link to more information, and carry **"Advisory provided by Google"**; the app must disclose that protection is imperfect; data used for a warning must be fresh (updated within 30 minutes). | **No**, once QuickQr sells a Pro tier: that is commercial. A purely free, non-revenue build could, but don't rely on that. |
| **SafetyNet Safe Browsing API** (Android, Play services; v5 adds real-time mode over Oblivious HTTP) | Bound by the same Safe Browsing Terms of Service; needs an Android API key | Same commercial restriction; Android only |
| **Web Risk API** (Google Cloud, commercial) | **Lookup** (`uris:search`): the first 100,000 calls a month are free, then **$0.50 per 1,000** up to 10M; the server sees the full URL. **Update** (`threatLists:computeDiff`): free, keeps a local hash-prefix DB for privacy, but confirmations via `hashes:search` are reported at **$50 per 1,000** (first 100k), and using computeDiff changes how `uris:search` is priced. Check the pricing page. Needs a Cloud project with billing; returned data "must not be redistributed"; lists are not guaranteed complete. | **Technically yes**, but an API key shipped in a client can be extracted (Google's own Android guidance says to keep static keys server-side). So in practice it **needs a backend proxy** with app attestation (Play Integrity or App Attest) and rate limiting. P2, as part of a paid cloud add-on. Disclose that URLs are sent to Google. |
| **Platform protection, free** | Chrome, Safari and Edge have built-in protection. **Android WebView** Safe Browsing is on by default (`setSafeBrowsingEnabled`, `onSafeBrowsingHit` to "back to safety"). **WKWebView** has a fraudulent-website warning preference. **WebView2** has SmartScreen reputation checking. | **Yes.** Open links with `Browser.OpenAsync(..., SystemPreferred)`, or show a protected preview (X6). It doesn't give a verdict before opening, but it blocks known-bad pages at navigation. |

### 6.6 How Kaspersky and Norton present warnings

- **Kaspersky QR Scanner:** works as scan, check, then open, checking the link against the Kaspersky Security Network cloud. A known-dangerous link is **highlighted in red, and opening it in the browser or saving it to contacts is blocked**. An **unknown** site is simply not highlighted, with no "safe" badge. A safe link prompts to open in the default browser, and pages can be viewed in a **built-in preview browser** first.
- **Norton Snap (retired):** showed a **Norton Safe Web rating and prompted before visiting**. Users complained that it auto-opened links rated "safe". Norton now points to Norton Mobile Security's Web Protection.
- **Android and iOS cameras:** show the URL or domain with an explicit button and no verdict.

**Lesson for QuickQr:** use three states (neutral, caution, warning), explain each reason in plain words, never auto-open, make "Copy" always possible, and make "Open anyway" deliberate.

---

## 7. Barcode formats in ZXing.Net (section F)

**ZXing.Net 0.16.11** (NuGet, 25 Oct 2025) with ZXing.Net.Maui(.Controls) 0.10.4 (Aug 2026; net10 for Android, iOS, Mac Catalyst and Windows). **ZXing.Net.Maui does not support camera scanning on Windows**: generation and still-image decoding work there, so the Windows camera needs a custom `MediaCapture` pipeline into ZXing.

| Format | Write | Read | Content constraints (as used by the ZXing writers) | Check digit |
|---|---|---|---|---|
| **QR Code** (Model 2) | Yes | Yes | Up to 2,953 bytes, 4,296 alphanumeric or 7,089 numeric (V40-L); see §5.0 | Reed-Solomon EC L/M/Q/H |
| **Micro QR / rMQR** | **No** | **No** | Not in the `BarcodeFormat` enum. Use zxing-cpp (it reads both and writes Micro QR and rMQR with the zint-based writer; the NuGet ships Windows, Linux and macOS natives only) or Apple Vision (reads Micro QR) | n/a |
| **Data Matrix** (ECC200) | Yes | Yes | Up to 3,116 numeric, 2,335 alphanumeric or 1,556 bytes (144×144). Square or rectangular shape hint. GS1 via FNC1 (`GS1Format`). | Reed-Solomon |
| **Aztec** | Yes | Yes | Up to 3,832 numeric, 3,067 alphabetic or 1,914 bytes (32 layers). EC percentage option (default 33%) and layer count. | Reed-Solomon |
| **PDF417** | Yes | Yes | Up to 2,710 numeric, 1,850 text or 1,108 bytes. EC level 0-8, compact mode, compaction mode, rows and columns. MicroPDF417 is not supported. | Reed-Solomon |
| **MaxiCode** | No | Yes (limited) | About 93 alphanumeric or 138 numeric characters. **ZXing's reader assumes a pure, unrotated image** ("PURE_BARCODE"), so it is unreliable from a camera. | Reed-Solomon |
| **Code 128** | Yes | Yes | ASCII 0-127 plus FNC1-4; the ZXing writer caps contents at 80 characters; code set chosen automatically (can be forced); GS1-128 with `GS1Format` | Mod 103 (automatic) |
| **Code 39** | Yes | Yes | `0-9 A-Z space - . $ / + %`, up to 80 characters; full ASCII through Code 39 Extended (check how your ZXing.Net version handles lowercase); reader options `useCode39CheckDigit` and extended mode | Optional mod 43 |
| **Code 93** | Yes | Yes | Full ASCII through shift characters; up to 80 characters after expansion | Two mandatory characters, C and K mod 47 (automatic) |
| **EAN-13** | Yes | Yes | **Digits only**: 12 (check digit computed) or 13 (check digit verified) | Mod 10 (3-1 weights) |
| **EAN-8** | Yes | Yes | Digits only: 7 or 8 | Mod 10 |
| **UPC-A** | Yes | Yes | Digits only: 11 or 12 | Mod 10 |
| **UPC-E** | Yes | Yes | Digits only: 7 or 8; **the first (number-system) digit must be 0 or 1**; the content is the compressed UPC-E form | Mod 10 (of the expanded UPC-A) |
| **UPC/EAN extension** (2 or 5 digits) | No | Yes (as result metadata) | Add-on to EAN/UPC, not standalone | Parity scheme |
| **ITF** (Interleaved 2 of 5) | Yes | Yes | **Digits only, even length**, up to 80 digits. ITF-14 is 14 digits with a GS1 check digit (add bearer bars yourself). | ITF-14 uses mod 10 |
| **Codabar** | Yes | Yes | `0-9 - $ : / . +`, with start and stop characters A/B/C/D (T/N/*/E also accepted). The writer adds start and stop characters if they are missing. | None required |
| **MSI** | Yes | Yes (**only when explicitly enabled**; excluded from `All_1D` because of false positives) | Digits only. Append a mod 10 or mod 11 check digit yourself if the reader expects it; the reader has a `useMsiCheckDigit` option. | Optional |
| **Plessey** | Yes | **No** | Hex digits `0-9 A-F` | CRC (automatic) |
| **GS1 DataBar** (RSS-14 / Expanded) | No | Yes | RSS-14 holds a 14-digit GTIN; Expanded holds up to 74 numeric or 41 alphanumeric characters with GS1 AIs. **DataBar Limited is not supported.** | Mod 89 or 211 (internal) |
| **IMb** (USPS Intelligent Mail) | No | Yes (not in the default reader set) | 20-digit tracking code plus a 0, 5, 9 or 11-digit routing code | Built in |
| **Pharmacode** | No | Yes (explicit only) | Integer 3-131,070 | n/a |
| DotCode, Han Xin, Code 11, Telepen, postal 4-state, Codablock, Code 16K/49 | No | No | zxing-cpp adds Telepen, DX Film Edge and DataBar Limited; Apple Vision adds MicroPDF417 | n/a |

**Reader notes:**
- By default (no `PossibleFormats`), `MultiFormatReader` uses the 1D readers (UPC/EAN, Code 39, Codabar, Code 93, Code 128, ITF, RSS-14, RSS Expanded) plus QR, Data Matrix, Aztec, PDF417 and MaxiCode.
- **IMb, MSI and Pharmacode must be requested explicitly.**
- On a camera, restrict formats (for example QR, Data Matrix, Aztec, PDF417, EAN/UPC and Code 128) for speed and fewer false reads.

**Native alternatives:** ML Kit on Android reads Codabar, Code 39/93/128, EAN-8/13, ITF, UPC-A/E, Aztec, Data Matrix, PDF417 and QR without a camera permission when used through the Play services code scanner. Apple Vision adds Micro QR, MicroPDF417 and GS1 DataBar (including Limited). The `BarcodeScanning.Native.Maui` package wraps both.

---

## 8. Implementation notes for MAUI

**History schema (SQLite)**
```sql
CREATE TABLE HistoryItem (
  Id TEXT PRIMARY KEY,            -- GUID (merge-safe for backup/restore)
  Kind INTEGER NOT NULL,          -- 0=scan, 1=generated
  Format TEXT NOT NULL,           -- QR_CODE, EAN_13, ...
  PayloadType TEXT NOT NULL,      -- Url, Wifi, VCard, Epc, Upi, Text...
  RawText TEXT NOT NULL,
  Title TEXT,                     -- derived (domain, SSID, contact name)
  StyleJson TEXT,                 -- generated items: style preset
  IsFavorite INTEGER DEFAULT 0,
  IsSensitive INTEGER DEFAULT 0,  -- otpauth, Wi-Fi password, keys
  ScanCount INTEGER DEFAULT 1,
  RiskScore INTEGER,              -- last analyzer score
  Notes TEXT, Lat REAL, Lon REAL,
  CreatedUtc TEXT NOT NULL, UpdatedUtc TEXT NOT NULL
);
CREATE VIRTUAL TABLE HistoryFts USING fts5(Title, RawText, Notes, content='HistoryItem');
CREATE TABLE Tag (Id TEXT PRIMARY KEY, Name TEXT UNIQUE);
CREATE TABLE ItemTag (ItemId TEXT, TagId TEXT, PRIMARY KEY (ItemId, TagId));
```

**Rendering pipeline:**
1. A payload builder produces a string.
2. `Encoder.encode(text, ecLevel, hints)` produces the `ByteMatrix`.
3. A SkiaSharp painter renders it (square, dot or rounded modules; custom finder eyes; logo knock-out; frame and caption) onto an `SKCanvas` abstraction, so the same code targets bitmap, `SKSvgCanvas` and `SKDocument` PDF.
4. The self-test decode (S2) runs on the bitmap.

**Entitlements:** `Plugin.InAppBilling` non-consumables `quickqr.pro` and `quickqr.business` (plus `quickqr.business_upgrade`). Cache the entitlement in `SecureStorage` and re-verify on launch or via restore. For sideloaded or GitHub desktop builds, use a signed license file.

**Testing checklist for payloads:** round-trip every builder through the ZXing decoder and the QuickQr parser. Do a manual matrix test with the iOS Camera, the Android system scanner and Google Lens for Wi-Fi (WPA, WPA3/SAE, hidden, special characters), vCard (UTF-8 names), VEVENT, SMSTO/sms and EPC (with a real banking app).

---

## 9. Sources

**Competitors and pricing**
- QR Code Generator PRO pricing and plan contents: https://www.qr-code-generator.com/pricing/ · https://www.qr-code-generator.com/upgrade · https://support.qr-code-generator.com/hc/en-us/articles/17979632960397-What-does-your-Starter-Plan-include · https://support.qr-code-generator.com/hc/en-us/articles/17981280158093-What-does-your-Advanced-Plan-include · https://support.qr-code-generator.com/hc/en-us/articles/17981356503693-What-does-your-Professional-Plan-include · https://ecommercefastlane.com/qr-code-generator-pro-vs-the-qr-code-generator/
- Bitly pricing: https://bitly.com/pages/pricing · https://www.capterra.com/p/172575/Bitly/pricing/ · https://bitly.com/blog/bitly-free/
- QR TIGER: https://www.qrcode-tiger.com/llms.txt · https://www.capterra.com/p/10041460/QR-TIGER/ · https://www.getapp.com/all-software/a/qr-tiger/ · https://qrtiger.helpscoutdocs.com/article/88-plans-and-pricing · https://vcard.qrcode-tiger.com/payment
- Uniqode: https://www.uniqode.com/pricing · https://www.capterra.com/p/234622/Uniqode
- Scanova: https://support.scanova.io/hc/en-us/articles/38942962402585-Scanova-Plans · https://docs.scanova.io/billing/upgrading · https://www.capterra.com/p/136160/Scanova/pricing/
- ME-QR: https://me-qr.com/pricing · https://techyorker.com/products/me-qr/
- QRCode Monkey: https://www.softwareadvice.com/product/548332-QRCode-Monkey/ · https://qrplanet.com/help/article/qr-planet-vs-qrcode-monkey
- QRbot: https://qrbot.net/ · https://apps.apple.com/app/id1048473097
- Kaspersky QR Scanner: https://support.kaspersky.com/qrscanner-for-android/1.12/93926 · https://support.kaspersky.com/qrscanner-for-ios/1.10/93973 · https://usa.kaspersky.com/about/press-releases/kaspersky-lab-launches-secure-qr-scanner
- Norton Snap: https://community.norton.com/t/end-of-life-announcement-for-norton-snap-qr-code-reader/235043 · https://community.norton.com/t/norton-snap-2-0-0-71-always-visits-sites-deemed-safe/203359 · https://us.norton.com/blog/online-scams/quishing
- Orca Scan: https://orcascan.com/pricing · https://www.trustradius.com/products/orca-scan/pricing
- Scan-IT to Office: https://www.tec-it.com/en/software/selection/office-barcode-scanner/EntryPage3.aspx · https://apps.apple.com/us/app/id1241932661
- SafeQR Reader: https://apps.apple.com/app/id6517350530
- Weekly-subscription QR app complaints: https://techcrunch.com/2018/10/15/sneaky-subscriptions-are-plaguing-the-app-store/ · https://apps.apple.com/us/app/qr-code-reader-scan/id1200318119
- Android scanner and Lens: https://www.android.com/articles/how-do-you-scan-qr-codes-on-android · https://9to5google.com/2024/07/15/android-qr-code-scanner-update-design/ · https://developers.google.com/ml-kit/code-scanner
- Apple Camera QR: https://support.apple.com/en-ie/guide/iphone/scan-a-qr-code-iphe8bda8762/ios · https://developer.apple.com/videos/play/fall2017/206/ · https://www.macrumors.com/how-to/scan-qr-codes-iphone-camera/
- Accessibility scanner examples: https://apps.apple.com/us/app/vip-code-reader-blind-scanner/id1273427754 · https://apps.apple.com/us/app/zapvision/id1603467286

**Monetization and platform**
- Apple Small Business Program: https://developer.apple.com/app-store/small-business-program/
- Google Play service fees: https://www.revenuecat.com/blog/engineering/app-store-fees · Play self-update policy (Device and Network Abuse): https://support.google.com/googleplay/android-developer/answer/9888379
- Microsoft Store revenue share and own commerce: https://blogs.windows.com/windowsdeveloper/2018/05/07/a-new-microsoft-store-revenue-share-is-coming/ · https://blogs.windows.com/windowsdeveloper/2025/09/10/free-developer-registration-for-individual-developers-on-microsoft-store/
- RevenueCat pricing: https://www.revenuecat.com/pricing
- Plugin.InAppBilling: https://www.nuget.org/packages/Plugin.InAppBilling · https://devblogs.microsoft.com/dotnet/cross-platform-billing-dotnet-maui/
- MAUI App Actions: https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/appmodel/app-actions
- MAUI widgets: https://devblogs.microsoft.com/dotnet/how-to-build-ios-widgets-with-dotnet-maui/ · https://devblogs.microsoft.com/dotnet/tag/widgets/
- Android QS tile: https://developer.android.com/develop/ui/views/quicksettings-tiles/qr-code
- Native scanner wrapper: https://github.com/afriscic/BarcodeScanning.Native.Maui

**Payload standards**
- ZXing Barcode Contents (Wi-Fi, MECARD, vCard, VEVENT, mailto, SMS, tel, geo): https://github.com/zxing/zxing/wiki/Barcode-Contents
- ZXing MECARD and vCard encoders: https://github.com/zxing/zxing/blob/master/android/src/com/google/zxing/client/android/encode/MECARDContactEncoder.java · https://github.com/zxing/zxing/blob/master/android/src/com/google/zxing/client/android/encode/VCardContactEncoder.java
- WPA3 specification v3.4, section 7 (Wi-Fi URI): https://www.wi-fi.org/system/files/WPA3%20Specification%20v3.4.pdf · IANA `wifi` scheme: https://www.iana.org/assignments/uri-schemes/prov/wifi · analysis: https://stevetech-me.pages.dev/posts/wifi-qr-codes-are-broken
- MeCard: https://en.wikipedia.org/wiki/MeCard_(QR_code) (archived DoCoMo spec: https://web.archive.org/web/20160304025131/https://www.nttdocomo.co.jp/english/service/developer/make/content/barcode/function/application/addressbook/index.html)
- segno helpers (Wi-Fi, MeCard and EPC reference implementation): https://github.com/heuer/segno/blob/master/segno/helpers.py · https://segno.readthedocs.io/en/stable/epc-qrcodes.html
- vCard 3.0: https://www.rfc-editor.org/rfc/rfc2426 · folding: https://www.rfc-editor.org/rfc/rfc2425 · vCard 4.0: https://www.rfc-editor.org/rfc/rfc6350
- iCalendar: https://www.rfc-editor.org/rfc/rfc5545
- mailto: https://www.rfc-editor.org/rfc/rfc6068 · MATMSG: https://qrcode-library.readthedocs.io/en/latest/formats/mail-message/
- sms: https://www.rfc-editor.org/rfc/rfc5724 · tel: https://www.rfc-editor.org/rfc/rfc3966 · geo: https://www.rfc-editor.org/rfc/rfc5870
- BIP21: https://github.com/bitcoin/bips/blob/master/bip-0021.mediawiki · BIP321: https://github.com/bitcoin/bips/blob/master/bip-0321.mediawiki · BIP173: https://github.com/bitcoin/bips/blob/master/bip-0173.mediawiki
- EIP-681: https://eips.ethereum.org/EIPS/eip-681 · EIP-55: https://eips.ethereum.org/EIPS/eip-55
- EPC069-12 v3.0: https://europeanpaymentscouncil.eu/sites/default/files/kb/file/2022-09/EPC069-12%20v3.0%20Quick%20Response%20Code%20-%20Guidelines%20to%20Enable%20the%20Data%20Capture%20for%20the%20Initiation%20of%20an%20SCT_0.pdf · https://en.wikipedia.org/wiki/EPC_QR_code
- UPI: NPCI UPI Linking Specification 1.6 (copy): https://www.labnol.org/files/linking.pdf · NPCI merchant circular: https://www.npci.org.in/PDF/npci/upi/circular/2017/Circular18_BankCompliances_to_enbaleUPIMerchantecosystem_0.pdf · Google Pay UPI parameters: https://developers.google.com/pay/india/api/android/in-app-payments · P2P collect ban: https://www.businesstoday.in/personal-finance/banking/story/npci-to-tighten-upi-rules-peer-to-peer-collect-feature-to-end-in-october-489244-2025-08-13
- WhatsApp click to chat: https://faq.whatsapp.com/5913398998672934
- Telegram deep links: https://core.telegram.org/api/links (connection refused during this research; not re-verified)
- Google Play links: https://developer.android.com/distribute/marketing-tools/linking-to-google-play · Microsoft Store links: https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-store-app
- PayPal.me: https://www.paypal.com/us/cshelp/article/what-is-paypalme-help432
- QR versions and capacity: https://www.qrcode.com/en/about/version.html · https://www.qrcode.com/en/about/error_correction.html · https://en.wikipedia.org/wiki/QR_code
- EMVCo QR: https://www.emvco.com/emv-technologies/qr-codes/ · Swiss QR-bill: https://www.six-group.com/en/products-services/banking-services/payment-standardization/standards/qr-bill.html
- Google Authenticator key URI: https://github.com/google/google-authenticator/wiki/Key-Uri-Format
- GS1 Digital Link: https://www.gs1.org/standards/gs1-digital-link · Sunrise 2027: https://www.gs1us.org/industries-and-insights/by-topic/sunrise-2027

**Security**
- FTC on QR scams: https://consumer.ftc.gov/consumer-alerts/2023/12/scammers-hide-harmful-links-qr-codes-steal-your-information · coverage of the FBI brushing warning: https://bgr.com/2122424/fbi-serious-warning-about-qr-codes-on-packages
- Interisle Phishing Landscape 2025: https://interisle.net/PhishingLandscape2025 · https://domainnamewire.com/2025/09/11/report-names-commonly-used-tlds-for-phishing-attacks/ · https://interisle.substack.com/p/cybercrime-reported-in-june-2025 · https://krebsonsecurity.com/2024/12/why-phishers-love-new-tlds-like-shop-top-and-xyz/
- Spamhaus domain reputation: https://www.spamhaus.org/resource-hub/domain-reputation/domain-reputation-update-april-september-2025/ · https://www.spamhaus.org/resource-hub/domain-reputation/domain-reputation-update-oct-2024-mar-2025/
- Chromium IDN policy and URL display guidelines: https://chromium.googlesource.com/chromium/src/+/main/docs/idn.md · https://chromium.googlesource.com/chromium/src/+/HEAD/docs/security/url_display_guidelines/url_display_guidelines.md
- Unicode TR39 (confusables and skeletons): https://www.unicode.org/reports/tr39/ · Public Suffix List: https://publicsuffix.org/
- URL shortener list: https://github.com/PeterDaveHello/url-shorteners
- Google Safe Browsing (non-commercial), terms and usage rules: https://developers.google.com/safe-browsing · https://developers.google.com/safe-browsing/terms · https://developers.google.com/safe-browsing/v4/usage-limits
- Web Risk overview and pricing: https://docs.cloud.google.com/web-risk/docs/overview · https://cloud.google.com/web-risk/pricing
- SafetyNet Safe Browsing (Android): https://developer.android.com/privacy-and-security/safetynet/safebrowsing
- Android WebView Safe Browsing: https://developer.android.com/develop/ui/views/layout/webapps/managing-webview
- Static API keys in apps: https://developer.android.com/privacy-and-security/risks/insecure-api-usage

**ZXing.Net and alternatives**
- ZXing.Net repository and README: https://github.com/micjahn/ZXing.Net · `BarcodeFormat.cs`: https://github.com/micjahn/ZXing.Net/blob/master/Source/lib/BarcodeFormat.cs · `MultiFormatWriter.cs`: https://github.com/micjahn/ZXing.Net/blob/master/Source/lib/MultiFormatWriter.cs · `MultiFormatReader.cs`: https://github.com/micjahn/ZXing.Net/blob/master/Source/lib/MultiFormatReader.cs · `MultiFormatOneDReader.cs`: https://github.com/micjahn/ZXing.Net/blob/master/Source/lib/oned/MultiFormatOneDReader.cs
- NuGet: https://www.nuget.org/packages/ZXing.Net · https://www.nuget.org/packages/ZXing.Net.Maui.Controls
- zxing-cpp: https://github.com/zxing-cpp/zxing-cpp · https://www.nuget.org/packages/ZXingCpp
