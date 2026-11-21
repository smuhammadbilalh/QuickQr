using Microsoft.Extensions.Logging;
using QuickQr.Core.Licensing;
using QuickQr.Core.Settings;
using QuickQr.Services;

namespace QuickQr;

public partial class App : Application
{
    private readonly AppShell _shell;
    private readonly IAppSettingsService _settings;
    private readonly IThemeService _theme;
    private readonly IEntitlementService _entitlements;
    private readonly IEnumerable<IAppLifecycleHook> _hooks;
    private readonly ILogger<App> _logger;

    public App(
        AppShell shell,
        IAppSettingsService settings,
        IThemeService theme,
        IEntitlementService entitlements,
        IEnumerable<IAppLifecycleHook> hooks,
        ILogger<App> logger)
    {
        InitializeComponent();
        _shell = shell;
        _settings = settings;
        _theme = theme;
        _entitlements = entitlements;
        _hooks = hooks;
        _logger = logger;

        _theme.Apply(_settings.Current.Theme);
        _settings.SettingsChanged += (_, s) => MainThread.BeginInvokeOnMainThread(() => _theme.Apply(s.Theme));
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_shell)
        {
            Title = "QuickQr",
            MinimumWidth = 400,
            MinimumHeight = 640,
        };

        if (DeviceInfo.Current.Idiom == DeviceIdiom.Desktop)
        {
            window.Width = 480;
            window.Height = 880;
        }

        return window;
    }

    protected override async void OnStart()
    {
        base.OnStart();
        try
        {
            await _entitlements.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Entitlement initialisation failed; continuing on cached tier");
        }

        await RunHooksAsync(h => h.OnStartAsync(), nameof(IAppLifecycleHook.OnStartAsync));
    }

    protected override async void OnResume()
    {
        base.OnResume();
        await RunHooksAsync(h => h.OnResumeAsync(), nameof(IAppLifecycleHook.OnResumeAsync));
    }

    protected override void OnSleep()
    {
        base.OnSleep();
        foreach (var hook in _hooks)
        {
            try { hook.OnSleep(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Lifecycle hook {Hook} failed in OnSleep", hook.GetType().Name); }
        }
    }

    private async Task RunHooksAsync(Func<IAppLifecycleHook, Task> run, string stage)
    {
        foreach (var hook in _hooks)
        {
            try { await run(hook); }
            catch (Exception ex) { _logger.LogWarning(ex, "Lifecycle hook {Hook} failed in {Stage}", hook.GetType().Name, stage); }
        }
    }
}
