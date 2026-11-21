using CommunityToolkit.Mvvm.ComponentModel;
using QuickQr.Controls;
using QuickQr.Core.Content;
using QuickQr.Core.Security;

namespace QuickQr.Features.Generate;

public sealed partial class AppStoreFormViewModel : ContentFormViewModel
{
    public AppStoreFormViewModel()
    {
        Store = new OptionGroup<AppStoreKind>()
            .Add(AppStoreKind.GooglePlay, "Google Play")
            .Add(AppStoreKind.AppleAppStore, "App Store")
            .Add(AppStoreKind.MicrosoftStore, "Microsoft Store");
        Store.SelectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Store));
            OnPropertyChanged(nameof(AppIdPlaceholder));
            OnPropertyChanged(nameof(AppIdHint));
        };
    }

    public override ContentKind Kind => ContentKind.AppStore;

    public OptionGroup<AppStoreKind> Store { get; }

    [ObservableProperty]
    public partial string AppId { get; set; } = string.Empty;

    public string AppIdPlaceholder => Store.Value switch
    {
        AppStoreKind.AppleAppStore => "1234567890",
        AppStoreKind.MicrosoftStore => "9NBLGGH4NNS1",
        _ => "com.example.app"
    };

    public string AppIdHint => Store.Value switch
    {
        AppStoreKind.AppleAppStore => "The numeric id from the App Store link (…/id1234567890).",
        AppStoreKind.MicrosoftStore => "The 12-character product id from the Microsoft Store link.",
        _ => "The package name from the Google Play link (…?id=com.example.app)."
    };

    public override bool IsEmpty => string.IsNullOrWhiteSpace(AppId);

    public override QrContent BuildContent() => new AppStoreContent(Store.Value, Required(AppId));

    public override bool TryLoad(QrContent content)
    {
        if (content is not AppStoreContent app) return false;
        Store.Value = app.Store;
        AppId = app.AppId;
        return true;
    }
}

public sealed partial class SocialFormViewModel : ContentFormViewModel
{
    public SocialFormViewModel()
    {
        Network = new OptionGroup<SocialNetwork>()
            .Add(SocialNetwork.Instagram, "Instagram")
            .Add(SocialNetwork.Facebook, "Facebook")
            .Add(SocialNetwork.X, "X")
            .Add(SocialNetwork.LinkedIn, "LinkedIn")
            .Add(SocialNetwork.TikTok, "TikTok")
            .Add(SocialNetwork.YouTube, "YouTube")
            .Add(SocialNetwork.GitHub, "GitHub")
            .Add(SocialNetwork.Snapchat, "Snapchat")
            .Add(SocialNetwork.Pinterest, "Pinterest")
            .Add(SocialNetwork.Threads, "Threads");
        Network.SelectionChanged += (_, _) => OnPropertyChanged(nameof(Network));
    }

    public override ContentKind Kind => ContentKind.Social;

    public OptionGroup<SocialNetwork> Network { get; }

    [ObservableProperty]
    public partial string Handle { get; set; } = string.Empty;

    public override bool IsEmpty => string.IsNullOrWhiteSpace(Handle);

    public override QrContent BuildContent() => new SocialProfileContent(Network.Value, Required(Handle).TrimStart('@'));

    public override bool TryLoad(QrContent content)
    {
        if (content is not SocialProfileContent social) return false;
        Network.Value = social.Network;
        Handle = social.Handle;
        return true;
    }
}

/// <summary>Retail barcode digits (EAN-8, EAN-13, UPC-A, UPC-E).</summary>
public sealed partial class ProductFormViewModel : ContentFormViewModel
{
    public override ContentKind Kind => ContentKind.Product;

    [ObservableProperty]
    public partial string Code { get; set; } = string.Empty;

    public override bool IsEmpty => string.IsNullOrWhiteSpace(Code);

    public override QrContent BuildContent() =>
        new ProductContent(Required(Code).Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal));

    public override bool TryLoad(QrContent content)
    {
        if (content is not ProductContent product) return false;
        Code = product.Code;
        return true;
    }
}

/// <summary>Passphrase-encrypted message (Secure QR, Pro). Encryption runs off the UI thread and is cached per input.</summary>
public sealed partial class SecureMessageFormViewModel(ISecurePayloadProtector protector) : ContentFormViewModel
{
    public const int MinimumPassphraseLength = 8;

    private (string Message, string Passphrase, string Envelope)? _cache;

    public override ContentKind Kind => ContentKind.Secure;

    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Passphrase { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfirmPassphrase { get; set; } = string.Empty;

    public override bool IsEmpty => string.IsNullOrEmpty(Message);

    public override string? Validate()
    {
        if (Passphrase.Length < MinimumPassphraseLength)
            return $"Use a passphrase of at least {MinimumPassphraseLength} characters.";
        if (!string.Equals(Passphrase, ConfirmPassphrase, StringComparison.Ordinal))
            return "The passphrases don't match.";
        return null;
    }

    /// <summary>The last envelope produced by <see cref="BuildContentAsync"/> (empty before the first build).</summary>
    public override QrContent BuildContent() => new SecureMessageContent(_cache?.Envelope ?? string.Empty);

    public override async Task<QrContent> BuildContentAsync(CancellationToken cancellationToken)
    {
        var message = Message;
        var passphrase = Passphrase;
        if (_cache is { } cached && cached.Message == message && cached.Passphrase == passphrase)
            return new SecureMessageContent(cached.Envelope);

        // Key derivation is deliberately slow: keep it off the UI thread.
        var envelope = await Task.Run(() => protector.Protect(message, passphrase), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _cache = (message, passphrase, envelope);
        return new SecureMessageContent(envelope);
    }

    /// <summary>Encrypted payloads can't be edited without the passphrase; the user types the message again.</summary>
    public override bool TryLoad(QrContent content) => content is SecureMessageContent;
}
