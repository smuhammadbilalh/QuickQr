using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QuickQr.Mvvm;
using QuickQr.Services;

namespace QuickQr.Features.About;

public sealed class LicenseItem(string name, string license, string url, IAsyncRelayCommand<string> open)
{
    public string Name { get; } = name;
    public string License { get; } = license;
    public string Url { get; } = url;
    public IAsyncRelayCommand<string> OpenCommand { get; } = open;
}

/// <summary>Third-party components shipped in QuickQr and their licences.</summary>
public sealed partial class LicensesViewModel : ViewModelBase
{
    private readonly IExternalLauncher _launcher;

    public LicensesViewModel(IDialogService dialogs, ILogger<LicensesViewModel> logger, IExternalLauncher launcher)
        : base(dialogs, logger)
    {
        _launcher = launcher;
        Title = "Open-source licences";
        var open = new AsyncRelayCommand<string>(OpenAsync);
        Items =
        [
            new(".NET and .NET MAUI", "MIT License", "https://github.com/dotnet/maui", open),
            new(".NET Community Toolkit (MVVM)", "MIT License", "https://github.com/CommunityToolkit/dotnet", open),
            new(".NET MAUI Community Toolkit", "MIT License", "https://github.com/CommunityToolkit/Maui", open),
            new("BarcodeScanning.Native.Maui", "MIT License", "https://github.com/afriscic/BarcodeScanning.Native.Maui", open),
            new("Google ML Kit barcode scanning (Android)", "ML Kit Terms of Service", "https://developers.google.com/ml-kit/terms", open),
            new("zxing-cpp (Windows scanning)", "Apache License 2.0", "https://github.com/zxing-cpp/zxing-cpp", open),
            new("ZXing.Net", "Apache License 2.0", "https://github.com/micjahn/ZXing.Net", open),
            new("SkiaSharp", "MIT License", "https://github.com/mono/SkiaSharp", open),
            new("Microsoft.Data.Sqlite", "MIT License", "https://github.com/dotnet/efcore", open),
            new("SQLite", "Public domain", "https://www.sqlite.org/copyright.html", open),
            new("Plugin.InAppBilling", "MIT License", "https://github.com/jamesmontemagno/InAppBillingPlugin", open),
            new("Material Symbols", "Apache License 2.0", "https://github.com/google/material-design-icons", open),
            new("Bootstrap Icons (brand marks)", "MIT License", "https://github.com/twbs/icons", open),
        ];
    }

    public List<LicenseItem> Items { get; }

    private async Task OpenAsync(string? url)
    {
        if (string.IsNullOrEmpty(url)) return;
        if (!await _launcher.OpenUrlAsync(url))
            await Dialogs.AlertAsync("Couldn't open the link", url);
    }
}
