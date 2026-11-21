using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using QuickQr.Core.Content;
using QuickQr.Core.Security;

namespace QuickQr.Services.Actions;

/// <summary>
/// Maps every <see cref="ContentActionKind"/> to a platform action. Every path degrades gracefully: when no app can
/// handle an action the relevant data is copied and the user is told what to do next. Links rated Medium or High risk
/// by the offline analyzer always ask for confirmation first, wherever the action comes from (scan, history, ...).
/// </summary>
public sealed class ContentActionExecutor(
    IDialogService dialogs,
    IClipboardService clipboard,
    IShareService share,
    IExternalLauncher launcher,
    IFileExportService files,
    IWifiConnector wifi,
    IUrlSafetyAnalyzer safety,
    ISecurePayloadProtector protector,
    IContentEncoder encoder,
    IContentParser parser,
    IHapticsService haptics,
    ILogger<ContentActionExecutor> logger) : IContentActionExecutor
{
    public async Task ExecuteAsync(ContentAction action, QrContent content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(content);

        try
        {
            await (action.Kind switch
            {
                ContentActionKind.Copy => CopyAsync(action.Target ?? PayloadOf(content), "Copied to clipboard"),
                ContentActionKind.Share => share.ShareTextAsync("Share", action.Target ?? PayloadOf(content)),
                ContentActionKind.OpenUrl => OpenLinkAsync(action.Target ?? UrlOf(content)),
                ContentActionKind.ConnectWifi => ConnectWifiAsync(content, action.Target, cancellationToken),
                ContentActionKind.CopyWifiPassword => CopyAsync(action.Target ?? (content as WifiContent)?.Password, "Wi-Fi password copied"),
                ContentActionKind.AddContact => AddContactAsync(content, action.Target, cancellationToken),
                ContentActionKind.Call => CallAsync(PhoneOf(content, action.Target)),
                ContentActionKind.SendSms => SendSmsAsync(content, action.Target),
                ContentActionKind.SendEmail => SendEmailAsync(content, action.Target),
                ContentActionKind.OpenMap => OpenMapAsync(content, action.Target, navigate: false),
                ContentActionKind.Navigate => OpenMapAsync(content, action.Target, navigate: true),
                ContentActionKind.AddToCalendar => AddToCalendarAsync(content, action.Target, cancellationToken),
                ContentActionKind.OpenWhatsApp => OpenWhatsAppAsync(content, action.Target),
                ContentActionKind.OpenTelegram => OpenTelegramAsync(content, action.Target),
                ContentActionKind.OpenPaymentApp => OpenPaymentAsync(content, action.Target),
                ContentActionKind.Decrypt => DecryptAsync(content, action.Target, cancellationToken),
                ContentActionKind.SearchWeb => SearchAsync(action.Target ?? PayloadOf(content), product: false),
                ContentActionKind.SearchProduct => SearchAsync(action.Target ?? (content as ProductContent)?.Code ?? PayloadOf(content), product: true),
                ContentActionKind.OpenStore => OpenStoreAsync(content, action.Target),
                _ => dialogs.AlertAsync("Not supported", "This action is not available on this device."),
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FeatureNotSupportedException ex)
        {
            logger.LogInformation(ex, "{Action} is not supported on this device", action.Kind);
            await FallbackCopyAsync(action, content, "This action isn't available on this device.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Action {Action} failed", action.Kind);
            await FallbackCopyAsync(action, content, "That didn't work.");
        }
    }

    // ---------- Text ----------

    private async Task CopyAsync(string? text, string toast)
    {
        if (string.IsNullOrEmpty(text))
        {
            await dialogs.AlertAsync("Nothing to copy", "This code has no text for that.");
            return;
        }

        await clipboard.SetTextAsync(text);
        haptics.Click();
        await dialogs.ToastAsync(toast);
    }

    private async Task FallbackCopyAsync(ContentAction action, QrContent content, string reason)
    {
        var text = action.Target ?? PayloadOf(content);
        if (string.IsNullOrEmpty(text))
        {
            await dialogs.AlertAsync("Action unavailable", reason);
            return;
        }

        await clipboard.SetTextAsync(text);
        await dialogs.AlertAsync("Action unavailable", reason + " The content was copied to the clipboard instead.");
    }

    // ---------- Links ----------

    private async Task OpenLinkAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            await dialogs.AlertAsync("No link", "This code does not contain a link.");
            return;
        }

        url = NormalizeUrl(url);
        if (IsWebUrl(url) && !await ConfirmRiskyLinkAsync(url)) return;

        if (!await launcher.OpenUrlAsync(url))
            await CopyFallbackAsync(url, "No app can open this link. It was copied to the clipboard.");
    }

    /// <summary>Medium/High risk links need an explicit "Open anyway".</summary>
    private async Task<bool> ConfirmRiskyLinkAsync(string url)
    {
        UrlSafetyReport report;
        try
        {
            report = safety.Analyze(url);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Link analysis failed");
            return true;
        }

        if (report.Level < RiskLevel.Medium) return true;

        var host = report.DisplayHost ?? url;
        var reasons = string.Join("\n", report.Signals
            .OrderByDescending(s => s.Severity)
            .Take(3)
            .Select(s => "• " + s.Description));
        var title = report.Level == RiskLevel.High ? "This link looks dangerous" : "Check this link first";
        var message = $"{host}\n\n{reasons}\n\nOnly open it if you trust where the code came from.".Trim();
        return await dialogs.ConfirmAsync(title, message, "Open anyway", "Cancel");
    }

    private async Task SearchAsync(string? query, bool product)
    {
        if (string.IsNullOrWhiteSpace(query)) return;

        if (IsWebUrl(query))
        {
            await OpenLinkAsync(query);
            return;
        }

        var q = product ? query.Trim() + " product" : query.Trim();
        var url = "https://www.google.com/search?q=" + Uri.EscapeDataString(q);
        if (!await launcher.OpenUrlAsync(url))
            await CopyFallbackAsync(query, "No browser is available. The text was copied to the clipboard.");
    }

    private async Task OpenStoreAsync(QrContent content, string? target)
    {
        var candidates = new List<string>();
        if (content is AppStoreContent store)
        {
            var id = Uri.EscapeDataString(store.AppId);
            switch (store.Store)
            {
                case AppStoreKind.GooglePlay:
                    candidates.Add($"market://details?id={id}");
                    candidates.Add($"https://play.google.com/store/apps/details?id={id}");
                    break;
                case AppStoreKind.AppleAppStore:
                    var appleId = store.AppId.StartsWith("id", StringComparison.OrdinalIgnoreCase) ? store.AppId : "id" + store.AppId;
                    candidates.Add($"itms-apps://apps.apple.com/app/{appleId}");
                    candidates.Add($"https://apps.apple.com/app/{appleId}");
                    break;
                case AppStoreKind.MicrosoftStore:
                    candidates.Add($"ms-windows-store://pdp/?ProductId={id}");
                    candidates.Add($"https://apps.microsoft.com/detail/{id}");
                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(target))
            candidates.Insert(IsWebUrl(target) ? candidates.Count : 0, target);

        foreach (var candidate in candidates.Distinct())
        {
            if (IsWebUrl(candidate))
            {
                await OpenLinkAsync(candidate);
                return;
            }

            if (await launcher.TryOpenAsync(candidate)) return;
        }

        await OpenLinkAsync(UrlOf(content));
    }

    // ---------- Wi-Fi ----------

    private async Task ConnectWifiAsync(QrContent content, string? target, CancellationToken cancellationToken)
    {
        // The action target is the raw "WIFI:" payload; the parsed content is used when it is already Wi-Fi.
        var network = content as WifiContent;
        if (network is null && !string.IsNullOrWhiteSpace(target))
        {
            try { network = parser.Parse(target) as WifiContent; }
            catch (Exception ex) { logger.LogDebug(ex, "Wi-Fi payload could not be parsed"); }
        }

        if (network is null)
        {
            await dialogs.AlertAsync("No network", "This code does not contain Wi-Fi details.");
            return;
        }

        var hasPassword = !string.IsNullOrEmpty(network.Password);
        var outcome = await wifi.ConnectAsync(network, cancellationToken);

        if (outcome is not (WifiConnectOutcome.Connected or WifiConnectOutcome.Cancelled) && hasPassword)
            await clipboard.SetTextAsync(network.Password!);

        switch (outcome)
        {
            case WifiConnectOutcome.Connected:
                haptics.Success();
                await dialogs.ToastAsync($"Connected to {network.Ssid}");
                break;
            case WifiConnectOutcome.Prompted:
            case WifiConnectOutcome.Cancelled:
                break;
            case WifiConnectOutcome.Suggested:
                await dialogs.AlertAsync("Network added",
                    $"\"{network.Ssid}\" was suggested to Android. Allow the suggestion if asked and your device joins when the network is in range." +
                    (hasPassword ? " The password is also on your clipboard." : string.Empty));
                break;
            case WifiConnectOutcome.OpenedSettings:
                await dialogs.ToastAsync(hasPassword
                    ? $"Password copied. Choose \"{network.Ssid}\" and paste it."
                    : $"Choose \"{network.Ssid}\" in the Wi-Fi list.", longDuration: true);
                break;
            default:
                await dialogs.AlertAsync("Join manually",
                    $"Automatic joining isn't available here. Open Wi-Fi settings and choose \"{network.Ssid}\"." +
                    (hasPassword ? " The password was copied to the clipboard." : string.Empty));
                break;
        }
    }

    // ---------- Contacts & calendar ----------

    private async Task AddContactAsync(QrContent content, string? target, CancellationToken cancellationToken)
    {
        var vcard = ContentDocuments.VCardFor(content, target);
        if (vcard is null)
        {
            await dialogs.AlertAsync("No contact", "This code does not contain contact details.");
            return;
        }

        var name = content switch
        {
            ContactContent c when !string.IsNullOrWhiteSpace(c.FullName) => c.FullName,
            MeCardContent m => m.Name,
            _ => "contact",
        };
        var path = await files.WriteTempFileAsync(Encoding.UTF8.GetBytes(vcard), FileNameFor(name, ".vcf"), cancellationToken);
        if (!await launcher.OpenFileAsync(path, "text/x-vcard", "Add contact"))
            await share.ShareFileAsync("Add contact", path, "text/x-vcard");
    }

    private async Task AddToCalendarAsync(QrContent content, string? target, CancellationToken cancellationToken)
    {
        var ics = ContentDocuments.CalendarFor(content, target);
        if (ics is null)
        {
            await dialogs.AlertAsync("No event", "This code does not contain an event.");
            return;
        }

        var title = (content as CalendarEventContent)?.Title ?? "event";
        var path = await files.WriteTempFileAsync(Encoding.UTF8.GetBytes(ics), FileNameFor(title, ".ics"), cancellationToken);
        if (!await launcher.OpenFileAsync(path, "text/calendar", "Add to calendar"))
            await share.ShareFileAsync("Add to calendar", path, "text/calendar");
    }

    // ---------- Communication ----------

    private async Task CallAsync(string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            await dialogs.AlertAsync("No number", "This code does not contain a phone number.");
            return;
        }

        if (PhoneDialer.Default.IsSupported)
        {
            await MainThread.InvokeOnMainThreadAsync(() => PhoneDialer.Default.Open(number));
            return;
        }

        if (!await launcher.TryOpenAsync("tel:" + number))
            await CopyFallbackAsync(number, "This device can't make calls. The number was copied to the clipboard.");
    }

    private async Task SendSmsAsync(QrContent content, string? target)
    {
        var (number, body) = content switch
        {
            SmsContent s => (s.Number, s.Message),
            _ => (PhoneOf(content, target), null),
        };

        if (string.IsNullOrWhiteSpace(number))
        {
            await dialogs.AlertAsync("No number", "This code does not contain a phone number.");
            return;
        }

        if (Sms.Default.IsComposeSupported)
        {
            await MainThread.InvokeOnMainThreadAsync(() => Sms.Default.ComposeAsync(new SmsMessage(body ?? string.Empty, number)));
            return;
        }

        var uri = "sms:" + number + (string.IsNullOrEmpty(body) ? string.Empty : "?body=" + Uri.EscapeDataString(body));
        if (!await launcher.TryOpenAsync(uri))
            await CopyFallbackAsync(body ?? number, "This device can't send text messages. The details were copied to the clipboard.");
    }

    private async Task SendEmailAsync(QrContent content, string? target)
    {
        var email = content as EmailContent;
        var to = email is not null ? Split(email.To) : Split(EmailOf(content, target));
        var cc = Split(email?.Cc);
        var bcc = Split(email?.Bcc);
        var subject = email?.Subject ?? string.Empty;
        var body = email?.Body ?? string.Empty;

        if (to.Count == 0)
        {
            await dialogs.AlertAsync("No address", "This code does not contain an email address.");
            return;
        }

        try
        {
            if (Email.Default.IsComposeSupported)
            {
                var message = new EmailMessage { To = to, Cc = cc, Bcc = bcc, Subject = subject, Body = body };
                await MainThread.InvokeOnMainThreadAsync(() => Email.Default.ComposeAsync(message));
                return;
            }
        }
        catch (FeatureNotSupportedException)
        {
            // Fall back to mailto: below.
        }

        var mailto = new StringBuilder("mailto:").Append(string.Join(',', to.Select(Uri.EscapeDataString)));
        var query = new List<string>();
        if (subject.Length > 0) query.Add("subject=" + Uri.EscapeDataString(subject));
        if (body.Length > 0) query.Add("body=" + Uri.EscapeDataString(body));
        if (cc.Count > 0) query.Add("cc=" + Uri.EscapeDataString(string.Join(',', cc)));
        if (bcc.Count > 0) query.Add("bcc=" + Uri.EscapeDataString(string.Join(',', bcc)));
        if (query.Count > 0) mailto.Append('?').Append(string.Join('&', query));

        if (!await launcher.TryOpenAsync(mailto.ToString()))
            await CopyFallbackAsync(string.Join(", ", to), "No email app is set up. The address was copied to the clipboard.");
    }

    /// <summary>WhatsApp app first (whatsapp://), then the https wa.me link from the action target.</summary>
    private async Task OpenWhatsAppAsync(QrContent content, string? target)
    {
        var (number, text) = content switch
        {
            WhatsAppContent w => (w.Number, w.Message),
            _ => (PhoneOf(content, IsWebUrl(target ?? string.Empty) ? null : target), null),
        };
        var digits = new string((number ?? string.Empty).Where(char.IsDigit).ToArray());
        var textQuery = string.IsNullOrEmpty(text) ? string.Empty : "text=" + Uri.EscapeDataString(text);

        if (digits.Length > 0 || textQuery.Length > 0)
        {
            var app = "whatsapp://send?" + (digits.Length > 0 ? "phone=" + digits + (textQuery.Length > 0 ? "&" : string.Empty) : string.Empty) + textQuery;
            if (await launcher.TryOpenAsync(app)) return;
        }

        var web = target is not null && IsWebUrl(target)
            ? target
            : "https://wa.me/" + digits + (textQuery.Length > 0 ? "?" + textQuery : string.Empty);
        await OpenLinkAsync(web);
    }

    /// <summary>Telegram app first (tg://resolve), then the https t.me link from the action target.</summary>
    private async Task OpenTelegramAsync(QrContent content, string? target)
    {
        var username = (content as TelegramContent)?.Username ?? string.Empty;
        if (username.Length == 0 && target is not null && IsWebUrl(target) &&
            Uri.TryCreate(target, UriKind.Absolute, out var link) && link.Host.EndsWith("t.me", StringComparison.OrdinalIgnoreCase))
            username = link.AbsolutePath.Trim('/');

        username = username.Trim().TrimStart('@');
        if (username.Length > 0 && !username.Contains('/', StringComparison.Ordinal) &&
            await launcher.TryOpenAsync("tg://resolve?domain=" + Uri.EscapeDataString(username)))
            return;

        await OpenLinkAsync(target is not null && IsWebUrl(target) ? target : "https://t.me/" + Uri.EscapeDataString(username));
    }

    // ---------- Maps ----------

    private async Task OpenMapAsync(QrContent content, string? target, bool navigate)
    {
        var options = new MapLaunchOptions
        {
            NavigationMode = navigate ? NavigationMode.Default : NavigationMode.None,
        };

        if (content is GeoContent geo || TryParseGeo(target, out geo))
        {
            options.Name = geo.Query;
            await MainThread.InvokeOnMainThreadAsync(() => Map.Default.OpenAsync(geo.Latitude, geo.Longitude, options));
            return;
        }

        var address = content switch
        {
            ContactContent { Address: { } a } => a,
            MeCardContent { Address: { } street } => new PostalAddress(Street: street),
            _ => null,
        };

        if (address is not null)
        {
            var placemark = new Placemark
            {
                Thoroughfare = address.Street,
                Locality = address.City,
                AdminArea = address.Region,
                PostalCode = address.PostalCode,
                CountryName = address.Country,
            };
            await MainThread.InvokeOnMainThreadAsync(() => Map.Default.OpenAsync(placemark, options));
            return;
        }

        if (!string.IsNullOrWhiteSpace(target))
        {
            await OpenLinkAsync("https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(target));
            return;
        }

        await dialogs.AlertAsync("No location", "This code does not contain a location.");
    }

    // ---------- Payments ----------

    private async Task OpenPaymentAsync(QrContent content, string? target)
    {
        var uri = target is not null && target.Contains(':', StringComparison.Ordinal) ? target : null;

        switch (content)
        {
            case CryptoPaymentContent crypto:
                if (await launcher.TryOpenAsync(uri ?? PayloadOf(content))) return;
                await CopyFallbackAsync(crypto.Address, "No wallet app was found. The address was copied to the clipboard.");
                return;

            case UpiPaymentContent upi:
                if (await launcher.TryOpenAsync(uri ?? PayloadOf(content))) return;
                await CopyFallbackAsync(upi.PayeeAddress, "No UPI app was found. The UPI ID was copied to the clipboard.");
                return;

            case EpcPaymentContent epc:
                if (uri is not null && await launcher.TryOpenAsync(uri)) return;
                await clipboard.SetTextAsync(epc.Iban);
                var amount = epc.Amount is { } a ? $" Amount: €{a.ToString("0.00", CultureInfo.InvariantCulture)}." : string.Empty;
                await dialogs.AlertAsync("IBAN copied",
                    $"Paste the IBAN into your banking app to pay {epc.BeneficiaryName}.{amount}");
                return;

            default:
                if (uri is not null && await launcher.TryOpenAsync(uri)) return;
                await CopyFallbackAsync(target ?? PayloadOf(content), "No payment app was found. The details were copied to the clipboard.");
                return;
        }
    }

    // ---------- Secure QR ----------

    private async Task DecryptAsync(QrContent content, string? target, CancellationToken cancellationToken)
    {
        // Decrypting is free so recipients can always read what a Pro user sent them.
        var envelope = (content as SecureMessageContent)?.Envelope ?? target ?? PayloadOf(content);
        if (string.IsNullOrEmpty(envelope) || !protector.IsProtected(envelope))
        {
            await dialogs.AlertAsync("Not encrypted", "This code is not a QuickQr secure message.");
            return;
        }

        var passphrase = await dialogs.PromptAsync("Encrypted message", "Enter the passphrase you were given.",
            "Decrypt", "Cancel", placeholder: "Passphrase");
        if (string.IsNullOrEmpty(passphrase)) return;

        // Key derivation is CPU heavy: keep it off the UI thread.
        var result = await Task.Run(() => protector.Unprotect(envelope, passphrase), cancellationToken);
        if (result.IsFailure)
        {
            haptics.Error();
            await dialogs.AlertAsync("Couldn't decrypt", "The passphrase is wrong or the code is damaged.");
            return;
        }

        haptics.Success();
        if (await dialogs.ConfirmAsync("Decrypted message", result.Value, "Copy", "Close"))
            await CopyAsync(result.Value, "Message copied");
    }

    // ---------- Helpers ----------

    private async Task CopyFallbackAsync(string? text, string message)
    {
        if (!string.IsNullOrEmpty(text)) await clipboard.SetTextAsync(text);
        await dialogs.ToastAsync(message, longDuration: true);
    }

    private string PayloadOf(QrContent content)
    {
        try
        {
            var encoded = encoder.Encode(content);
            if (encoded.IsSuccess) return encoded.Value;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not re-encode {Type}", content.Type);
        }

        return content switch
        {
            TextContent t => t.Text,
            UrlContent u => u.Url,
            ProductContent p => p.Code,
            SecureMessageContent s => s.Envelope,
            _ => string.Empty,
        };
    }

    private static string? UrlOf(QrContent content) => content switch
    {
        UrlContent u => u.Url,
        SocialProfileContent s => SocialUrl(s),
        ContactContent c => c.Website,
        MeCardContent m => m.Url,
        TextContent t when IsWebUrl(t.Text.Trim()) => t.Text.Trim(),
        _ => null,
    };

    internal static string SocialUrl(SocialProfileContent s)
    {
        var handle = s.Handle.Trim();
        if (IsWebUrl(handle)) return handle;
        handle = Uri.EscapeDataString(handle.TrimStart('@'));
        return s.Network switch
        {
            SocialNetwork.Instagram => $"https://www.instagram.com/{handle}",
            SocialNetwork.Facebook => $"https://www.facebook.com/{handle}",
            SocialNetwork.X => $"https://x.com/{handle}",
            SocialNetwork.LinkedIn => $"https://www.linkedin.com/in/{handle}",
            SocialNetwork.TikTok => $"https://www.tiktok.com/@{handle}",
            SocialNetwork.YouTube => $"https://www.youtube.com/@{handle}",
            SocialNetwork.GitHub => $"https://github.com/{handle}",
            SocialNetwork.Snapchat => $"https://www.snapchat.com/add/{handle}",
            SocialNetwork.Pinterest => $"https://www.pinterest.com/{handle}",
            SocialNetwork.Threads => $"https://www.threads.net/@{handle}",
            _ => $"https://www.google.com/search?q={handle}",
        };
    }

    private static string? PhoneOf(QrContent content, string? target)
    {
        if (!string.IsNullOrWhiteSpace(target))
        {
            var t = target.Trim();
            foreach (var prefix in new[] { "tel:", "sms:", "smsto:" })
            {
                if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    t = t[prefix.Length..].Split(['?', ':'], 2)[0];
            }

            if (t.Any(char.IsDigit)) return t;
        }

        return content switch
        {
            PhoneContent p => p.Number,
            SmsContent s => s.Number,
            WhatsAppContent w => w.Number,
            ContactContent c => c.Phones.FirstOrDefault()?.Number,
            MeCardContent m => m.Phone,
            _ => null,
        };
    }

    private static string? EmailOf(QrContent content, string? target)
    {
        if (!string.IsNullOrWhiteSpace(target))
        {
            var t = target.Trim();
            if (t.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) t = t[7..].Split('?', 2)[0];
            if (t.Contains('@', StringComparison.Ordinal)) return Uri.UnescapeDataString(t);
        }

        return content switch
        {
            EmailContent e => e.To,
            ContactContent c => c.Emails.FirstOrDefault(),
            MeCardContent m => m.Email,
            _ => null,
        };
    }

    private static List<string> Split(string? addresses) =>
        string.IsNullOrWhiteSpace(addresses)
            ? []
            : addresses.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static bool TryParseGeo(string? target, out GeoContent geo)
    {
        geo = null!;
        if (string.IsNullOrWhiteSpace(target)) return false;
        var t = target.Trim();
        if (t.StartsWith("geo:", StringComparison.OrdinalIgnoreCase)) t = t[4..];
        var coordinates = t.Split(['?', ';'], 2)[0].Split(',');
        if (coordinates.Length < 2 ||
            !double.TryParse(coordinates[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
            !double.TryParse(coordinates[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            return false;

        geo = new GeoContent(lat, lon);
        return true;
    }

    private static string NormalizeUrl(string url)
    {
        var trimmed = url.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Scheme) && trimmed.Contains(':', StringComparison.Ordinal))
            return trimmed;
        return "https://" + trimmed;
    }

    private static bool IsWebUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private static string FileNameFor(string name, string extension)
    {
        var safe = new string(name.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray()).Trim();
        if (safe.Length == 0) safe = "quickqr";
        if (safe.Length > 40) safe = safe[..40];
        return safe + extension;
    }
}
