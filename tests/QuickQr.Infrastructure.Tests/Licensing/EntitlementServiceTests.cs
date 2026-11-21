using Microsoft.Extensions.Time.Testing;
using QuickQr.Core.Licensing;
using QuickQr.Infrastructure.Licensing;
using QuickQr.Infrastructure.Tests.Fakes;

namespace QuickQr.Infrastructure.Tests.Licensing;

public sealed class EntitlementServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _clock = new(Start);
    private readonly InMemorySecureKeyValueStore _secure = new();
    private readonly IPurchaseProvider _provider = Substitute.For<IPurchaseProvider>();

    public EntitlementServiceTests()
    {
        _provider.Name.Returns("Fake store");
        _provider.IsSupported.Returns(true);
        _provider.GetOwnedPurchasesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<OwnedPurchase>>([]));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Starts_free_and_stays_free_without_purchases()
    {
        var service = Create();

        await service.InitializeAsync(Ct);

        service.Tier.Should().Be(SubscriptionTier.Free);
        service.HasFeature(PremiumFeature.BatchGeneration).Should().BeFalse();
        service.HasFeature(PremiumFeature.UnlimitedHistory).Should().BeTrue("unlimited history is free");
    }

    [Fact]
    public async Task Uses_the_highest_tier_among_active_purchases_and_caches_it()
    {
        Owns(Purchase(ProductIds.ProLifetime), Purchase(ProductIds.BusinessMonthly, expiresAt: Start.AddDays(-1)));
        var service = Create();

        await service.InitializeAsync(Ct);

        service.Tier.Should().Be(SubscriptionTier.Pro, "the Business subscription has expired");
        service.HasFeature(PremiumFeature.BrandKits).Should().BeTrue();
        service.HasFeature(PremiumFeature.BatchGeneration).Should().BeFalse();
        service.HasFeature(PremiumFeature.UrlReputation).Should().BeFalse("coming-soon features stay locked");
        _secure.Values.Should().ContainKey("entitlement.v1").WhoseValue.Should().Contain("\"tier\":\"Pro\"").And.Contain(ProductIds.ProLifetime);

        Owns(Purchase(ProductIds.ProLifetime), Purchase(ProductIds.BusinessLifetime));
        await service.InitializeAsync(Ct);
        service.Tier.Should().Be(SubscriptionTier.Business);
        service.HasFeature(PremiumFeature.BatchGeneration).Should().BeTrue();
    }

    [Fact]
    public async Task Paid_users_start_with_their_cached_tier_when_offline()
    {
        Owns(Purchase(ProductIds.BusinessLifetime));
        await Create().InitializeAsync(Ct);

        _provider.GetOwnedPurchasesAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromException<IReadOnlyList<OwnedPurchase>>(new HttpRequestException("offline")));
        _clock.Advance(TimeSpan.FromDays(365));
        var restarted = Create();
        await restarted.InitializeAsync(Ct);

        restarted.Tier.Should().Be(SubscriptionTier.Business, "one-time purchases never expire offline");
    }

    [Fact]
    public async Task Cached_subscription_survives_a_seven_day_grace_period_after_expiry()
    {
        Owns(Purchase(ProductIds.ProMonthly, expiresAt: Start.AddDays(2)));
        await Create().InitializeAsync(Ct);
        _provider.GetOwnedPurchasesAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromException<IReadOnlyList<OwnedPurchase>>(new TimeoutException()));

        _clock.Advance(TimeSpan.FromDays(6));
        var withinGrace = Create();
        await withinGrace.InitializeAsync(Ct);
        withinGrace.Tier.Should().Be(SubscriptionTier.Pro, "the store was last reached 6 days ago");

        _clock.Advance(TimeSpan.FromDays(2));
        var afterGrace = Create();
        await afterGrace.InitializeAsync(Ct);
        afterGrace.Tier.Should().Be(SubscriptionTier.Free, "expired and not validated for 8 days");
    }

    [Fact]
    public async Task Cached_subscription_is_honoured_until_expiry_without_validation()
    {
        Owns(Purchase(ProductIds.ProYearly, expiresAt: Start.AddDays(300)));
        await Create().InitializeAsync(Ct);
        _provider.IsSupported.Returns(false);

        _clock.Advance(TimeSpan.FromDays(100));
        var service = Create();
        await service.InitializeAsync(Ct);

        service.Tier.Should().Be(SubscriptionTier.Pro);
    }

    [Fact]
    public async Task A_successful_refresh_is_authoritative()
    {
        Owns(Purchase(ProductIds.ProLifetime));
        await Create().InitializeAsync(Ct);

        Owns(); // refunded
        var service = Create();
        await service.InitializeAsync(Ct);

        service.Tier.Should().Be(SubscriptionTier.Free);
    }

    [Fact]
    public async Task Corrupt_or_unreadable_cache_starts_free()
    {
        await _secure.SetAsync("entitlement.v1", "{broken");
        _provider.IsSupported.Returns(false);
        var service = Create();
        await service.InitializeAsync(Ct);
        service.Tier.Should().Be(SubscriptionTier.Free);

        _secure.ThrowOnAccess = true;
        var unreadable = Create();
        await unreadable.InitializeAsync(Ct);
        unreadable.Tier.Should().Be(SubscriptionTier.Free);
    }

    [Fact]
    public async Task Raises_entitlements_changed_only_on_tier_changes()
    {
        var service = Create();
        var raised = 0;
        service.EntitlementsChanged += (_, _) => Interlocked.Increment(ref raised);

        await service.InitializeAsync(Ct);
        raised.Should().Be(0);

        Owns(Purchase(ProductIds.ProLifetime));
        await service.InitializeAsync(Ct);
        await service.InitializeAsync(Ct);
        raised.Should().Be(1);
    }

    [Fact]
    public async Task Purchase_updates_the_tier_and_raises_the_event()
    {
        var purchase = Purchase(ProductIds.ProLifetime);
        _provider.PurchaseAsync(ProductIds.ProLifetime, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(PurchaseStatus, OwnedPurchase?, string?)>((PurchaseStatus.Purchased, purchase, null)));
        var service = Create();
        await service.InitializeAsync(Ct);
        using var monitor = service.Monitor();

        var outcome = await service.PurchaseAsync(ProductIds.ProLifetime, Ct);

        outcome.Should().Be(new PurchaseOutcome(PurchaseStatus.Purchased, SubscriptionTier.Pro));
        outcome.IsSuccess.Should().BeTrue();
        service.Tier.Should().Be(SubscriptionTier.Pro);
        monitor.Should().Raise(nameof(IEntitlementService.EntitlementsChanged));
        _secure.Values["entitlement.v1"].Should().Contain("Pro");
    }

    [Theory]
    [InlineData(PurchaseStatus.Cancelled)]
    [InlineData(PurchaseStatus.Pending)]
    [InlineData(PurchaseStatus.Failed)]
    public async Task Unsuccessful_purchases_keep_the_tier(PurchaseStatus status)
    {
        _provider.PurchaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(PurchaseStatus, OwnedPurchase?, string?)>((status, null, "nope")));
        var service = Create();

        var outcome = await service.PurchaseAsync(ProductIds.BusinessLifetime, Ct);

        outcome.Should().Be(new PurchaseOutcome(status, SubscriptionTier.Free, "nope"));
        service.Tier.Should().Be(SubscriptionTier.Free);
    }

    [Fact]
    public async Task Purchase_maps_exceptions_and_guards_invalid_requests()
    {
        _provider.PurchaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<(PurchaseStatus, OwnedPurchase?, string?)>(new InvalidOperationException("Billing unavailable")));
        var service = Create();

        (await service.PurchaseAsync(ProductIds.ProLifetime, Ct)).Should().Be(new PurchaseOutcome(PurchaseStatus.Failed, SubscriptionTier.Free, "Billing unavailable"));
        (await service.PurchaseAsync("com.other.app", Ct)).Status.Should().Be(PurchaseStatus.Failed);
        (await service.PurchaseAsync(ProductIds.BusinessUpgrade, Ct)).Message.Should().Contain("Pro owners");
    }

    [Fact]
    public async Task Owners_are_not_charged_twice()
    {
        Owns(Purchase(ProductIds.BusinessLifetime));
        var service = Create();
        await service.InitializeAsync(Ct);

        var outcome = await service.PurchaseAsync(ProductIds.ProLifetime, Ct);

        outcome.Status.Should().Be(PurchaseStatus.AlreadyOwned);
        _ = _provider.DidNotReceiveWithAnyArgs().PurchaseAsync(default!, Ct);
    }

    [Fact]
    public async Task Pro_owners_can_upgrade_to_business()
    {
        Owns(Purchase(ProductIds.ProLifetime));
        var upgrade = Purchase(ProductIds.BusinessUpgrade);
        _provider.PurchaseAsync(ProductIds.BusinessUpgrade, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(PurchaseStatus, OwnedPurchase?, string?)>((PurchaseStatus.Purchased, upgrade, null)));
        var service = Create();
        await service.InitializeAsync(Ct);

        var outcome = await service.PurchaseAsync(ProductIds.BusinessUpgrade, Ct);

        outcome.Tier.Should().Be(SubscriptionTier.Business, "the store's owned list may lag; the fresh purchase still counts");
    }

    [Fact]
    public async Task Purchase_is_not_supported_without_a_store()
    {
        _provider.IsSupported.Returns(false);

        (await Create().PurchaseAsync(ProductIds.ProLifetime, Ct)).Status.Should().Be(PurchaseStatus.NotSupported);
        (await Create().RestoreAsync(Ct)).Status.Should().Be(PurchaseStatus.NotSupported);
    }

    [Fact]
    public async Task Restore_reports_found_and_missing_purchases()
    {
        var service = Create();
        (await service.RestoreAsync(Ct)).Should().Match<PurchaseOutcome>(o => o.Status == PurchaseStatus.Failed && o.Message!.Contains("No purchases"));

        Owns(Purchase(ProductIds.ProLifetime));
        (await service.RestoreAsync(Ct)).Should().Be(new PurchaseOutcome(PurchaseStatus.Restored, SubscriptionTier.Pro));
        service.Tier.Should().Be(SubscriptionTier.Pro);

        _provider.GetOwnedPurchasesAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromException<IReadOnlyList<OwnedPurchase>>(new HttpRequestException("offline")));
        (await service.RestoreAsync(Ct)).Should().Be(new PurchaseOutcome(PurchaseStatus.Failed, SubscriptionTier.Pro, "offline"));
    }

    [Fact]
    public async Task Products_come_from_the_store_with_a_static_fallback()
    {
        _provider.GetProductsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StoreProduct>>(
            [
                new StoreProduct(ProductIds.BusinessLifetime, "Business", "", "€32,99", SubscriptionTier.Business, BillingPeriod.Lifetime),
                new StoreProduct(ProductIds.ProLifetime, "Pro", "", "€10,99", SubscriptionTier.Pro, BillingPeriod.Lifetime, true)
            ]));
        var service = Create();

        (await service.GetProductsAsync(Ct)).Select(p => p.LocalizedPrice).Should().Equal("€10,99", "€32,99");
        _ = _provider.Received().GetProductsAsync(Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(ProductIds.Offered)), Arg.Any<CancellationToken>());

        _provider.GetProductsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<IReadOnlyList<StoreProduct>>(new HttpRequestException()));
        var fallback = await service.GetProductsAsync(Ct);
        fallback.Select(p => p.ProductId).Should().Equal(ProductIds.Offered);
        fallback.Select(p => p.LocalizedPrice).Should().Equal("$9.99", "$29.99", "$19.99");

        _provider.IsSupported.Returns(false);
        (await service.GetProductsAsync(Ct)).Should().HaveCount(3);
    }

    [Fact]
    public void Compute_picks_lifetime_over_subscription_at_the_same_tier()
    {
        var state = EntitlementService.Compute(
            [Purchase(ProductIds.ProYearly, expiresAt: Start.AddDays(10)), Purchase(ProductIds.ProLifetime), Purchase("unknown.product")],
            Start);

        state.Should().Be(new EntitlementCache(SubscriptionTier.Pro, ProductIds.ProLifetime, null, Start));
    }

    private EntitlementService Create() => new(_provider, _secure, _clock);

    private void Owns(params OwnedPurchase[] purchases) =>
        _provider.GetOwnedPurchasesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<OwnedPurchase>>(purchases));

    private static OwnedPurchase Purchase(string productId, DateTimeOffset? expiresAt = null) =>
        new(productId, "token-" + productId, Start.AddDays(-30), expiresAt);
}
