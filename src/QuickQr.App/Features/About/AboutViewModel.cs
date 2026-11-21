using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QuickQr.Core.Content;
using QuickQr.Core.Platform;
using QuickQr.Mvvm;
using QuickQr.Services;

namespace QuickQr.Features.About;

public static class AboutRoutes
{
    /// <summary>Open-source licences page (pushed from Settings and About).</summary>
    public const string Licenses = "licenses";
}

/// <summary>Developer profile, contact links and app information (ported from the original DeveloperPage).</summary>
public sealed partial class AboutViewModel : ViewModelBase
{
    public const string DeveloperName = "Muhammad Bilal";
    public const string DeveloperRole = "IT Specialist and App Developer";
    public const string PortfolioUrl = "https://www.muhammadbilal.tararg.com";
    public const string GitHubUrl = "https://github.com/smuhammadbilalh";
    public const string XUrl = "https://x.com/Muhamma32151108";
    public const string InstagramUrl = "https://www.instagram.com/smuhammad_bilalh/";
    public const string LinkedInUrl = "https://www.linkedin.com/in/muhammadbilaltech/";
    public const string FacebookUrl = "https://www.facebook.com/muhammad.bilal.285914/";
    public const string WhatsAppBusinessUrl = "https://wa.me/message/ZUCBRGI34GZ4G1";
    public const string EmailAddress = "smuhammadbilalh@gmail.com";
    public const string PhoneNumber = "+9203255806291";
    public const string DemoVideoUrl = "https://drive.google.com/file/d/1yigcKBX3uGF0xIOzrXWMVxeoxgeN-Y2r/view?usp=sharing";
    public const string SourceCodeUrl = "https://github.com/smuhammadbilalh/QuickQr";

    private readonly IExternalLauncher _launcher;
    private readonly IContentActionExecutor _executor;
    private readonly INavigationService _navigation;

    public AboutViewModel(
        IDialogService dialogs,
        ILogger<AboutViewModel> logger,
        IExternalLauncher launcher,
        IContentActionExecutor executor,
        INavigationService navigation,
        IAppInfoProvider appInfo,
        TimeProvider clock) : base(dialogs, logger)
    {
        _launcher = launcher;
        _executor = executor;
        _navigation = navigation;

        Title = "About";
        AppName = string.IsNullOrWhiteSpace(appInfo.AppName) ? "QuickQr" : appInfo.AppName;
        VersionText = $"Version {appInfo.Version} (build {appInfo.Build}) · {appInfo.Platform}";
        Copyright = $"© {clock.GetLocalNow().Year} {DeveloperName}. All rights reserved.";
    }

    public string AppName { get; }
    public string VersionText { get; }
    public string Copyright { get; }
    public string Name => DeveloperName;
    public string Role => DeveloperRole;
    public string Initials => "MB";
    public string Email => EmailAddress;
    public string Phone => PhoneNumber;

    [RelayCommand]
    private Task OpenLinkAsync(string url) => OpenAsync(url);

    [RelayCommand]
    private Task OpenPortfolioAsync() => OpenAsync(PortfolioUrl);

    [RelayCommand]
    private Task OpenWhatsAppBusinessAsync() => OpenAsync(WhatsAppBusinessUrl);

    [RelayCommand]
    private Task WatchDemoAsync() => OpenAsync(DemoVideoUrl);

    [RelayCommand]
    private Task OpenSourceCodeAsync() => OpenAsync(SourceCodeUrl);

    [RelayCommand]
    private Task OpenLicensesAsync() => _navigation.GoToAsync(AboutRoutes.Licenses);

    [RelayCommand]
    private Task SendEmailAsync() =>
        _executor.ExecuteAsync(
            new ContentAction(ContentActionKind.SendEmail, "Email", "\ue159", Target: EmailAddress),
            new EmailContent(EmailAddress, "Wants to Build an Application", "Hi!"));

    [RelayCommand]
    private Task CallAsync() =>
        _executor.ExecuteAsync(
            new ContentAction(ContentActionKind.Call, "Call", "\uf0d4", Target: PhoneNumber),
            new PhoneContent(PhoneNumber));

    private async Task OpenAsync(string url)
    {
        try
        {
            if (!await _launcher.OpenUrlAsync(url))
                await Dialogs.AlertAsync("Couldn't open the link", "Please check your internet connection and try again.");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Opening {Url} failed", url);
            await Dialogs.AlertAsync("Couldn't open the link", "Please check your internet connection and try again.");
        }
    }
}
