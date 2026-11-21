using QuickQr.Core.Barcodes;
using QuickQr.Core.Platform;
using QuickQr.Core.Settings;
using QuickQr.Infrastructure.Settings;

namespace QuickQr.Infrastructure.Tests.Settings;

public sealed class AppSettingsServiceTests
{
    private readonly InMemoryKeyValueStore _store = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Defaults_when_nothing_is_stored() =>
        new AppSettingsService(_store).Current.Should().Be(new AppSettings());

    [Fact]
    public async Task Save_round_trips_through_the_store_and_raises_the_event()
    {
        var service = new AppSettingsService(_store);
        var changed = new List<AppSettings>();
        service.SettingsChanged += (_, s) => changed.Add(s);
        var settings = new AppSettings
        {
            Theme = ThemePreference.Dark,
            BeepOnScan = true,
            VibrateOnScan = false,
            DefaultErrorCorrection = ErrorCorrectionLevel.High,
            DefaultFormat = BarcodeFormat.DataMatrix,
            DefaultExportSize = 2048,
            HasCompletedOnboarding = true
        };

        await service.SaveAsync(settings, Ct);

        service.Current.Should().Be(settings);
        changed.Should().Equal(settings);
        _store.Get("settings.v1").Should().Contain("\"theme\":\"Dark\"");
        new AppSettingsService(_store).Current.Should().Be(settings);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("null")]
    [InlineData("[1,2,3]")]
    [InlineData("   ")]
    public void Corrupt_json_falls_back_to_defaults(string stored)
    {
        _store.Set("settings.v1", stored);

        new AppSettingsService(_store).Current.Should().Be(new AppSettings());
    }

    [Fact]
    public void Unknown_properties_are_ignored_and_missing_ones_keep_defaults()
    {
        _store.Set("settings.v1", """{"theme":"Light","futureSetting":42}""");

        var current = new AppSettingsService(_store).Current;

        current.Theme.Should().Be(ThemePreference.Light);
        current.SaveScansToHistory.Should().BeTrue();
    }

    [Fact]
    public async Task Reset_restores_defaults_and_clears_storage()
    {
        var service = new AppSettingsService(_store);
        await service.SaveAsync(new AppSettings { Theme = ThemePreference.Dark }, Ct);
        AppSettings? raised = null;
        service.SettingsChanged += (_, s) => raised = s;

        await service.ResetAsync(Ct);

        service.Current.Should().Be(new AppSettings());
        raised.Should().Be(new AppSettings());
        _store.Get("settings.v1").Should().BeNull();
    }

    [Fact]
    public async Task Concurrent_saves_and_reads_are_safe()
    {
        var service = new AppSettingsService(_store);

        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(async () =>
        {
            await service.SaveAsync(new AppSettings { DefaultExportSize = 100 + i }, Ct);
            _ = service.Current;
        }, Ct)));

        new AppSettingsService(_store).Current.Should().Be(service.Current);
    }
}
