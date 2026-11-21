using QuickQr.Core;
using QuickQr.Core.Licensing;
using QuickQr.Core.Rendering;
using QuickQr.Infrastructure.Branding;
using QuickQr.Infrastructure.Tests.Fakes;

namespace QuickQr.Infrastructure.Tests.Branding;

public sealed class FileBrandKitStoreTests : IDisposable
{
    private static readonly DateTimeOffset Created = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly TempDirectory _temp = new();
    private readonly IEntitlementService _entitlements = Substitute.For<IEntitlementService>();

    public FileBrandKitStoreTests() => _entitlements.HasFeature(PremiumFeature.BrandKits).Returns(true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Empty_when_no_file_exists()
    {
        (await Create().GetAllAsync(Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Saves_and_reloads_kits_including_logo_bytes()
    {
        var kit = new BrandKit(Guid.NewGuid(), "  Acme  ", StylePresets.All[2].Style with { Logo = new LogoOptions([1, 2, 3, 4]) }, Created);

        var result = await Create().SaveAsync(kit, Ct);

        result.IsSuccess.Should().BeTrue();
        File.Exists(_temp.File("brandkits.json")).Should().BeTrue();
        File.Exists(_temp.File("brandkits.json.tmp")).Should().BeFalse("writes are atomic");

        var loaded = (await Create().GetAllAsync(Ct)).Should().ContainSingle().Subject;
        loaded.Id.Should().Be(kit.Id);
        loaded.Name.Should().Be("Acme");
        loaded.CreatedAt.Should().Be(Created);
        loaded.Style.Logo!.ImageBytes.Should().Equal(1, 2, 3, 4);
        loaded.Style.Should().BeEquivalentTo(kit.Style);
    }

    [Fact]
    public async Task Upserts_by_id()
    {
        var store = Create();
        var kit = new BrandKit(Guid.NewGuid(), "One", QrStyle.Default, Created);
        await store.SaveAsync(kit, Ct);
        await store.SaveAsync(new BrandKit(Guid.NewGuid(), "Two", QrStyle.Default, Created), Ct);

        await store.SaveAsync(kit with { Name = "One (renamed)" }, Ct);

        (await Create().GetAllAsync(Ct)).Select(k => k.Name).Should().Equal("One (renamed)", "Two");
    }

    [Fact]
    public async Task Saving_is_locked_on_the_free_tier_but_reading_and_deleting_are_not()
    {
        var store = Create();
        var kit = new BrandKit(Guid.NewGuid(), "Kit", QrStyle.Default, Created);
        await store.SaveAsync(kit, Ct);
        _entitlements.HasFeature(PremiumFeature.BrandKits).Returns(false);

        var result = await store.SaveAsync(kit with { Id = Guid.NewGuid() }, Ct);

        result.Error.Code.Should().Be(ErrorCodes.FeatureLocked);
        (await store.GetAllAsync(Ct)).Should().ContainSingle();
        await store.DeleteAsync(kit.Id, Ct);
        (await Create().GetAllAsync(Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Allows_at_most_fifty_kits()
    {
        var store = Create();
        for (var i = 0; i < 50; i++)
            (await store.SaveAsync(new BrandKit(Guid.NewGuid(), $"Kit {i}", QrStyle.Default, Created), Ct)).IsSuccess.Should().BeTrue();

        var result = await store.SaveAsync(new BrandKit(Guid.NewGuid(), "One too many", QrStyle.Default, Created), Ct);

        result.Error.Code.Should().Be(ErrorCodes.Validation);
        var existing = (await store.GetAllAsync(Ct))[0];
        (await store.SaveAsync(existing with { Name = "Updating is still fine" }, Ct)).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Requires_a_name(string name)
    {
        var result = await Create().SaveAsync(new BrandKit(Guid.NewGuid(), name, QrStyle.Default, Created), Ct);

        result.Error.Code.Should().Be(ErrorCodes.Validation);
    }

    [Fact]
    public async Task Corrupt_file_is_set_aside_and_treated_as_empty()
    {
        await File.WriteAllTextAsync(_temp.File("brandkits.json"), "{definitely not json", Ct);

        (await Create().GetAllAsync(Ct)).Should().BeEmpty();
        File.Exists(_temp.File("brandkits.json.corrupt")).Should().BeTrue();
    }

    [Fact]
    public async Task Delete_of_unknown_id_is_a_no_op()
    {
        var store = Create();

        await store.DeleteAsync(Guid.NewGuid(), Ct);

        File.Exists(_temp.File("brandkits.json")).Should().BeFalse();
    }

    private FileBrandKitStore Create() => new(new FakeAppInfoProvider(_temp.Path), _entitlements);
}
